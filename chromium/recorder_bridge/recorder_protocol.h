#ifndef WINDOWS_A11Y_RECORDER_CHROMIUM_RECORDER_BRIDGE_RECORDER_PROTOCOL_H_
#define WINDOWS_A11Y_RECORDER_CHROMIUM_RECORDER_BRIDGE_RECORDER_PROTOCOL_H_

#include <stdint.h>

#include <memory>
#include <optional>
#include <string>
#include <string_view>

#include "base/synchronization/lock.h"
#include "base/threading/platform_thread.h"
#include "base/values.h"
#include "base/win/scoped_handle.h"
#include "chromium/recorder_bridge/evidence_queue.h"

namespace a11y_recorder {

inline constexpr char kProtocolVersion[] = "0.52";
// The largest frame the recorder reads: its length is a 32-bit signed count.
// Values are recorded whole, so a record is as large as what it records.
inline constexpr uint32_t kDefaultMaximumMessageBytes = 2147483647;
// How long a process waits for a free recorder pipe instance before it reports
// that it could not connect, and how long it backs off between attempts while
// the pipe is not there at all.
inline constexpr uint32_t kPipeConnectTimeoutMilliseconds = 15000;
inline constexpr uint32_t kPipeConnectRetryMilliseconds = 25;
// How much evidence one process may hold while its writer thread waits on the
// recorder. A thread that records evidence waits only once this much is
// already queued; a single larger record is accepted once the queue is empty.
inline constexpr size_t kMaximumQueuedEvidenceBytes = 64 * 1024 * 1024;

struct BootstrapConfiguration {
  std::string protocol_version;
  std::string pipe_name;
  std::string authentication_token;
  std::string browser_instance_id;
  uint32_t maximum_message_bytes = kDefaultMaximumMessageBytes;
  std::optional<int> parent_process_id;
  std::optional<int> child_process_id;
  // Every how many requests a document is walked in full to check its change
  // records (protocol 0.35), or 0 when it is not.
  int full_walk_interval = 0;
};

bool ParseBootstrapConfiguration(std::string_view json,
                                 BootstrapConfiguration* configuration,
                                 std::string* error);

bool SerializeBootstrapConfiguration(
    const BootstrapConfiguration& configuration,
    std::string* json,
    std::string* error);

// Reads the single JSON bootstrap line written to inherited standard input by
// Recorder.App. The authentication token is never passed in argv or written to
// disk.
bool ReadBootstrapFromStandardInput(BootstrapConfiguration* configuration,
                                    std::string* error);

// One record queued for the writer thread. The thread that observed the
// evidence fills in what it observed, including the timestamp of the
// observation; the writer thread builds the payload, serializes the message,
// and writes it. A type that defers building its payload overrides TakePayload
// and must copy every observed value it needs, since the writer runs after the
// observing thread has moved on.
struct PendingEvidence : QueuedEvidence {
  PendingEvidence();
  ~PendingEvidence() override;
  int64_t browser_timestamp_ticks = 0;
  std::string event_type;
  // How many records a failed write of this one loses. An evidence record is
  // one; an omission record restates the count it carried.
  int lost_records_on_failure = 1;
  // Called once, on the writer thread.
  virtual base::DictValue TakePayload() = 0;
};

// Reports, on the writer thread, a record that could not be written. It is
// installed before the connection is made and never changes.
using EvidenceWriteFailureHandler = void (*)(const std::string& channel,
                                             int lost_records,
                                             const std::string& error);

// Estimates the serialized size of a value without serializing it, so a
// payload built by the observing thread can be charged against the queue
// limit. The estimate counts string contents and a fixed cost per value.
size_t EstimateSerializedBytes(const base::Value& value);
size_t EstimateSerializedBytes(const base::DictValue& value);

class RecorderPipeClient : public base::PlatformThread::Delegate {
 public:
  explicit RecorderPipeClient(BootstrapConfiguration configuration);
  RecorderPipeClient(const RecorderPipeClient&) = delete;
  RecorderPipeClient& operator=(const RecorderPipeClient&) = delete;
  ~RecorderPipeClient() override;

  // Must be called before ConnectAndSynchronize.
  void SetWriteFailureHandler(EvidenceWriteFailureHandler handler) {
    write_failure_handler_ = handler;
  }

  // Connects, authenticates, synchronizes clocks, and then starts the thread
  // that writes queued evidence.
  bool ConnectAndSynchronize(const std::string& process_type,
                             const std::string& chromium_version,
                             std::string* error);

  // Queues a record whose payload is already built, timestamped when it
  // reaches the queue. Returns false, with *error set, when the record cannot
  // be queued; a write that fails later is reported to the write-failure
  // handler instead.
  bool SendEvidence(std::string channel,
                    std::string event_type,
                    base::DictValue payload,
                    std::string* error,
                    int lost_records_on_failure = 1);

  // Queues a record whose payload the writer thread builds. The caller sets
  // the channel, event type, and estimated size; the timestamp is taken when
  // the record reaches the queue, in the same order as the records.
  bool QueueEvidence(std::unique_ptr<PendingEvidence> evidence,
                     std::string* error);

  bool connected() const { return pipe_.is_valid(); }
  const std::string& browser_instance_id() const {
    return configuration_.browser_instance_id;
  }
  const std::string& process_type() const { return process_type_; }
  // How long this process spent opening the pipe, and how many times it had to
  // wait for a free instance. Both are zero for a connection that was accepted
  // on the first attempt.
  uint32_t connect_wait_milliseconds() const {
    return connect_wait_milliseconds_;
  }
  uint32_t connect_wait_count() const { return connect_wait_count_; }

 private:
  // Runs the writer thread.
  void ThreadMain() override;
  void WriteQueuedEvidence(PendingEvidence& evidence);

  bool WriteMessage(base::DictValue message, std::string* error);
  // The two halves of WriteMessage, apart so the writer thread can time them.
  bool SerializeMessage(const base::DictValue& message,
                        std::string* json,
                        std::string* error) const;
  bool WriteFrame(const std::string& json, std::string* error);
  bool ReadMessage(base::DictValue* message, std::string* error);

  BootstrapConfiguration configuration_;
  std::string process_type_;
  base::win::ScopedHandle pipe_;
  uint32_t connect_wait_milliseconds_ = 0;
  uint32_t connect_wait_count_ = 0;
  EvidenceWriteFailureHandler write_failure_handler_ = nullptr;
  EvidenceQueue queue_{kMaximumQueuedEvidenceBytes};
  base::PlatformThreadHandle writer_thread_;
  bool writer_started_ = false;
  // Serializes writes made without the writer thread, when it could not be
  // started.
  base::Lock direct_write_lock_;
};

}  // namespace a11y_recorder

#endif  // WINDOWS_A11Y_RECORDER_CHROMIUM_RECORDER_BRIDGE_RECORDER_PROTOCOL_H_
