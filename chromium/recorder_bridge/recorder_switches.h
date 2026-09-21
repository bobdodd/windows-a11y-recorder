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
inline constexpr wchar_t kBridgeLogFileEnvironmentWide[] =
    L"A11Y_RECORDER_BRIDGE_LOG_FILE";

// A recorder-launched browser whose bridge cannot initialize exits with this
// code. The value is outside Chromium's own result-code range, so the recorder
// can distinguish a failed bridge from a browser that exited normally. A normal
// exit code here would make a failed bridge indistinguishable from success.
// The recorder declares the same value as
// ChromiumLauncher.BridgeInitializationFailureExitCode.
inline constexpr int kBridgeInitializationFailureExitCode = 0xA11B;

// These values mirror Chromium's public process command-line contract. Keeping
// them here prevents the bridge component, which is also consumed by Blink
// core, from depending upward on //content/public/common.
inline constexpr char kChromiumProcessTypeSwitch[] = "type";
inline constexpr char kChromiumRendererProcess[] = "renderer";
inline constexpr char kChromiumGpuProcess[] = "gpu-process";
inline constexpr char kChromiumUtilityProcess[] = "utility";
inline constexpr char kChromiumEnableLoggingSwitch[] = "enable-logging";
inline constexpr char kChromiumLogFileSwitch[] = "log-file";
inline constexpr char kChromiumLoggingToHandle[] = "handle";

}  // namespace a11y_recorder

#endif  // WINDOWS_A11Y_RECORDER_CHROMIUM_RECORDER_BRIDGE_RECORDER_SWITCHES_H_
