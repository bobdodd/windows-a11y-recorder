#ifndef WINDOWS_A11Y_RECORDER_CHROMIUM_RECORDER_BRIDGE_BROWSER_BRIDGE_H_
#define WINDOWS_A11Y_RECORDER_CHROMIUM_RECORDER_BRIDGE_BROWSER_BRIDGE_H_

#include <stdint.h>

#include <string>
#include <string_view>

#include "base/component_export.h"

namespace base {
class CommandLine;
struct LaunchOptions;
}  // namespace base

namespace a11y_recorder {

class RecorderPipeClient;

// Initializes the recorder connection in the current Chromium process.
// Returns true when recording was not requested or the connection is ready.
// When the bootstrap switch is present, failure is fatal to recorder-launched
// Chromium so a session cannot silently continue without browser evidence.
COMPONENT_EXPORT(RECORDER_BRIDGE)
bool InitializeProcessBridge(std::string* error);

// Adds an inherited, read-only shared-memory capability to supported Chromium
// child processes. The command-line switch contains only handle metadata; the
// authentication token remains inside the inherited shared-memory region.
COMPONENT_EXPORT(RECORDER_BRIDGE)
bool AppendRecorderBootstrapToChildProcess(base::CommandLine* command_line,
                                           base::LaunchOptions* launch_options,
                                           int child_process_id,
                                           std::string* error);

// Appends a non-secret startup diagnostic when the opt-in bridge log
// environment variable is present. This works before Chromium logging starts.
COMPONENT_EXPORT(RECORDER_BRIDGE)
void WriteRecorderBridgeDiagnostic(std::string_view message);

// Returns the connected client for the current process, or nullptr when
// Chromium was not launched by the recorder.
COMPONENT_EXPORT(RECORDER_BRIDGE)
RecorderPipeClient* GetProcessRecorderClient();

// Records a Blink listener only after Blink has accepted the registration.
// Node identifiers are Blink DOMNodeIds.
COMPONENT_EXPORT(RECORDER_BRIDGE)
void RecordBlinkListenerRegistered(uintptr_t listener_identity,
                                   int document_node_id,
                                   int target_node_id,
                                   std::string event_name,
                                   std::string target_tag_name,
                                   std::string target_element_id,
                                   bool capture,
                                   bool passive,
                                   bool once);

// Records a listener only after Blink has accepted its removal. The listener
// identifier is the same one allocated when the registration was accepted.
COMPONENT_EXPORT(RECORDER_BRIDGE)
void RecordBlinkListenerRemoved(uintptr_t listener_identity,
                                int document_node_id,
                                int target_node_id,
                                std::string event_name,
                                std::string target_tag_name,
                                std::string target_element_id,
                                bool capture,
                                bool passive,
                                bool once);

// Records one dispatch-started event after Blink has established the event
// path and original target, but before capture-phase listeners run.
COMPONENT_EXPORT(RECORDER_BRIDGE)
void RecordBlinkDispatchStarted(uintptr_t event_identity,
                                int document_node_id,
                                int target_node_id,
                                std::string event_name,
                                std::string target_tag_name,
                                std::string target_element_id,
                                bool trusted);

// Preserves listener correlation across a callback that removes itself.
COMPONENT_EXPORT(RECORDER_BRIDGE)
void BeginBlinkListenerInvocation(uintptr_t event_identity,
                                  uintptr_t listener_identity);

// Records the state immediately after Blink invokes one listener.
COMPONENT_EXPORT(RECORDER_BRIDGE)
void RecordBlinkListenerInvoked(uintptr_t event_identity,
                                int event_phase,
                                bool default_prevented,
                                bool propagation_stopped,
                                bool immediate_propagation_stopped);

// Records the final dispatch result and releases the active dispatch identity.
COMPONENT_EXPORT(RECORDER_BRIDGE)
void RecordBlinkDispatchCompleted(uintptr_t event_identity,
                                  int dispatch_result,
                                  bool default_prevented,
                                  bool propagation_stopped,
                                  bool immediate_propagation_stopped);

}  // namespace a11y_recorder

#endif  // WINDOWS_A11Y_RECORDER_CHROMIUM_RECORDER_BRIDGE_BROWSER_BRIDGE_H_
