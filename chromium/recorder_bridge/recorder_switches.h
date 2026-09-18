#ifndef WINDOWS_A11Y_RECORDER_CHROMIUM_RECORDER_BRIDGE_RECORDER_SWITCHES_H_
#define WINDOWS_A11Y_RECORDER_CHROMIUM_RECORDER_BRIDGE_RECORDER_SWITCHES_H_

namespace a11y_recorder {

inline constexpr char kBootstrapSwitch[] = "a11y-recorder-bootstrap";
inline constexpr char kBootstrapFromStandardInput[] = "stdin";
inline constexpr char kChildBootstrapHandleSwitch[] =
    "a11y-recorder-bootstrap-handle";
inline constexpr char kChildProcessIdSwitch[] =
    "a11y-recorder-child-process-id";

}  // namespace a11y_recorder

#endif  // WINDOWS_A11Y_RECORDER_CHROMIUM_RECORDER_BRIDGE_RECORDER_SWITCHES_H_
