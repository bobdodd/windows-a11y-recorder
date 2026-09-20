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

// Adds one Node entry to the active dispatch path, in Blink path order.
COMPONENT_EXPORT(RECORDER_BRIDGE)
void RecordBlinkDispatchPathNode(uintptr_t event_identity,
                                 int document_node_id,
                                 int node_id,
                                 std::string tag_name,
                                 std::string element_id);

// Emits dispatch-started after the complete Node path has been accumulated.
COMPONENT_EXPORT(RECORDER_BRIDGE)
void CompleteBlinkDispatchStart(uintptr_t event_identity);

// Preserves listener correlation across a callback that removes itself.
COMPONENT_EXPORT(RECORDER_BRIDGE)
void BeginBlinkListenerInvocation(uintptr_t event_identity,
                                  uintptr_t listener_identity,
                                  int current_document_node_id,
                                  int current_target_node_id,
                                  std::string current_target_tag_name,
                                  std::string current_target_element_id);

// Records the state immediately after Blink invokes one listener.
COMPONENT_EXPORT(RECORDER_BRIDGE)
void RecordBlinkListenerInvoked(uintptr_t event_identity,
                                int event_phase,
                                bool default_prevented,
                                bool propagation_stopped,
                                bool immediate_propagation_stopped);

// Records Blink's decision at the Node default-event-handler boundary. An
// invoked record means Blink entered DefaultEventHandler for the identified
// Node; it does not by itself assert that the handler changed browser state.
COMPONENT_EXPORT(RECORDER_BRIDGE)
void RecordBlinkDefaultAction(uintptr_t event_identity,
                              int current_document_node_id,
                              int current_target_node_id,
                              std::string current_target_tag_name,
                              std::string current_target_element_id,
                              int outcome,
                              bool default_prevented,
                              bool propagation_stopped,
                              bool immediate_propagation_stopped);

// Records an accepted window setTimeout or setInterval after Blink assigns its
// timeout ID and applies delay clamping. Unknown scheduler throttling and page
// lifecycle state are represented explicitly in the payload.
COMPONENT_EXPORT(RECORDER_BRIDGE)
void RecordBlinkTimerScheduled(uintptr_t timer_identity,
                               int document_node_id,
                               int timeout_id,
                               bool repeating,
                               double requested_delay_milliseconds,
                               double effective_delay_milliseconds,
                               int nesting_level,
                               int page_lifecycle_state);

// Records callback entry for a previously scheduled DOM timer. The evidence
// timestamp is the observed firing time; it does not imply callback completion.
COMPONENT_EXPORT(RECORDER_BRIDGE)
void RecordBlinkTimerFired(uintptr_t timer_identity,
                           bool repeating,
                           int page_lifecycle_state);

// Records explicit clearTimeout or clearInterval removal of a live DOM timer.
COMPONENT_EXPORT(RECORDER_BRIDGE)
void RecordBlinkTimerCancelled(uintptr_t timer_identity,
                               int page_lifecycle_state);

// Records an accepted web-exposed requestAnimationFrame callback. Delay,
// nesting, throttling, lifecycle, and source-location facts that are not
// observed at this boundary remain null or explicitly unknown.
COMPONENT_EXPORT(RECORDER_BRIDGE)
void RecordBlinkAnimationFrameScheduled(uintptr_t callback_identity,
                                        int document_node_id,
                                        int callback_id,
                                        int page_lifecycle_state);

// Records entry into a previously scheduled web-exposed animation-frame
// callback. This does not imply callback completion or frame presentation.
COMPONENT_EXPORT(RECORDER_BRIDGE)
void RecordBlinkAnimationFrameFired(uintptr_t callback_identity,
                                    int page_lifecycle_state);

// Records explicit cancelAnimationFrame removal of a live callback.
COMPONENT_EXPORT(RECORDER_BRIDGE)
void RecordBlinkAnimationFrameCancelled(uintptr_t callback_identity,
                                        int page_lifecycle_state);

// Records an accepted web-exposed requestIdleCallback registration and its
// requested timeout. A zero timeout means no positive timeout was requested.
COMPONENT_EXPORT(RECORDER_BRIDGE)
void RecordBlinkIdleCallbackScheduled(uintptr_t callback_identity,
                                      int document_node_id,
                                      int callback_id,
                                      bool has_timeout,
                                      double timeout_milliseconds,
                                      int page_lifecycle_state);

// Records entry into a previously scheduled idle callback and whether Blink
// invoked it because its timeout elapsed. This does not imply completion.
COMPONENT_EXPORT(RECORDER_BRIDGE)
void RecordBlinkIdleCallbackFired(uintptr_t callback_identity,
                                  bool did_timeout,
                                  int page_lifecycle_state);

// Records explicit cancelIdleCallback removal of a live callback.
COMPONENT_EXPORT(RECORDER_BRIDGE)
void RecordBlinkIdleCallbackCancelled(uintptr_t callback_identity,
                                      int page_lifecycle_state);

// Starts one bounded structural checkpoint at a named Blink document boundary.
// The document token is Chromium's shared browser-renderer document identity.
// Returns zero when the recorder is not connected.
COMPONENT_EXPORT(RECORDER_BRIDGE)
uint64_t BeginBlinkDomCheckpoint(int document_node_id,
                                 std::string document_token,
                                 std::string reason,
                                 int maximum_nodes);

// Records one node in preorder. Text content and attributes are intentionally
// excluded from this initial structural evidence boundary.
COMPONENT_EXPORT(RECORDER_BRIDGE)
void RecordBlinkDomCheckpointNode(uint64_t checkpoint_sequence,
                                  int document_node_id,
                                  std::string document_token,
                                  int node_index,
                                  int node_id,
                                  int parent_node_id,
                                  int node_type,
                                  std::string node_name);

// Completes the checkpoint and explicitly reports whether its node limit was
// reached before the complete document tree was emitted.
COMPONENT_EXPORT(RECORDER_BRIDGE)
void CompleteBlinkDomCheckpoint(uint64_t checkpoint_sequence,
                                int document_node_id,
                                std::string document_token,
                                std::string reason,
                                int node_count,
                                bool truncated,
                                int maximum_nodes);

// Records an authoritative task-queue scheduler decision only when the final
// allowed wake-up is later than the desired wake-up. This queue boundary does
// not identify an individual DOM timer or document.
COMPONENT_EXPORT(RECORDER_BRIDGE)
void RecordBlinkSchedulerWakeUpDeferred(
    int queue_type,
    int throttling_type,
    int64_t desired_wake_up_microseconds,
    int64_t allowed_wake_up_microseconds,
    bool has_ready_task,
    int block_type);

// Records a navigation boundary in the browser process. Page and frame
// identifiers remain stable across same-document navigations.
// A committed document identifier comes from the RenderFrameHost navigation
// that created that document and is absent before commit.
COMPONENT_EXPORT(RECORDER_BRIDGE)
void RecordBrowserNavigationStarted(int64_t navigation_id,
                                    int page_frame_tree_node_id,
                                    int frame_tree_node_id,
                                    int parent_frame_tree_node_id,
                                    int parent_or_outer_document_frame_tree_node_id,
                                    std::string frame_type,
                                    bool primary_page,
                                    std::string url,
                                    bool renderer_initiated,
                                    bool same_document);

// A committed completion carries Chromium's shared document token and the
// renderer process that hosts that document.
COMPONENT_EXPORT(RECORDER_BRIDGE)
void RecordBrowserNavigationCompleted(int64_t navigation_id,
                                      int page_frame_tree_node_id,
                                      int frame_tree_node_id,
                                      int parent_frame_tree_node_id,
                                      int parent_or_outer_document_frame_tree_node_id,
                                      std::string frame_type,
                                      bool primary_page,
                                      int64_t document_navigation_id,
                                      std::string document_token,
                                      int renderer_process_id,
                                      std::string url,
                                      bool renderer_initiated,
                                      bool same_document,
                                      bool committed,
                                      bool error_page,
                                      int net_error_code);

// Records the final dispatch result and releases the active dispatch identity.
COMPONENT_EXPORT(RECORDER_BRIDGE)
void RecordBlinkDispatchCompleted(uintptr_t event_identity,
                                  int dispatch_result,
                                  bool default_prevented,
                                  bool propagation_stopped,
                                  bool immediate_propagation_stopped);

}  // namespace a11y_recorder

#endif  // WINDOWS_A11Y_RECORDER_CHROMIUM_RECORDER_BRIDGE_BROWSER_BRIDGE_H_
