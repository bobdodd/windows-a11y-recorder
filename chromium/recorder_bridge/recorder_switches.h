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

// Asks the browser to report the evidence protocol version it was built with
// and exit without starting. A file written beside the executable could be
// separated from the executable it describes, so the recorder asks the binary
// it is about to run rather than trusting anything alongside it.
inline constexpr char kPrintProtocolVersionSwitch[] =
    "a11y-recorder-print-protocol-version";

// The reported version is written to standard output behind this prefix, and
// the process exits with this code. The code distinguishes a browser that
// answered the query from one that ignored an unknown switch and started
// normally. The recorder declares the same values as
// ChromiumLauncher.ProtocolVersionQueryExitCode and
// ChromiumLauncher.ProtocolVersionOutputPrefix.
inline constexpr int kProtocolVersionQueryExitCode = 0xA11C;
inline constexpr char kProtocolVersionOutputPrefix[] =
    "a11y-recorder-protocol-version=";

// Marks a network service process that a recording browser launched. It
// carries no capability and no connection to the recorder: it tells the
// network service to report WebSocket handshake cookies to the renderer by
// name only, instead of dropping them. A network service running inside the
// browser process reads kBootstrapSwitch instead.
inline constexpr char kRecordingNetworkServiceSwitch[] =
    "a11y-recorder-recording-network-service";

// These values mirror Chromium's public process command-line contract. Keeping
// them here prevents the bridge component, which is also consumed by Blink
// core, from depending upward on //content/public/common.
inline constexpr char kChromiumProcessTypeSwitch[] = "type";
inline constexpr char kChromiumRendererProcess[] = "renderer";
inline constexpr char kChromiumGpuProcess[] = "gpu-process";
inline constexpr char kChromiumUtilityProcess[] = "utility";
inline constexpr char kChromiumUtilitySubTypeSwitch[] = "utility-sub-type";
inline constexpr char kChromiumNetworkServiceSubType[] =
    "network.mojom.NetworkService";
inline constexpr char kChromiumEnableLoggingSwitch[] = "enable-logging";
inline constexpr char kChromiumLogFileSwitch[] = "log-file";
inline constexpr char kChromiumLoggingToHandle[] = "handle";

}  // namespace a11y_recorder

#endif  // WINDOWS_A11Y_RECORDER_CHROMIUM_RECORDER_BRIDGE_RECORDER_SWITCHES_H_
