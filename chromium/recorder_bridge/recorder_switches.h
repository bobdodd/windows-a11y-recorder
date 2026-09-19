#ifndef WINDOWS_A11Y_RECORDER_CHROMIUM_RECORDER_BRIDGE_RECORDER_SWITCHES_H_
#define WINDOWS_A11Y_RECORDER_CHROMIUM_RECORDER_BRIDGE_RECORDER_SWITCHES_H_

namespace a11y_recorder {

inline constexpr char kBootstrapSwitch[] = "a11y-recorder-bootstrap";
inline constexpr char kBootstrapFromStandardInput[] = "stdin";
inline constexpr char kChildBootstrapHandleSwitch[] =
    "a11y-recorder-bootstrap-handle";
inline constexpr char kChildProcessIdSwitch[] =
    "a11y-recorder-child-process-id";
inline constexpr char kChildBootstrapMetadataEnvironment[] =
    "A11Y_RECORDER_CHILD_BOOTSTRAP_METADATA";
inline constexpr wchar_t kChildBootstrapMetadataEnvironmentWide[] =
    L"A11Y_RECORDER_CHILD_BOOTSTRAP_METADATA";
inline constexpr char kBridgeLogFileEnvironment[] =
    "A11Y_RECORDER_BRIDGE_LOG_FILE";
inline constexpr wchar_t kBridgeLogFileEnvironmentWide[] =
    L"A11Y_RECORDER_BRIDGE_LOG_FILE";
inline constexpr char kBridgeLogFileSwitch[] =
    "a11y-recorder-bridge-log-file";

// These values mirror Chromium's public process command-line contract. Keeping
// them here prevents the bridge component, which is also consumed by Blink
// core, from depending upward on //content/public/common.
inline constexpr char kChromiumProcessTypeSwitch[] = "type";
inline constexpr char kChromiumRendererProcess[] = "renderer";
inline constexpr char kChromiumGpuProcess[] = "gpu-process";
inline constexpr char kChromiumUtilityProcess[] = "utility";

}  // namespace a11y_recorder

#endif  // WINDOWS_A11Y_RECORDER_CHROMIUM_RECORDER_BRIDGE_RECORDER_SWITCHES_H_
