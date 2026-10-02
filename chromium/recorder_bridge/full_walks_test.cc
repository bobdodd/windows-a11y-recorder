// Checks when a document is walked in full without a Chromium build. The test
// suite in chromium/test_integrate.py compiles and runs this file when a C++
// compiler is available, since it depends on the standard library alone.

#include "chromium/recorder_bridge/full_walks.h"

#include <cstdio>
#include <string>

namespace {

int failures = 0;

void Expect(bool condition, const char* description) {
  if (!condition) {
    std::fprintf(stderr, "FAILED: %s\n", description);
    ++failures;
  }
}

void TestWithoutChecksOnlyTheFirstRequestsAreWalked() {
  a11y_recorder::FullWalkSchedule schedule(0);
  Expect(schedule.DomWalkReason(1, "finished-parsing", 0) == "first",
         "finished parsing is walked");
  for (int index = 0; index < 1000; ++index) {
    if (!schedule.DomWalkReason(1, "post-mutation", 0).empty()) {
      Expect(false, "a mutation delivery after parsing is not walked");
      break;
    }
  }
  Expect(schedule.LayoutWalkReason(1, 0) == "first",
         "the first rendering update is walked");
  for (int index = 0; index < 1000; ++index) {
    if (!schedule.LayoutWalkReason(1, 0).empty()) {
      Expect(false, "a later rendering update is not walked");
      break;
    }
  }
}

void TestAChangeBeforeAnyWalkIsWalkedFirst() {
  a11y_recorder::FullWalkSchedule schedule(0);
  Expect(schedule.DomWalkReason(2, "post-mutation", 0) == "first",
         "a document with no walk is walked at its first change");
  Expect(schedule.DomWalkReason(2, "post-mutation", 0).empty(),
         "and not at the next");
  Expect(schedule.DomWalkReason(2, "finished-parsing", 0) ==
             "finished-parsing",
         "finished parsing is always walked");
  Expect(schedule.DomWalkReason(2, "finished-parsing", 1) == "after-loss",
         "and names a loss before it");
  Expect(schedule.DomWalkReason(2, "unknown", 0).empty(),
         "an unknown request is not walked");
}

void TestTheStartOfParsingIsAlwaysWalked() {
  // Protocol 0.42: the walk when parsing starts is the document's first, and
  // a later one, as after document.open(), names its own reason.
  a11y_recorder::FullWalkSchedule schedule(0);
  Expect(schedule.DomWalkReason(3, "started-parsing", 0) == "first",
         "the start of parsing is the first walk");
  Expect(schedule.DomWalkReason(3, "post-mutation", 0).empty(),
         "a mutation delivery while parsing is not walked");
  Expect(schedule.DomWalkReason(3, "finished-parsing", 0) ==
             "finished-parsing",
         "finished parsing is still walked");
  Expect(schedule.DomWalkReason(3, "started-parsing", 0) ==
             "started-parsing",
         "a later start of parsing is walked as one");
  Expect(schedule.DomWalkReason(3, "started-parsing", 2) == "after-loss",
         "and names a loss before it");
}

void TestALossWalksEachDocumentAgainOnce() {
  a11y_recorder::FullWalkSchedule schedule(0);
  schedule.DomWalkReason(1, "finished-parsing", 0);
  schedule.DomWalkReason(2, "finished-parsing", 0);
  schedule.LayoutWalkReason(1, 0);
  Expect(schedule.DomWalkReason(1, "post-mutation", 3) == "after-loss",
         "a DOM loss walks the document");
  Expect(schedule.DomWalkReason(1, "post-mutation", 3).empty(),
         "once");
  Expect(schedule.DomWalkReason(2, "post-mutation", 3) == "after-loss",
         "and every other document");
  Expect(schedule.LayoutWalkReason(1, 5) == "after-loss",
         "a layout loss walks the document's layout");
  Expect(schedule.LayoutWalkReason(1, 5).empty(), "once");
  Expect(schedule.DomWalkReason(1, "post-mutation", 4) == "after-loss",
         "a further loss walks it again");
}

void TestChecksWalkEveryNthRequest() {
  a11y_recorder::FullWalkSchedule schedule(3);
  schedule.DomWalkReason(1, "finished-parsing", 0);
  std::string reasons;
  for (int index = 0; index < 7; ++index) {
    reasons += schedule.DomWalkReason(1, "post-mutation", 0).empty()
                   ? "-"
                   : "w";
  }
  Expect(reasons == "--w--w-", "every third mutation delivery is walked");
  Expect(schedule.DomWalkReason(1, "post-mutation", 0) == "",
         "the delivery after a check is not walked");
  Expect(schedule.DomWalkReason(1, "post-mutation", 0) == "check",
         "a check is named as one");
  schedule.LayoutWalkReason(1, 0);
  reasons.clear();
  for (int index = 0; index < 7; ++index) {
    reasons += schedule.LayoutWalkReason(1, 0).empty() ? "-" : "w";
  }
  Expect(reasons == "--w--w-", "every third rendering update is walked");
  Expect(schedule.DomWalkReason(1, "post-mutation", 1) == "after-loss",
         "a loss is walked before a check");
  reasons.clear();
  for (int index = 0; index < 3; ++index) {
    reasons += schedule.DomWalkReason(1, "post-mutation", 1).empty()
                   ? "-"
                   : "w";
  }
  Expect(reasons == "--w", "a walk after a loss restarts the interval");
}

void TestAForgottenDocumentIsWalkedAgain() {
  a11y_recorder::FullWalkSchedule schedule(0);
  schedule.LayoutWalkReason(1, 0);
  for (int document = 2;
       document <= static_cast<int>(
                       a11y_recorder::FullWalkSchedule::kMaximumDocuments) +
                       1;
       ++document) {
    schedule.LayoutWalkReason(document, 0);
  }
  Expect(schedule.LayoutWalkReason(1, 0) == "first",
         "the least recently used document is walked at its next request");
}

void TestTheChangeSetSourceIsDistinct() {
  Expect(a11y_recorder::LayoutChangeSetSource(0) == 0, "no change set");
  Expect(a11y_recorder::LayoutChangeSetSource(5) ==
             (5 | a11y_recorder::kLayoutChangeSetSourceBit),
         "a change set is marked");
}

}  // namespace

int main() {
  TestWithoutChecksOnlyTheFirstRequestsAreWalked();
  TestAChangeBeforeAnyWalkIsWalkedFirst();
  TestTheStartOfParsingIsAlwaysWalked();
  TestALossWalksEachDocumentAgainOnce();
  TestChecksWalkEveryNthRequest();
  TestAForgottenDocumentIsWalkedAgain();
  TestTheChangeSetSourceIsDistinct();
  if (failures == 0) {
    std::printf("full walk tests passed\n");
  }
  return failures == 0 ? 0 : 1;
}
