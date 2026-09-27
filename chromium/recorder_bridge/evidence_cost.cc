#include "chromium/recorder_bridge/evidence_cost.h"

#include <array>
#include <atomic>
#include <chrono>
#include <mutex>
#include <string>
#include <string_view>

namespace a11y_recorder {

namespace {

constexpr int kMaximumCostKinds = 128;

struct CostSlot {
  std::atomic<const char*> name{nullptr};
  std::atomic<uint64_t> calls{0};
  std::atomic<uint64_t> total_nanoseconds{0};
  std::atomic<uint64_t> maximum_nanoseconds{0};
};

std::array<CostSlot, kMaximumCostKinds>& Slots() {
  static std::array<CostSlot, kMaximumCostKinds> slots;
  return slots;
}

// Leaked, so no destructor runs at exit while another thread may register.
std::mutex& RegistrationMutex() {
  static std::mutex* const mutex = new std::mutex;
  return *mutex;
}

std::atomic<int> registered_kinds{0};
std::atomic<CostReporter> reporter{nullptr};
std::atomic<int64_t> report_interval{0};
std::atomic<int64_t> next_report_at{0};
std::atomic<int64_t> previous_report_at{0};

thread_local std::array<int64_t, kMaximumCostKinds> span_started = {};

void Add(int slot, int64_t elapsed) {
  if (slot < 0 || elapsed < 0) {
    return;
  }
  CostSlot& target = Slots()[static_cast<size_t>(slot)];
  const uint64_t value = static_cast<uint64_t>(elapsed);
  target.calls.fetch_add(1, std::memory_order_relaxed);
  target.total_nanoseconds.fetch_add(value, std::memory_order_relaxed);
  uint64_t maximum = target.maximum_nanoseconds.load(std::memory_order_relaxed);
  while (value > maximum &&
         !target.maximum_nanoseconds.compare_exchange_weak(
             maximum, value, std::memory_order_relaxed)) {
  }
}

// Reports when the interval has passed. Only the thread that moves the next
// report time forward reports, so each interval is reported once.
void ReportIfDue(int64_t now) {
  const CostReporter current = reporter.load(std::memory_order_acquire);
  if (!current) {
    return;
  }
  int64_t due = next_report_at.load(std::memory_order_relaxed);
  if (now < due ||
      !next_report_at.compare_exchange_strong(
          due, now + report_interval.load(std::memory_order_relaxed),
          std::memory_order_relaxed)) {
    return;
  }
  current(TakeCostReport(now));
}

}  // namespace

int64_t CostNowNanoseconds() {
  return std::chrono::duration_cast<std::chrono::nanoseconds>(
             std::chrono::steady_clock::now().time_since_epoch())
      .count();
}

int RegisterCostKind(const char* name) {
  std::lock_guard<std::mutex> lock(RegistrationMutex());
  const int count = registered_kinds.load(std::memory_order_relaxed);
  for (int slot = 0; slot < count; ++slot) {
    if (std::string_view(Slots()[static_cast<size_t>(slot)].name.load()) ==
        std::string_view(name)) {
      return slot;
    }
  }
  if (count >= kMaximumCostKinds) {
    return -1;
  }
  Slots()[static_cast<size_t>(count)].name.store(name);
  registered_kinds.store(count + 1, std::memory_order_release);
  return count;
}

CostScope::CostScope(int slot)
    : slot_(reporter.load(std::memory_order_relaxed) ? slot : -1),
      started_(slot_ >= 0 ? CostNowNanoseconds() : 0) {}

CostScope::~CostScope() {
  if (slot_ < 0) {
    return;
  }
  const int64_t now = CostNowNanoseconds();
  Add(slot_, now - started_);
  ReportIfDue(now);
}

void RecordCost(int slot, int64_t elapsed_nanoseconds) {
  if (!reporter.load(std::memory_order_relaxed)) {
    return;
  }
  Add(slot, elapsed_nanoseconds);
  ReportIfDue(CostNowNanoseconds());
}

void BeginCostSpan(int slot) {
  if (slot < 0 || !reporter.load(std::memory_order_relaxed)) {
    return;
  }
  span_started[static_cast<size_t>(slot)] = CostNowNanoseconds();
}

void EndCostSpan(int slot) {
  if (slot < 0) {
    return;
  }
  int64_t& started = span_started[static_cast<size_t>(slot)];
  if (started == 0) {
    return;
  }
  const int64_t now = CostNowNanoseconds();
  Add(slot, now - started);
  started = 0;
  ReportIfDue(now);
}

void SetCostReporter(CostReporter installed, int64_t interval_nanoseconds) {
  const int64_t now = CostNowNanoseconds();
  report_interval.store(interval_nanoseconds, std::memory_order_relaxed);
  previous_report_at.store(now, std::memory_order_relaxed);
  next_report_at.store(now + interval_nanoseconds, std::memory_order_relaxed);
  reporter.store(installed, std::memory_order_release);
}

std::string TakeCostReport(int64_t now_nanoseconds) {
  const int64_t previous =
      previous_report_at.exchange(now_nanoseconds, std::memory_order_relaxed);
  std::string report = "Recorder evidence cost interval_us=" +
                       std::to_string((now_nanoseconds - previous) / 1000);
  const int count = registered_kinds.load(std::memory_order_acquire);
  for (int slot = 0; slot < count; ++slot) {
    CostSlot& source = Slots()[static_cast<size_t>(slot)];
    const uint64_t calls = source.calls.exchange(0, std::memory_order_relaxed);
    const uint64_t total =
        source.total_nanoseconds.exchange(0, std::memory_order_relaxed);
    const uint64_t maximum =
        source.maximum_nanoseconds.exchange(0, std::memory_order_relaxed);
    if (calls == 0) {
      continue;
    }
    report += " ";
    report += source.name.load();
    report += "=" + std::to_string(calls) + "/" + std::to_string(total / 1000) +
              "/" + std::to_string(maximum / 1000);
  }
  return report;
}

}  // namespace a11y_recorder
