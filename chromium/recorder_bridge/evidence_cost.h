#ifndef WINDOWS_A11Y_RECORDER_CHROMIUM_RECORDER_BRIDGE_EVIDENCE_COST_H_
#define WINDOWS_A11Y_RECORDER_CHROMIUM_RECORDER_BRIDGE_EVIDENCE_COST_H_

#include <cstdint>
#include <string>

// Measures the time the bridge costs the threads that observe evidence, so the
// cost of each kind of evidence to the page can be found by measurement. Each
// kind is a name, such as the bridge function that records it, with a count of
// calls, their total time, and the longest call. A span covers work between two
// bridge calls on one thread, such as the traversal between the start and the
// completion of a layout checkpoint, which Blink performs only for the
// recorder. Every few seconds the totals since the last report are handed to a
// reporter, which the bridge installs, as one line.
//
// The accounting uses the C++ standard library alone so it can be exercised
// outside a Chromium build, as the evidence queue is.
namespace a11y_recorder {

// Returns the slot for a kind, registering it on first use. Returns -1 once
// every slot is taken, and a scope or span with that slot records nothing.
int RegisterCostKind(const char* name);

// Times the enclosing block into a kind's slot.
class CostScope {
 public:
  explicit CostScope(int slot);
  ~CostScope();
  CostScope(const CostScope&) = delete;
  CostScope& operator=(const CostScope&) = delete;

 private:
  const int slot_;
  const int64_t started_;
};

// Adds one measured call to a kind's slot.
void RecordCost(int slot, int64_t elapsed_nanoseconds);

// Starts and ends a span on the calling thread. An end without a start on the
// same thread records nothing, and a second start replaces the first.
void BeginCostSpan(int slot);
void EndCostSpan(int slot);

// Receives each report. It is called on whichever thread's scope or span ends
// once the interval has passed, outside any lock the accounting holds.
using CostReporter = void (*)(const std::string& report);

// Installs the reporter and the interval between reports. Accounting starts
// when a reporter is installed.
void SetCostReporter(CostReporter reporter, int64_t interval_nanoseconds);

// Returns the totals since the previous call as one line, and starts new
// totals. Used by the reporter's schedule and by the tests.
std::string TakeCostReport(int64_t now_nanoseconds);

int64_t CostNowNanoseconds();

}  // namespace a11y_recorder

// Times the rest of the enclosing block as the named kind.
#define A11Y_RECORDER_COST(name)                                  \
  static const int recorder_cost_slot =                           \
      ::a11y_recorder::RegisterCostKind(name);                    \
  ::a11y_recorder::CostScope recorder_cost_scope(recorder_cost_slot)

#endif  // WINDOWS_A11Y_RECORDER_CHROMIUM_RECORDER_BRIDGE_EVIDENCE_COST_H_
