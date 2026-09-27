#ifndef WINDOWS_A11Y_RECORDER_CHROMIUM_RECORDER_BRIDGE_EVIDENCE_QUEUE_H_
#define WINDOWS_A11Y_RECORDER_CHROMIUM_RECORDER_BRIDGE_EVIDENCE_QUEUE_H_

#include <condition_variable>
#include <cstddef>
#include <deque>
#include <memory>
#include <mutex>
#include <string>

// Holds recorded evidence between the thread that observed it and the one
// thread that formats it and writes it to the recorder pipe. A pipe write
// blocks until the recorder has read enough of what was written before it, so
// a thread that formatted and wrote its own records waited on the recorder for
// every record. With the queue, an observing thread only copies what it
// observed and waits only when the evidence already queued would exceed the
// byte limit, which happens when the recorder has fallen that far behind.
// Evidence is never dropped to make room: a full queue delays the observing
// thread rather than losing evidence.
//
// The queue uses the C++ standard library alone so it can be exercised outside
// a Chromium build, as network_text and cookie_text are.
namespace a11y_recorder {

// One record waiting to be formatted and written. The bridge derives a type
// for each shape of observed values it queues.
struct QueuedEvidence {
  virtual ~QueuedEvidence() = default;
  // The channel the record belongs to.
  std::string channel;
  // An estimate of the record's formatted size, charged against the limit.
  size_t bytes = 0;
};

class EvidenceQueue {
 public:
  explicit EvidenceQueue(size_t maximum_bytes);
  EvidenceQueue(const EvidenceQueue&) = delete;
  EvidenceQueue& operator=(const EvidenceQueue&) = delete;

  // Adds a record. Waits while the queue holds anything and adding this record
  // would exceed the byte limit, so a single record larger than the limit is
  // still accepted once the queue is empty. Sets *waited, when given, to
  // whether the call had to wait. Returns the record, instead of taking it,
  // once the queue is closed.
  std::unique_ptr<QueuedEvidence> Push(std::unique_ptr<QueuedEvidence> record,
                                       bool* waited = nullptr);

  // Takes the oldest record, waiting until one is available. Returns null once
  // the queue is closed and empty. The record's bytes still count against the
  // limit until Release is called for them, so a record being formatted and
  // written is bounded too.
  std::unique_ptr<QueuedEvidence> Pop();

  // Returns the space a popped record held after it was written or failed.
  void Release(size_t bytes);

  // Stops accepting records and wakes every waiting thread. Records already
  // queued are still returned by Pop.
  void Close();

  size_t maximum_bytes() const { return maximum_bytes_; }
  size_t queued_bytes() const;

 private:
  const size_t maximum_bytes_;
  mutable std::mutex mutex_;
  std::condition_variable record_available_;
  std::condition_variable space_available_;
  std::deque<std::unique_ptr<QueuedEvidence>> records_;
  // Bytes of queued records plus records popped but not yet released.
  size_t held_bytes_ = 0;
  bool closed_ = false;
};

}  // namespace a11y_recorder

#endif  // WINDOWS_A11Y_RECORDER_CHROMIUM_RECORDER_BRIDGE_EVIDENCE_QUEUE_H_
