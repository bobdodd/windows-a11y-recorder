#ifndef WINDOWS_A11Y_RECORDER_CHROMIUM_RECORDER_BRIDGE_RECORDER_PROTOCOL_H_
#define WINDOWS_A11Y_RECORDER_CHROMIUM_RECORDER_BRIDGE_RECORDER_PROTOCOL_H_

#include <stdint.h>

#include <string>

#include "base/values.h"
#include "base/win/scoped_handle.h"

namespace a11y_recorder {

inline constexpr char kProtocolVersion[] = "0.1";
inline constexpr uint32_t kDefaultMaximumMessageBytes = 4 * 1024 * 1024;

struct BootstrapConfiguration {
  std::string protocol_version;
  std::string pipe_name;
  std::string authentication_token;
  std::string browser_instance_id;
  uint32_t maximum_message_bytes = kDefaultMaximumMessageBytes;
};

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

 private:
  bool WriteMessage(base::DictValue message, std::string* error);
  bool ReadMessage(base::DictValue* message, std::string* error);

  BootstrapConfiguration configuration_;
  base::win::ScopedHandle pipe_;
};

}  // namespace a11y_recorder

#endif  // WINDOWS_A11Y_RECORDER_CHROMIUM_RECORDER_BRIDGE_RECORDER_PROTOCOL_H_
