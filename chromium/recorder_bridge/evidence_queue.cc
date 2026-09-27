#include "chromium/recorder_bridge/evidence_queue.h"

#include <utility>

namespace a11y_recorder {

QueuedEvidence::QueuedEvidence() = default;

QueuedEvidence::~QueuedEvidence() = default;

EvidenceQueue::EvidenceQueue(size_t maximum_bytes)
    : maximum_bytes_(maximum_bytes) {}

EvidenceQueue::~EvidenceQueue() = default;

std::unique_ptr<QueuedEvidence> EvidenceQueue::Push(
    std::unique_ptr<QueuedEvidence> record,
    bool* waited) {
  const size_t size = record->bytes;
  std::unique_lock<std::mutex> lock(mutex_);
  bool had_to_wait = false;
  while (!closed_ && held_bytes_ > 0 &&
         (size > maximum_bytes_ || held_bytes_ > maximum_bytes_ - size)) {
    had_to_wait = true;
    space_available_.wait(lock);
  }
  if (waited) {
    *waited = had_to_wait;
  }
  if (closed_) {
    return record;
  }
  held_bytes_ += size;
  records_.push_back(std::move(record));
  lock.unlock();
  record_available_.notify_one();
  return nullptr;
}

std::unique_ptr<QueuedEvidence> EvidenceQueue::Pop() {
  std::unique_lock<std::mutex> lock(mutex_);
  record_available_.wait(lock, [this] { return closed_ || !records_.empty(); });
  if (records_.empty()) {
    return nullptr;
  }
  std::unique_ptr<QueuedEvidence> record = std::move(records_.front());
  records_.pop_front();
  return record;
}

void EvidenceQueue::Release(size_t bytes) {
  {
    std::lock_guard<std::mutex> lock(mutex_);
    held_bytes_ = bytes > held_bytes_ ? 0 : held_bytes_ - bytes;
  }
  space_available_.notify_all();
}

void EvidenceQueue::Close() {
  {
    std::lock_guard<std::mutex> lock(mutex_);
    closed_ = true;
  }
  record_available_.notify_all();
  space_available_.notify_all();
}

size_t EvidenceQueue::queued_bytes() const {
  std::lock_guard<std::mutex> lock(mutex_);
  return held_bytes_;
}

}  // namespace a11y_recorder
