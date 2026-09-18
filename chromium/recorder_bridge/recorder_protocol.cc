#include "chromium/recorder_bridge/recorder_protocol.h"

#include <windows.h>

#include <array>
#include <iostream>
#include <limits>
#include <optional>
#include <string_view>
#include <utility>

#include "base/json/json_reader.h"
#include "base/json/json_writer.h"
#include "base/check.h"
#include "base/strings/string_number_conversions.h"
#include "base/strings/utf_string_conversions.h"
#include "base/containers/span.h"
#include "base/numerics/safe_conversions.h"

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

}  // namespace

bool ReadBootstrapFromStandardInput(BootstrapConfiguration* configuration,
                                    std::string* error) {
  if (!configuration || !error) {
    return false;
  }

  std::string line;
  if (!std::getline(std::cin, line) || line.empty()) {
    *error = "Recorder bootstrap was not available on standard input.";
    return false;
  }

  std::optional<base::Value> parsed = base::JSONReader::Read(line, base::JSON_PARSE_RFC);
  if (!parsed || !parsed->is_dict()) {
    *error = "Recorder bootstrap was not a JSON object.";
    return false;
  }

  const base::DictValue& value = parsed->GetDict();
  const std::string* kind = value.FindString("kind");
  if (!kind || *kind != "a11y-recorder-bootstrap" ||
      !RequireString(value, "protocolVersion",
                     &configuration->protocol_version, error) ||
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
  if (configuration->protocol_version != kProtocolVersion) {
    *error = "Recorder protocol version is not supported.";
    return false;
  }
  return true;
}

RecorderPipeClient::RecorderPipeClient(BootstrapConfiguration configuration)
    : configuration_(std::move(configuration)) {}

RecorderPipeClient::~RecorderPipeClient() = default;

bool RecorderPipeClient::ConnectAndSynchronize(
    const std::string& process_type,
    const std::string& chromium_version,
    std::string* error) {
  const std::wstring path =
      L"\\\\.\\pipe\\" + base::UTF8ToWide(configuration_.pipe_name);
  pipe_.Set(::CreateFileW(path.c_str(), GENERIC_READ | GENERIC_WRITE, 0,
                          nullptr, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL,
                          nullptr));
  if (!pipe_.is_valid()) {
    *error = "Could not connect to the recorder named pipe.";
    return false;
  }

  base::DictValue hello;
  hello.Set("kind", "hello");
  hello.Set("protocolVersion", configuration_.protocol_version);
  hello.Set("authenticationToken", configuration_.authentication_token);
  hello.Set("browserInstanceId", configuration_.browser_instance_id);
  hello.Set("processId", static_cast<int>(::GetCurrentProcessId()));
  hello.Set("processType", process_type);
  hello.Set("chromiumVersion", chromium_version);
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
  response.Set("browserSendTicks",
               base::NumberToString(QueryMonotonicTicks()));
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
  return true;
}

bool RecorderPipeClient::SendEvidence(int64_t browser_timestamp_ticks,
                                      std::string channel,
                                      std::string event_type,
                                      base::DictValue payload,
                                      base::ListValue quality_flags,
                                      std::string* error) {
  base::DictValue message;
  message.Set("kind", "evidence");
  message.Set("protocolVersion", configuration_.protocol_version);
  message.Set("browserTimestampTicks",
              base::NumberToString(browser_timestamp_ticks));
  message.Set("channel", std::move(channel));
  message.Set("eventType", std::move(event_type));
  message.Set("payload", std::move(payload));
  message.Set("qualityFlags", std::move(quality_flags));
  return WriteMessage(std::move(message), error);
}

bool RecorderPipeClient::WriteMessage(base::DictValue message,
                                      std::string* error) {
  std::string json;
  if (!base::JSONWriter::Write(message, &json) ||
      json.size() > configuration_.maximum_message_bytes ||
      json.size() > std::numeric_limits<uint32_t>::max()) {
    *error = "Browser evidence message could not be serialized safely.";
    return false;
  }

  const uint32_t length = static_cast<uint32_t>(json.size());
  std::array<uint8_t, 4> header = {
      static_cast<uint8_t>(length),
      static_cast<uint8_t>(length >> 8),
      static_cast<uint8_t>(length >> 16),
      static_cast<uint8_t>(length >> 24)};
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
  std::optional<base::Value> parsed = base::JSONReader::Read(json, base::JSON_PARSE_RFC);
  if (!parsed || !parsed->is_dict()) {
    *error = "Recorder message was not a JSON object.";
    return false;
  }
  *message = std::move(*parsed).TakeDict();
  return true;
}

}  // namespace a11y_recorder
