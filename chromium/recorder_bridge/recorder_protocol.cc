#include "chromium/recorder_bridge/recorder_protocol.h"

#include <windows.h>

#include <algorithm>
#include <array>
#include <iostream>
#include <limits>
#include <optional>
#include <string>
#include <string_view>
#include <utility>

#include "base/check.h"
#include "base/containers/span.h"
#include "base/json/json_reader.h"
#include "base/json/json_writer.h"
#include "base/numerics/safe_conversions.h"
#include "base/strings/string_number_conversions.h"
#include "base/strings/utf_string_conversions.h"
#include "chromium/recorder_bridge/evidence_cost.h"

namespace a11y_recorder {
namespace {

bool ReadExact(HANDLE handle, base::span<uint8_t> output) {
  size_t total = 0;
  while (total < output.size()) {
    base::span<uint8_t> remaining = output.subspan(total);
    DWORD read = 0;
    if (!::ReadFile(handle, remaining.data(),
                    base::checked_cast<DWORD>(remaining.size()), &read,
                    nullptr) ||
        read == 0) {
      return false;
    }
    total += read;
  }
  return true;
}

bool WriteExact(HANDLE handle, base::span<const uint8_t> input) {
  size_t total = 0;
  while (total < input.size()) {
    base::span<const uint8_t> remaining = input.subspan(total);
    DWORD written = 0;
    if (!::WriteFile(handle, remaining.data(),
                     base::checked_cast<DWORD>(remaining.size()), &written,
                     nullptr) ||
        written == 0) {
      return false;
    }
    total += written;
  }
  return true;
}

int64_t QueryMonotonicTicks() {
  LARGE_INTEGER value = {};
  CHECK(::QueryPerformanceCounter(&value));
  return value.QuadPart;
}

int64_t QueryMonotonicFrequency() {
  LARGE_INTEGER value = {};
  CHECK(::QueryPerformanceFrequency(&value));
  return value.QuadPart;
}

bool RequireString(const base::DictValue& value,
                   std::string_view name,
                   std::string* output,
                   std::string* error) {
  const std::string* item = value.FindString(name);
  if (!item || item->empty()) {
    *error = "Missing or empty bootstrap field: " + std::string(name);
    return false;
  }
  *output = *item;
  return true;
}

// The rejected protocol version is the only bootstrap field this bridge ever
// reports, because a mismatch cannot be acted on without knowing which half is
// stale. It arrives from outside this process, so it is bounded and reduced to
// printable ASCII before it reaches a log or an error message.
std::string DescribeReportedProtocolVersion(const std::string& version) {
  constexpr size_t kMaximumReportedVersionLength = 32;
  if (version.empty()) {
    return "<empty>";
  }
  std::string described;
  const size_t length =
      std::min(version.size(), kMaximumReportedVersionLength);
  described.reserve(length + 3);
  for (size_t index = 0; index < length; ++index) {
    const unsigned char character =
        static_cast<unsigned char>(version[index]);
    described.push_back(character >= 0x20 && character < 0x7F
                            ? version[index]
                            : '?');
  }
  if (version.size() > kMaximumReportedVersionLength) {
    described.append("...");
  }
  return described;
}

}  // namespace

bool ParseBootstrapConfiguration(std::string_view json,
                                 BootstrapConfiguration* configuration,
                                 std::string* error) {
  if (!configuration || !error) {
    return false;
  }

  std::optional<base::Value> parsed =
      base::JSONReader::Read(json, base::JSON_PARSE_RFC);
  if (!parsed || !parsed->is_dict()) {
    *error = "Recorder bootstrap was not a JSON object.";
    return false;
  }

  const base::DictValue& value = parsed->GetDict();
  const std::string* kind = value.FindString("kind");
  if (!kind || *kind != "a11y-recorder-bootstrap" ||
      !RequireString(value, "protocolVersion", &configuration->protocol_version,
                     error) ||
      !RequireString(value, "pipeName", &configuration->pipe_name, error) ||
      !RequireString(value, "authenticationToken",
                     &configuration->authentication_token, error) ||
      !RequireString(value, "browserInstanceId",
                     &configuration->browser_instance_id, error)) {
    if (error->empty()) {
      *error = "Recorder bootstrap kind was invalid.";
    }
    return false;
  }

  std::optional<int> maximum = value.FindInt("maximumMessageBytes");
  if (!maximum || *maximum <= 0) {
    *error = "Recorder bootstrap message limit was invalid.";
    return false;
  }
  configuration->maximum_message_bytes = static_cast<uint32_t>(*maximum);
  configuration->parent_process_id = value.FindInt("parentProcessId");
  configuration->child_process_id = value.FindInt("childProcessId");
  std::optional<int> interval = value.FindInt("fullWalkInterval");
  if (!interval || *interval < 0) {
    *error = "Recorder bootstrap full walk interval was invalid.";
    return false;
  }
  configuration->full_walk_interval = *interval;
  if (configuration->protocol_version != kProtocolVersion) {
    *error = "Recorder protocol version " +
             DescribeReportedProtocolVersion(configuration->protocol_version) +
             " is not supported. This browser requires protocol version " +
             std::string(kProtocolVersion) +
             ", so the recorder application and the instrumented browser were "
             "built from different revisions.";
    return false;
  }
  return true;
}

bool SerializeBootstrapConfiguration(
    const BootstrapConfiguration& configuration,
    std::string* json,
    std::string* error) {
  if (!json || !error) {
    return false;
  }

  base::DictValue value;
  value.Set("kind", "a11y-recorder-bootstrap");
  value.Set("protocolVersion", configuration.protocol_version);
  value.Set("pipeName", configuration.pipe_name);
  value.Set("authenticationToken", configuration.authentication_token);
  value.Set("browserInstanceId", configuration.browser_instance_id);
  value.Set("maximumMessageBytes",
            static_cast<int>(configuration.maximum_message_bytes));
  if (configuration.parent_process_id) {
    value.Set("parentProcessId", *configuration.parent_process_id);
  }
  if (configuration.child_process_id) {
    value.Set("childProcessId", *configuration.child_process_id);
  }
  value.Set("fullWalkInterval", configuration.full_walk_interval);
  if (!base::JSONWriter::Write(value, json)) {
    *error = "Recorder bootstrap could not be serialized.";
    return false;
  }
  return true;
}

bool ReadBootstrapFromStandardInput(BootstrapConfiguration* configuration,
                                    std::string* error) {
  std::string line;
  if (!std::getline(std::cin, line) || line.empty()) {
    if (error) {
      *error = "Recorder bootstrap was not available on standard input.";
    }
    return false;
  }
  return ParseBootstrapConfiguration(line, configuration, error);
}

RecorderPipeClient::RecorderPipeClient(BootstrapConfiguration configuration)
    : configuration_(std::move(configuration)) {}

RecorderPipeClient::~RecorderPipeClient() {
  // Records already queued are written before the writer thread ends.
  queue_.Close();
  if (writer_started_) {
    base::PlatformThread::Join(writer_thread_);
  }
}

bool RecorderPipeClient::ConnectAndSynchronize(
    const std::string& process_type,
    const std::string& chromium_version,
    std::string* error) {
  process_type_ = process_type;
  const std::wstring path =
      L"\\\\.\\pipe\\" + base::UTF8ToWide(configuration_.pipe_name);
  // The recorder keeps one pending pipe instance at a time, so a process that
  // opens the pipe while another process is being accepted is told the pipe is
  // busy. Chromium starts several renderers at once, so this is ordinary
  // contention rather than a recorder that has gone away, and a single open
  // attempt loses a whole process's evidence and, because the bridge hook
  // fails the process, the process itself. Waiting for a free instance until a
  // deadline expires makes contention cost a wait instead of a process.
  const DWORD started_at = ::GetTickCount();
  DWORD last_error = ERROR_SUCCESS;
  for (;;) {
    pipe_.Set(::CreateFileW(path.c_str(), GENERIC_READ | GENERIC_WRITE, 0,
                            nullptr, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL,
                            nullptr));
    if (pipe_.is_valid()) {
      break;
    }
    last_error = ::GetLastError();
    // A busy pipe has instances that are all taken, and a pipe that is not
    // found yet may be between instances. Every other error, an access denial
    // above all, describes a pipe this process will never be allowed to open,
    // so it is reported rather than retried.
    if (last_error != ERROR_PIPE_BUSY && last_error != ERROR_FILE_NOT_FOUND) {
      break;
    }
    const DWORD elapsed = ::GetTickCount() - started_at;
    if (elapsed >= kPipeConnectTimeoutMilliseconds) {
      break;
    }
    const DWORD remaining = kPipeConnectTimeoutMilliseconds - elapsed;
    connect_wait_count_++;
    if (last_error == ERROR_PIPE_BUSY) {
      // WaitNamedPipe returns as soon as an instance is free, but the instance
      // it saw can be taken by another process before this one opens it, so the
      // result is a reason to try again rather than a guarantee.
      if (!::WaitNamedPipeW(path.c_str(), remaining)) {
        last_error = ::GetLastError();
        if (last_error != ERROR_SEM_TIMEOUT &&
            last_error != ERROR_FILE_NOT_FOUND) {
          break;
        }
      }
    } else {
      // Nothing to wait on when the pipe is not there, so this backs off
      // briefly instead of spinning on the open call.
      ::Sleep(std::min<DWORD>(kPipeConnectRetryMilliseconds, remaining));
    }
  }
  if (!pipe_.is_valid()) {
    connect_wait_milliseconds_ =
        static_cast<uint32_t>(::GetTickCount() - started_at);
    *error = "Could not connect to the recorder named pipe after " +
             base::NumberToString(connect_wait_milliseconds_) +
             " ms: Windows error " +
             base::NumberToString(static_cast<uint32_t>(last_error)) +
             " after " + base::NumberToString(connect_wait_count_) +
             " waits.";
    return false;
  }
  connect_wait_milliseconds_ =
      static_cast<uint32_t>(::GetTickCount() - started_at);

  base::DictValue hello;
  hello.Set("kind", "hello");
  hello.Set("protocolVersion", configuration_.protocol_version);
  hello.Set("authenticationToken", configuration_.authentication_token);
  hello.Set("browserInstanceId", configuration_.browser_instance_id);
  hello.Set("processId", static_cast<int>(::GetCurrentProcessId()));
  hello.Set("processType", process_type);
  hello.Set("chromiumVersion", chromium_version);
  if (configuration_.parent_process_id) {
    hello.Set("parentProcessId", *configuration_.parent_process_id);
  }
  if (configuration_.child_process_id) {
    hello.Set("childProcessId", *configuration_.child_process_id);
  }
  hello.Set("monotonicFrequency",
            base::NumberToString(QueryMonotonicFrequency()));
  if (!WriteMessage(std::move(hello), error)) {
    return false;
  }

  base::DictValue request;
  if (!ReadMessage(&request, error)) {
    return false;
  }
  const std::string* kind = request.FindString("kind");
  const std::string* request_id = request.FindString("requestId");
  if (!kind || *kind != "clock-sync-request" || !request_id) {
    *error = "Recorder returned an invalid clock synchronization request.";
    return false;
  }

  const int64_t receive_ticks = QueryMonotonicTicks();
  base::DictValue response;
  response.Set("kind", "clock-sync-response");
  response.Set("requestId", *request_id);
  response.Set("browserReceiveTicks", base::NumberToString(receive_ticks));
  response.Set("browserSendTicks", base::NumberToString(QueryMonotonicTicks()));
  if (!WriteMessage(std::move(response), error)) {
    return false;
  }

  base::DictValue ready;
  if (!ReadMessage(&ready, error)) {
    return false;
  }
  kind = ready.FindString("kind");
  if (!kind || *kind != "ready") {
    *error = "Recorder did not complete clock synchronization.";
    return false;
  }
  // The handshake above is the last exchange that reads from the pipe, so from
  // here on the writer thread is the pipe's only user. A process whose writer
  // thread cannot be started still records, writing each record from the
  // thread that observed it.
  writer_started_ = base::PlatformThread::Create(0, this, &writer_thread_);
  return true;
}

PendingEvidence::PendingEvidence() = default;

PendingEvidence::~PendingEvidence() = default;

size_t EstimateSerializedBytes(const base::Value& value) {
  // Quotes, separators, and the digits of a number fit in this allowance.
  constexpr size_t kValueOverhead = 24;
  switch (value.type()) {
    case base::Value::Type::STRING:
      return kValueOverhead + value.GetString().size();
    case base::Value::Type::DICT:
      return EstimateSerializedBytes(value.GetDict());
    case base::Value::Type::LIST: {
      size_t bytes = kValueOverhead;
      for (const base::Value& item : value.GetList()) {
        bytes += EstimateSerializedBytes(item);
      }
      return bytes;
    }
    case base::Value::Type::BINARY:
      return kValueOverhead + value.GetBlob().size() * 2;
    default:
      return kValueOverhead;
  }
}

size_t EstimateSerializedBytes(const base::DictValue& value) {
  size_t bytes = 24;
  for (const auto [key, item] : value) {
    bytes += key.size() + EstimateSerializedBytes(item);
  }
  return bytes;
}

namespace {

// A record whose payload the observing thread already built.
struct BuiltEvidence : PendingEvidence {
  base::DictValue payload;
  base::DictValue TakePayload() override { return std::move(payload); }
};

// Takes a record's timestamp. It is called with the lock that orders the
// records held, so no record is written with an earlier time than the one
// before it, even when several threads of the process record evidence at once.
void StampEvidence(QueuedEvidence& evidence) {
  static_cast<PendingEvidence&>(evidence).browser_timestamp_ticks =
      QueryMonotonicTicks();
}

}  // namespace

bool RecorderPipeClient::SendEvidence(std::string channel,
                                      std::string event_type,
                                      base::DictValue payload,
                                      std::string* error,
                                      int lost_records_on_failure) {
  auto evidence = std::make_unique<BuiltEvidence>();
  evidence->bytes = EstimateSerializedBytes(payload) + channel.size() +
                    event_type.size();
  evidence->channel = std::move(channel);
  evidence->event_type = std::move(event_type);
  evidence->lost_records_on_failure = lost_records_on_failure;
  evidence->payload = std::move(payload);
  return QueueEvidence(std::move(evidence), error);
}

bool RecorderPipeClient::QueueEvidence(
    std::unique_ptr<PendingEvidence> evidence,
    std::string* error) {
  if (!writer_started_) {
    // Without a writer thread the observing thread writes the record itself,
    // as every record was written before the queue existed.
    base::AutoLock lock(direct_write_lock_);
    StampEvidence(*evidence);
    base::DictValue message;
    message.Set("kind", "evidence");
    message.Set("protocolVersion", configuration_.protocol_version);
    message.Set("browserTimestampTicks",
                base::NumberToString(evidence->browser_timestamp_ticks));
    message.Set("channel", evidence->channel);
    message.Set("eventType", evidence->event_type);
    message.Set("payload", evidence->TakePayload());
    message.Set("qualityFlags", base::ListValue());
    return WriteMessage(std::move(message), error);
  }
  // A push that waited for the writer to free space is measured apart from
  // one that did not, since only a wait means the queue held the page back.
  static const int push_slot = RegisterCostKind("queue.push");
  static const int waited_slot = RegisterCostKind("queue.push-waited");
  const int64_t push_started = CostNowNanoseconds();
  bool waited = false;
  std::unique_ptr<QueuedEvidence> refused =
      queue_.Push(std::move(evidence), &StampEvidence, &waited);
  RecordCost(waited ? waited_slot : push_slot,
             CostNowNanoseconds() - push_started);
  if (refused) {
    *error = "Browser evidence queue was closed.";
    return false;
  }
  return true;
}

void RecorderPipeClient::ThreadMain() {
  base::PlatformThread::SetName("A11yRecorderEvidenceWriter");
  while (std::unique_ptr<QueuedEvidence> queued = queue_.Pop()) {
    const size_t bytes = queued->bytes;
    WriteQueuedEvidence(static_cast<PendingEvidence&>(*queued));
    queued.reset();
    queue_.Release(bytes);
  }
}

void RecorderPipeClient::WriteQueuedEvidence(PendingEvidence& evidence) {
  A11Y_RECORDER_COST("writer.write");
  // The parts of writer.write: building the payload, which for a deferred
  // record is where its values become a dictionary; serializing the message
  // to JSON; and writing the frame to the pipe, which includes any time the
  // pipe was full. The bytes written are counted.
  static const int payload_slot = RegisterCostKind("writer.payload");
  static const int serialize_slot = RegisterCostKind("writer.serialize");
  static const int pipe_slot = RegisterCostKind("writer.pipe-write");
  static const int bytes_slot = RegisterCountKind("count:writer.bytes");
  int64_t part_started = CostNowNanoseconds();
  base::DictValue message;
  message.Set("kind", "evidence");
  message.Set("protocolVersion", configuration_.protocol_version);
  message.Set("browserTimestampTicks",
              base::NumberToString(evidence.browser_timestamp_ticks));
  message.Set("channel", evidence.channel);
  message.Set("eventType", evidence.event_type);
  message.Set("payload", evidence.TakePayload());
  message.Set("qualityFlags", base::ListValue());
  int64_t part_ended = CostNowNanoseconds();
  RecordCost(payload_slot, part_ended - part_started);
  part_started = part_ended;
  std::string json;
  std::string error;
  bool written = SerializeMessage(message, &json, &error);
  part_ended = CostNowNanoseconds();
  RecordCost(serialize_slot, part_ended - part_started);
  if (written) {
    part_started = part_ended;
    written = WriteFrame(json, &error);
    RecordCost(pipe_slot, CostNowNanoseconds() - part_started);
    RecordCost(bytes_slot, static_cast<int64_t>(json.size()) + 4);
  }
  if (!written && write_failure_handler_) {
    write_failure_handler_(evidence.channel, evidence.lost_records_on_failure,
                           error);
  }
}

bool RecorderPipeClient::WriteMessage(base::DictValue message,
                                      std::string* error) {
  std::string json;
  return SerializeMessage(message, &json, error) && WriteFrame(json, error);
}

bool RecorderPipeClient::SerializeMessage(const base::DictValue& message,
                                          std::string* json,
                                          std::string* error) const {
  if (!base::JSONWriter::Write(message, json) ||
      json->size() > configuration_.maximum_message_bytes ||
      json->size() > std::numeric_limits<uint32_t>::max()) {
    *error = "Browser evidence message could not be serialized safely.";
    return false;
  }
  return true;
}

bool RecorderPipeClient::WriteFrame(const std::string& json,
                                    std::string* error) {
  const uint32_t length = static_cast<uint32_t>(json.size());
  std::array<uint8_t, 4> header = {
      static_cast<uint8_t>(length), static_cast<uint8_t>(length >> 8),
      static_cast<uint8_t>(length >> 16), static_cast<uint8_t>(length >> 24)};
  if (!WriteExact(pipe_.get(), base::span(header)) ||
      !WriteExact(pipe_.get(), base::as_byte_span(json))) {
    *error = "Browser evidence pipe write failed.";
    return false;
  }
  return true;
}

bool RecorderPipeClient::ReadMessage(base::DictValue* message,
                                     std::string* error) {
  std::array<uint8_t, 4> header = {};
  if (!ReadExact(pipe_.get(), base::span(header))) {
    *error = "Browser evidence pipe ended while reading a frame header.";
    return false;
  }
  const uint32_t length = static_cast<uint32_t>(header[0]) |
                          (static_cast<uint32_t>(header[1]) << 8) |
                          (static_cast<uint32_t>(header[2]) << 16) |
                          (static_cast<uint32_t>(header[3]) << 24);
  if (length == 0 || length > configuration_.maximum_message_bytes) {
    *error = "Recorder message exceeded the negotiated size limit.";
    return false;
  }

  std::string json(length, '\0');
  if (!ReadExact(pipe_.get(), base::as_writable_byte_span(json))) {
    *error = "Browser evidence pipe ended inside a message.";
    return false;
  }
  std::optional<base::Value> parsed =
      base::JSONReader::Read(json, base::JSON_PARSE_RFC);
  if (!parsed || !parsed->is_dict()) {
    *error = "Recorder message was not a JSON object.";
    return false;
  }
  *message = std::move(*parsed).TakeDict();
  return true;
}

}  // namespace a11y_recorder
