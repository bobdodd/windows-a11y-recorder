// Checks the evidence cost accounting without a Chromium build. The test suite
// in chromium/test_integrate.py compiles and runs this file when a C++
// compiler is available, since the accounting depends on the standard library
// alone.

#include "chromium/recorder_bridge/evidence_cost.h"

#include <chrono>
#include <cstdio>
#include <string>
#include <thread>
#include <vector>

namespace {

int failures = 0;

void Expect(bool condition, const char* description) {
  if (!condition) {
    std::fprintf(stderr, "FAILED: %s\n", description);
    ++failures;
  }
}

std::vector<std::string> reports;

void CollectReport(const std::string& report) {
  reports.push_back(report);
}

bool Contains(const std::string& text, const std::string& part) {
  return text.find(part) != std::string::npos;
}

void TestNothingIsMeasuredBeforeAReporterIsInstalled() {
  const int slot = a11y_recorder::RegisterCostKind("before-reporter");
  { a11y_recorder::CostScope scope(slot); }
  a11y_recorder::SetCostReporter(&CollectReport, 3'600'000'000'000);
  const std::string report =
      a11y_recorder::TakeCostReport(a11y_recorder::CostNowNanoseconds());
  Expect(!Contains(report, "before-reporter="),
         "a scope before the reporter is installed is not counted");
}

void TestAKindIsRegisteredOnce() {
  const int first = a11y_recorder::RegisterCostKind("same-kind");
  const int second = a11y_recorder::RegisterCostKind("same-kind");
  Expect(first >= 0 && first == second, "one name has one slot");
}

void TestScopesAreCountedAndTotalled() {
  const int slot = a11y_recorder::RegisterCostKind("scoped");
  for (int call = 0; call < 3; ++call) {
    a11y_recorder::CostScope scope(slot);
    std::this_thread::sleep_for(std::chrono::milliseconds(2));
  }
  const std::string report =
      a11y_recorder::TakeCostReport(a11y_recorder::CostNowNanoseconds());
  Expect(Contains(report, " scoped=3/"), "three scopes are three calls");
  const size_t at = report.find(" scoped=3/");
  const long total = at == std::string::npos
                         ? 0
                         : std::stol(report.substr(at + 10));
  Expect(total >= 6000, "the total covers every call's time");
  const std::string next =
      a11y_recorder::TakeCostReport(a11y_recorder::CostNowNanoseconds());
  Expect(!Contains(next, " scoped="), "a report starts new totals");
}

void TestASpanCoversWorkBetweenItsEnds() {
  const int slot = a11y_recorder::RegisterCostKind("span");
  a11y_recorder::BeginCostSpan(slot);
  std::this_thread::sleep_for(std::chrono::milliseconds(3));
  a11y_recorder::EndCostSpan(slot);
  a11y_recorder::EndCostSpan(slot);
  const std::string report =
      a11y_recorder::TakeCostReport(a11y_recorder::CostNowNanoseconds());
  Expect(Contains(report, " span=1/"),
         "an end without a start records nothing");
}

void TestASpanBelongsToTheThreadThatStartedIt() {
  const int slot = a11y_recorder::RegisterCostKind("thread-span");
  a11y_recorder::BeginCostSpan(slot);
  std::thread other([slot] { a11y_recorder::EndCostSpan(slot); });
  other.join();
  const std::string report =
      a11y_recorder::TakeCostReport(a11y_recorder::CostNowNanoseconds());
  Expect(!Contains(report, " thread-span="),
         "another thread cannot end the span");
  a11y_recorder::EndCostSpan(slot);
}

void TestTheReporterIsCalledOnceTheIntervalHasPassed() {
  reports.clear();
  a11y_recorder::SetCostReporter(&CollectReport, 1'000'000);
  const int slot = a11y_recorder::RegisterCostKind("reported");
  std::this_thread::sleep_for(std::chrono::milliseconds(2));
  { a11y_recorder::CostScope scope(slot); }
  Expect(reports.size() == 1, "one report after one interval");
  Expect(!reports.empty() && Contains(reports[0], " reported=1/"),
         "the report names the measured kind");
  { a11y_recorder::CostScope scope(slot); }
  Expect(reports.size() == 1, "no second report within the interval");
}

void TestConcurrentCallsAreAllCounted() {
  a11y_recorder::SetCostReporter(&CollectReport, 3'600'000'000'000);
  const int slot = a11y_recorder::RegisterCostKind("concurrent");
  std::vector<std::thread> threads;
  for (int thread = 0; thread < 4; ++thread) {
    threads.emplace_back([slot] {
      for (int call = 0; call < 1000; ++call) {
        a11y_recorder::CostScope scope(slot);
      }
    });
  }
  for (std::thread& thread : threads) {
    thread.join();
  }
  const std::string report =
      a11y_recorder::TakeCostReport(a11y_recorder::CostNowNanoseconds());
  Expect(Contains(report, " concurrent=4000/"),
         "every call from every thread is counted");
}

}  // namespace

void TestACountKindReportsCountsAsTheyAre() {
  const int slot = a11y_recorder::RegisterCountKind("counted");
  a11y_recorder::RecordCost(slot, 1500);
  a11y_recorder::RecordCost(slot, 2500);
  const std::string report =
      a11y_recorder::TakeCostReport(a11y_recorder::CostNowNanoseconds());
  Expect(Contains(report, " counted=2/4000/2500"),
         "a count kind states its total and largest count unscaled");
}

int main() {
  TestNothingIsMeasuredBeforeAReporterIsInstalled();
  TestAKindIsRegisteredOnce();
  TestScopesAreCountedAndTotalled();
  TestASpanCoversWorkBetweenItsEnds();
  TestASpanBelongsToTheThreadThatStartedIt();
  TestTheReporterIsCalledOnceTheIntervalHasPassed();
  TestConcurrentCallsAreAllCounted();
  TestACountKindReportsCountsAsTheyAre();
  if (failures != 0) {
    std::fprintf(stderr, "%d evidence cost checks failed\n", failures);
    return 1;
  }
  std::printf("evidence cost checks passed\n");
  return 0;
}
