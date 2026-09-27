// Checks the evidence queue without a Chromium build. The test suite in
// chromium/test_integrate.py compiles and runs this file when a C++ compiler
// is available, since the queue depends on the standard library alone.

#include "chromium/recorder_bridge/evidence_queue.h"

#include <atomic>
#include <memory>
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

using a11y_recorder::EvidenceQueue;
using a11y_recorder::QueuedEvidence;

struct TestEvidence : QueuedEvidence {
  int index = 0;
};

std::unique_ptr<QueuedEvidence> Record(const std::string& channel,
                                       size_t bytes,
                                       int index = 0) {
  auto record = std::make_unique<TestEvidence>();
  record->channel = channel;
  record->bytes = bytes;
  record->index = index;
  return record;
}

int IndexOf(const std::unique_ptr<QueuedEvidence>& record) {
  return static_cast<const TestEvidence&>(*record).index;
}

// Gives another thread time to reach a wait. The assertions that follow hold
// whether or not it has, except the one that checks the thread is still
// waiting, which a slow machine can only make pass later, never wrongly.
void Settle() {
  std::this_thread::sleep_for(std::chrono::milliseconds(50));
}

void TestRecordsLeaveInTheOrderTheyArrived() {
  EvidenceQueue queue(1024);
  bool waited = true;
  Expect(!queue.Push(Record("browser.dom", 10, 1), &waited),
         "first push accepted");
  Expect(!waited, "a push with room does not wait");
  Expect(!queue.Push(Record("browser.layout", 20, 2)), "second push accepted");
  Expect(!queue.Push(Record("browser.dom", 30, 3)), "third push accepted");
  Expect(queue.queued_bytes() == 60, "queued bytes count every record");

  std::unique_ptr<QueuedEvidence> record = queue.Pop();
  Expect(record && IndexOf(record) == 1 && record->channel == "browser.dom",
         "first record out is the first record in");
  Expect(queue.queued_bytes() == 60,
         "a popped record still counts until it is released");
  queue.Release(record->bytes);
  Expect(queue.queued_bytes() == 50, "release returns the record's bytes");
  record = queue.Pop();
  Expect(record && IndexOf(record) == 2 && record->channel == "browser.layout",
         "second record out is the second record in");
  record = queue.Pop();
  Expect(record && IndexOf(record) == 3, "third record out is the third in");
}

void TestAFullQueueDelaysThePushInsteadOfDroppingIt() {
  EvidenceQueue queue(100);
  Expect(!queue.Push(Record("browser.layout", 80, 1)), "first record fits");
  std::atomic<bool> pushed{false};
  bool waited = false;
  std::thread producer([&] {
    queue.Push(Record("browser.layout", 40, 2), &waited);
    pushed = true;
  });
  Settle();
  Expect(!pushed, "a record that would exceed the limit waits");

  std::unique_ptr<QueuedEvidence> record = queue.Pop();
  Expect(record != nullptr, "the writer takes the first record");
  Settle();
  Expect(!pushed, "taking a record does not free its space before release");
  queue.Release(record->bytes);
  producer.join();
  Expect(pushed, "releasing space lets the waiting record in");
  Expect(waited, "the delayed push reports that it waited");
  record = queue.Pop();
  Expect(record && IndexOf(record) == 2,
         "the delayed record is kept, not dropped");
}

void TestARecordLargerThanTheLimitIsAcceptedWhenTheQueueIsEmpty() {
  EvidenceQueue queue(16);
  bool waited = true;
  Expect(!queue.Push(Record("browser.dom", 64, 1), &waited),
         "an oversized record enters an empty queue");
  Expect(!waited, "an oversized record does not wait on an empty queue");
  std::unique_ptr<QueuedEvidence> record = queue.Pop();
  Expect(record && record->bytes == 64, "the oversized record is returned");
}

void TestCloseWakesTheWriterAndStillDeliversQueuedRecords() {
  EvidenceQueue queue(1024);
  Expect(!queue.Push(Record("browser.dom", 5)), "record queued before close");
  queue.Close();
  Expect(queue.Push(Record("browser.dom", 5)) != nullptr,
         "a closed queue hands the record back");
  Expect(queue.Pop() != nullptr,
         "a record queued before close is still delivered");
  Expect(queue.Pop() == nullptr, "a closed, empty queue ends the writer");

  EvidenceQueue idle(1024);
  std::atomic<bool> ended{false};
  std::thread writer([&] {
    idle.Pop();
    ended = true;
  });
  Settle();
  idle.Close();
  writer.join();
  Expect(ended, "close wakes a writer waiting on an empty queue");

  EvidenceQueue full(10);
  Expect(!full.Push(Record("browser.dom", 10)), "the queue is filled");
  std::atomic<bool> returned{false};
  std::thread producer([&] {
    returned = full.Push(Record("browser.dom", 10)) != nullptr;
  });
  Settle();
  full.Close();
  producer.join();
  Expect(returned, "close wakes a producer waiting for space");
}

void TestManyProducersKeepEachProducersOrder() {
  EvidenceQueue queue(64);
  constexpr int kProducers = 4;
  constexpr int kRecordsEach = 500;
  std::vector<std::thread> producers;
  for (int producer = 0; producer < kProducers; ++producer) {
    producers.emplace_back([&queue, producer] {
      for (int index = 0; index < kRecordsEach; ++index) {
        queue.Push(Record(std::to_string(producer), 8, index));
      }
    });
  }
  std::vector<int> next(kProducers, 0);
  bool ordered = true;
  for (int received = 0; received < kProducers * kRecordsEach; ++received) {
    std::unique_ptr<QueuedEvidence> record = queue.Pop();
    if (!record) {
      ordered = false;
      break;
    }
    const int producer = std::stoi(record->channel);
    if (IndexOf(record) != next[producer]) {
      ordered = false;
    }
    ++next[producer];
    queue.Release(record->bytes);
  }
  for (std::thread& producer : producers) {
    producer.join();
  }
  Expect(ordered, "each producer's records leave in the order it pushed them");
  Expect(queue.queued_bytes() == 0, "every record's space is returned");
}

}  // namespace

int main() {
  TestRecordsLeaveInTheOrderTheyArrived();
  TestAFullQueueDelaysThePushInsteadOfDroppingIt();
  TestARecordLargerThanTheLimitIsAcceptedWhenTheQueueIsEmpty();
  TestCloseWakesTheWriterAndStillDeliversQueuedRecords();
  TestManyProducersKeepEachProducersOrder();
  if (failures != 0) {
    std::fprintf(stderr, "%d evidence queue check(s) failed\n", failures);
    return 1;
  }
  return 0;
}
