#ifndef WINDOWS_A11Y_RECORDER_CHROMIUM_RECORDER_BRIDGE_RECORDER_PROTOCOL_H_
#define WINDOWS_A11Y_RECORDER_CHROMIUM_RECORDER_BRIDGE_RECORDER_PROTOCOL_H_

#include <stdint.h>

#include <optional>
#include <string>
#include <string_view>

#include "base/synchronization/lock.h"
#include "base/values.h"
#include "base/win/scoped_handle.h"

namespace a11y_recorder {

inline constexpr char kProtocolVersion[] = "0.17";
inline constexpr uint32_t kDefaultMaximumMessageBytes = 4 * 1024 * 1024;

struct BootstrapConfiguration {
  std::string protocol_version;
  std::string pipe_name;
  std::string authentication_token;
  std::string browser_instance_id;
  uint32_t maximum_message_bytes = kDefaultMaximumMessageBytes;
  std::optional<int> parent_process_id;
  std::optional<int> child_process_id;
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

class RecorderPipeClient {
 public:
  explicit RecorderPipeClient(BootstrapConfiguration configuration);
  RecorderPipeClient(const RecorderPipeClient&) = delete;
  RecorderPipeClient& operator=(const RecorderPipeClient&) = delete;
  ~RecorderPipeClient();

  bool ConnectAndSynchronize(const std::string& process_type,
                             const std::string& chromium_version,
                             std::string* error);

  bool SendEvidence(int64_t browser_timestamp_ticks,
                    std::string channel,
                    std::string event_type,
                    base::DictValue payload,
                    base::ListValue quality_flags,
                    std::string* error);

  bool connected() const { return pipe_.is_valid(); }
  const std::string& browser_instance_id() const {
    return configuration_.browser_instance_id;
  }
  const std::string& process_type() const { return process_type_; }

 private:
  bool WriteMessage(base::DictValue message, std::string* error);
  bool ReadMessage(base::DictValue* message, std::string* error);

  BootstrapConfiguration configuration_;
  std::string process_type_;
  base::win::ScopedHandle pipe_;
  base::Lock write_lock_;
};

}  // namespace a11y_recorder

#endif  // WINDOWS_A11Y_RECORDER_CHROMIUM_RECORDER_BRIDGE_RECORDER_PROTOCOL_H_
