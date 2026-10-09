#ifndef WINDOWS_A11Y_RECORDER_CHROMIUM_RECORDER_BRIDGE_BROWSER_BRIDGE_H_
#define WINDOWS_A11Y_RECORDER_CHROMIUM_RECORDER_BRIDGE_BROWSER_BRIDGE_H_

#include <stdint.h>

#include <limits>
#include <optional>

#include <string>
#include <string_view>
#include <vector>

#include "base/component_export.h"
#include "base/values.h"
#include "chromium/recorder_bridge/full_walks.h"
#include "chromium/recorder_bridge/layout_changes.h"
#include "chromium/recorder_bridge/recorder_switches.h"
#include "chromium/recorder_bridge/recreation_compositor_values.h"
#include "chromium/recorder_bridge/recreation_paint_worklet_values.h"
#include "chromium/recorder_bridge/recreation_preferences.h"
#include "chromium/recorder_bridge/recreation_image_frames.h"

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

// Reports the evidence protocol version this browser was built with when the
// query switch is present, and returns true when it did. The caller must then
// exit with kProtocolVersionQueryExitCode without starting a browser. This lets
// the recorder refuse a mismatched pair before it launches a session, instead
// of discovering the mismatch from a browser that has already failed.
COMPONENT_EXPORT(RECORDER_BRIDGE)
bool WriteProtocolVersionIfRequested();

// Adds an inherited, read-only shared-memory capability to supported Chromium
// child processes. The command-line switch contains only handle metadata; the
// authentication token remains inside the inherited shared-memory region.
COMPONENT_EXPORT(RECORDER_BRIDGE)
bool AppendRecorderBootstrapToChildProcess(base::CommandLine* command_line,
                                           base::LaunchOptions* launch_options,
                                           int child_process_id,
                                           std::string* error);

// Returns whether this process runs in the recreation mode: the browser was
// started with --a11y-recorder-recreation, which it passes to its renderers.
COMPONENT_EXPORT(RECORDER_BRIDGE)
bool IsRecreationMode();

// "Input refused only in the recreation": true for a browser page's URL
// scheme (devtools, chrome, chrome-untrusted, chrome-extension), whose page
// takes input in the recreation mode as in any Chromium.
COMPONENT_EXPORT(RECORDER_BRIDGE)
bool IsRecreationBrowserPageScheme(std::string_view scheme);

// Marks, on the main thread, that this renderer process shows a browser
// page: called when such a page's parser is created, before it can be drawn
// or take input. Chromium keeps browser pages out of web content's
// processes, so the whole process is then a browser page's.
COMPONENT_EXPORT(RECORDER_BRIDGE)
void MarkRecreationBrowserPageProcess();

// True, on any thread, when this process runs in the recreation mode and
// shows no browser page, so that the compositor thread refuses its input.
COMPONENT_EXPORT(RECORDER_BRIDGE)
bool RecreationRefusesCompositorInput();

// True, on any thread, when this process runs in the recreation mode and
// shows no browser page, so that its page is held at the recorded moment:
// no CSS animation or transition is run, and the recorded compositor values
// are imposed ("Sub-step 2b-i design: compositor values imposed"). A browser
// page, such as DevTools, runs its own animations as usual.
COMPONENT_EXPORT(RECORDER_BRIDGE)
bool RecreationHoldsTime();

// "Sub-step 2a design: animated images held": in the recreation mode, Blink's
// main thread gives the value of the X-A11y-Recorder-Image-Frame header of
// the recorder's answer for an image, and its paint image is held at that
// frame index; cc reads it, on the compositor thread, for each animated
// image it is given. An empty or malformed value holds nothing. Does nothing
// outside the recreation mode.
COMPONENT_EXPORT(RECORDER_BRIDGE)
void HoldRecreationImageFrame(int64_t paint_image_id, std::string frame_header);

// The frame index a paint image is held at in the recreation mode, or none.
COMPONENT_EXPORT(RECORDER_BRIDGE)
std::optional<size_t> RecreationHeldImageFrame(int64_t paint_image_id);

// "Sub-step 2b-i design: compositor values imposed": in the recreation mode,
// the compositor values of an element's data-a11y-recorded-compositor
// attribute, which Blink imposes on its paint properties and style. Text not
// of the attribute's form gives none. Gives none outside the recreation mode.
COMPONENT_EXPORT(RECORDER_BRIDGE)
RecreationCompositorValues RecreationCompositorValuesOf(std::string_view text);

// "Sub-step 2c design: paint worklet colors and clip paths imposed": in the
// recreation mode, the native paint worklet values of an element's
// data-a11y-recorded-paint-worklet attribute, which Blink imposes on its
// style and clip path. Text not of the attribute's form gives none. Gives
// none outside the recreation mode.
COMPONENT_EXPORT(RECORDER_BRIDGE)
RecreationPaintWorkletValues RecreationPaintWorkletValuesOf(
    std::string_view text);

// Stage 3 of accessibility preferences ("Stage 3: the recreation" in
// docs/architecture/accessibility-preferences.md): in the recreation mode,
// the recorded preferences of the data-a11y-recorded-preferences attribute
// of a recreated document's root element, which Blink gives the page in
// place of those the recreation browser sends. Text not of the attribute's
// form gives none. Gives none outside the recreation mode.
COMPONENT_EXPORT(RECORDER_BRIDGE)
RecreationPreferences RecreationPreferencesOf(std::string_view text);

// Appends a non-secret startup diagnostic when the opt-in bridge log
// environment variable is present. This works before Chromium logging starts.
COMPONENT_EXPORT(RECORDER_BRIDGE)
void WriteRecorderBridgeDiagnostic(std::string_view message);

// Returns the connected client for the current process, or nullptr when
// Chromium was not launched by the recorder.
COMPONENT_EXPORT(RECORDER_BRIDGE)
RecorderPipeClient* GetProcessRecorderClient();

// Identifies the kind of EventTarget a record is about. A listener on a Window
// or another non-Node EventTarget has no DOM node identifier, so the kind is
// recorded rather than inferred from a missing node. These are header constants
// rather than exported data so a patched Chromium source can name a kind
// without depending on the bridge's data exports.
inline constexpr char kEventTargetKindNode[] = "node";
inline constexpr char kEventTargetKindWindow[] = "window";
inline constexpr char kEventTargetKindOther[] = "other";

// Names the execution context a listener or dispatch record belongs to. A
// listener or dispatch in a dedicated, shared, or service worker, or in a
// worklet, belongs to no document, so its records name the global scope
// instead: the context kind, the worker's DevTools token, which is the same
// token that worker's network records carry, and the global object URL. A
// window scope carries no worker token or URL, because the document already
// identifies it. The context kind is one of window, dedicated-worker,
// shared-worker, service-worker, worklet, or other, and the bridge records any
// other value as other.
struct EventScope {
  std::string context_kind;
  std::string worker_token;
  std::string global_object_url;
};

// Identifies how a listener entered Blink's listener map. Blink accepts the
// three forms through one internal registration path, and the distinction is
// read from the listener object Blink created rather than from the call site:
// a content-attribute handler is a JSEventHandlerForContentAttribute, any other
// event handler is the one an on-event IDL attribute setter created, and
// everything else arrived through addEventListener. A native listener is one
// Blink itself installed, which reports no script handler.
inline constexpr char kListenerRegistrationKindAddEventListener[] =
    "add-event-listener";
inline constexpr char kListenerRegistrationKindInlineAttribute[] =
    "inline-attribute";
inline constexpr char kListenerRegistrationKindEventHandlerProperty[] =
    "event-handler-property";

// Identifies the JavaScript world a listener callback belongs to. Blink keeps
// the main world, each isolated world an embedder creates, each world the
// inspector creates, worker and worklet worlds, and shadow realms in one world
// type enumeration, and a recorded kind is a reading of that enumeration. A
// world Blink classifies as none of these, such as the utility world used for
// context snapshotting, is recorded as other, so a future world type is named
// honestly instead of being reported as a world it is not. These are header
// constants for the same reason the target and registration kinds are.
inline constexpr char kExecutionWorldKindMain[] = "main";
inline constexpr char kExecutionWorldKindIsolated[] = "isolated";
inline constexpr char kExecutionWorldKindInspectorIsolated[] =
    "inspector-isolated";
inline constexpr char kExecutionWorldKindWorkerOrWorklet[] =
    "worker-or-worklet";
inline constexpr char kExecutionWorldKindShadowRealm[] = "shadow-realm";
inline constexpr char kExecutionWorldKindOther[] = "other";

// The world identifier Blink uses for a world it has not classified. A hook
// that observed no world passes this together with an empty world kind.
inline constexpr int kExecutionWorldIdUnobserved = -1;

// Records a Blink listener only after Blink has accepted the registration.
// Node identifiers are Blink DOMNodeIds. A non-Node target passes a zero
// target node identifier, its Blink interface name, and the address Blink uses
// for the target, which is mapped to a stable process-local target identifier.
COMPONENT_EXPORT(RECORDER_BRIDGE)
// The trailing five parameters of each listener entry point describe where the
// call that produced the record came from, as Blink reported it. An empty script
// URL, an empty function name, and a zero script identifier, line, or column
// each mean that fact was not observed, which Blink itself represents the same
// way; a record whose location is unknown in every field reports a null
// location rather than an object of nulls. The location describes the call that
// registered, removed, or replaced the listener, not where its callback
// function was defined.
// The four parameters before the scope describe the
// JavaScript world the callback belongs to, which is the world the registration
// was made from rather than the world that happened to be current when the
// record was written. An empty world kind means Blink reported no world, which
// is the case for a listener Blink installed itself, and a record with no world
// reports a null world rather than a world of nulls. An empty world name or
// stable identifier means Blink holds none for that world. The final parameter
// names the execution context the target belongs to. A target in a worker or
// worklet scope reports a zero document node identifier, and only a non-Node
// target in such a scope is recorded without a document.
void RecordBlinkListenerRegistered(uintptr_t listener_identity,
                                   std::string registration_kind,
                                   std::string target_kind,
                                   std::string target_interface_name,
                                   uintptr_t target_identity,
                                   int document_node_id,
                                   int target_node_id,
                                   std::string event_name,
                                   std::string target_tag_name,
                                   std::string target_element_id,
                                   bool capture,
                                   bool passive,
                                   bool once,
                                   std::string script_url,
                                   std::string function_name,
                                   int script_id,
                                   int line_number,
                                   int column_number,
                                   std::string world_kind,
                                   int world_id,
                                   std::string world_name,
                                   std::string world_stable_id,
                                   EventScope scope);

// Records a listener only after Blink has accepted its removal. The listener
// identifier is the same one allocated when the registration was accepted.
COMPONENT_EXPORT(RECORDER_BRIDGE)
void RecordBlinkListenerRemoved(uintptr_t listener_identity,
                                std::string registration_kind,
                                std::string target_kind,
                                std::string target_interface_name,
                                uintptr_t target_identity,
                                int document_node_id,
                                int target_node_id,
                                std::string event_name,
                                std::string target_tag_name,
                                std::string target_element_id,
                                bool capture,
                                bool passive,
                                bool once,
                                std::string script_url,
                                std::string function_name,
                                int script_id,
                                int line_number,
                                int column_number,
                                std::string world_kind,
                                int world_id,
                                std::string world_name,
                                std::string world_stable_id,
                                EventScope scope);

// Records that Blink replaced the callback of an existing attribute listener
// registration in place. Assigning an on-event IDL attribute over a listener
// that a content attribute or an earlier assignment established does not add or
// remove a registration, so neither of those records is emitted and the
// registration keeps the identity it was given. Without this record the
// registration kind reported for that identity would describe the callback
// Blink no longer holds.
COMPONENT_EXPORT(RECORDER_BRIDGE)
void RecordBlinkListenerCallbackReplaced(uintptr_t listener_identity,
                                        std::string registration_kind,
                                        std::string target_kind,
                                        std::string target_interface_name,
                                        uintptr_t target_identity,
                                        int document_node_id,
                                        int target_node_id,
                                        std::string event_name,
                                        std::string target_tag_name,
                                        std::string target_element_id,
                                        bool capture,
                                        bool passive,
                                        bool once,
                                        std::string script_url,
                                        std::string function_name,
                                        int script_id,
                                        int line_number,
                                        int column_number,
                                        std::string world_kind,
                                        int world_id,
                                        std::string world_name,
                                        std::string world_stable_id,
                                         EventScope scope);

// Records one dispatch-started event after Blink has established the event
// path and original target, but before capture-phase listeners run.
COMPONENT_EXPORT(RECORDER_BRIDGE)
void RecordBlinkDispatchStarted(uintptr_t event_identity,
                                int document_node_id,
                                int target_node_id,
                                std::string event_name,
                                std::string target_tag_name,
                                std::string target_element_id,
                                bool trusted,
                                EventScope scope);

// Opens a dispatch that does not pass through Blink's Node event dispatcher:
// the at-target dispatch EventTarget performs for a target that is not a Node,
// the dispatch a window performs for its own load and pageshow events with the
// document as target, and the IndexedDB dispatcher's request, transaction, and
// database propagation. Returns true only when this call opened the dispatch,
// so a hook that finds the event already being recorded neither adds path
// entries to it nor completes it. The original target is described the same
// way a listener target is. Path entries follow through
// RecordBlinkDispatchPathTarget and the start record through
// CompleteBlinkDispatchStart.
COMPONENT_EXPORT(RECORDER_BRIDGE)
bool BeginBlinkTargetDispatch(uintptr_t event_identity,
                              std::string target_kind,
                              std::string target_interface_name,
                              uintptr_t target_identity,
                              int document_node_id,
                              int target_node_id,
                              std::string target_tag_name,
                              std::string target_element_id,
                              std::string event_name,
                              bool trusted,
                              EventScope scope);

// Adds one entry, in Blink path order, to a dispatch opened by
// BeginBlinkTargetDispatch. Such a path has no tree scopes, so each entry's
// scope reports only the original target's node identifier, when the original
// target is a Node, and whether composedPath() returns this entry to a
// listener on it, which Blink decides per entry for these paths.
COMPONENT_EXPORT(RECORDER_BRIDGE)
void RecordBlinkDispatchPathTarget(uintptr_t event_identity,
                                   std::string target_kind,
                                   std::string target_interface_name,
                                   uintptr_t target_identity,
                                   int document_node_id,
                                   int target_node_id,
                                   std::string target_tag_name,
                                   std::string target_element_id,
                                   int scope_target_node_id,
                                   bool visible_to_listener);

// Adds one Node entry to the active dispatch path, in Blink path order.
COMPONENT_EXPORT(RECORDER_BRIDGE)
void RecordBlinkDispatchPathNode(uintptr_t event_identity,
                                 int document_node_id,
                                 int node_id,
                                 std::string tag_name,
                                 std::string element_id,
                                 int tree_scope_root_node_id,
                                 std::string shadow_root_mode,
                                 int target_node_id,
                                 int related_target_node_id,
                                 std::vector<int> visible_path_indexes);

// Adds the Window entry that terminates a Node dispatch path. Blink keeps the
// Window outside its NodeEventContexts, so it is appended separately rather
// than represented as a Node.
COMPONENT_EXPORT(RECORDER_BRIDGE)
void RecordBlinkDispatchPathWindow(uintptr_t event_identity,
                                   int document_node_id,
                                   uintptr_t target_identity,
                                   std::string interface_name,
                                   int target_node_id,
                                   int related_target_node_id,
                                   std::vector<int> visible_path_indexes);

// Emits dispatch-started after the complete Node path has been accumulated.
COMPONENT_EXPORT(RECORDER_BRIDGE)
void CompleteBlinkDispatchStart(uintptr_t event_identity);

// Preserves listener correlation across a callback that removes itself.
COMPONENT_EXPORT(RECORDER_BRIDGE)
void BeginBlinkListenerInvocation(uintptr_t event_identity,
                                  uintptr_t listener_identity,
                                  std::string current_target_kind,
                                  std::string current_target_interface_name,
                                  uintptr_t current_target_identity,
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
// Returns zero when the recorder is not connected, or when the document is not
// walked at this request (protocol 0.35): a "post-mutation" request is walked
// only when the document has no walk yet, when a DOM record was lost since its
// last walk, or at the recording's check interval. The caller still records
// the interaction snapshot of a mutation delivery that was not walked.
//
// Protocol 0.55 (slice 5a): the started record also names the DevTools frame
// token of the document's frame, and whether that frame is a main frame. An
// empty token means the document has no frame; both are then written as null.
COMPONENT_EXPORT(RECORDER_BRIDGE)
uint64_t BeginBlinkDomCheckpoint(int document_node_id,
                                 std::string document_token,
                                 std::string reason,
                                 int maximum_nodes,
                                 std::string frame_token,
                                 bool main_frame);

// Protocol 0.55 (slice 5a): records, after the node record of a frame owner
// element (iframe, frame, object, embed, or fencedframe) in the same
// checkpoint, the DevTools frame token of the frame it holds, and whether that
// frame is remote in this renderer. An owner that holds no frame is not
// recorded.
COMPONENT_EXPORT(RECORDER_BRIDGE)
void RecordBlinkDomCheckpointFrameOwner(uint64_t checkpoint_sequence,
                                        int document_node_id,
                                        std::string document_token,
                                        int owner_node_id,
                                        std::string frame_token,
                                        bool remote);

// Protocol 0.55 (slice 5a): records that a frame owner element was given a
// frame, from HTMLFrameOwnerElement::SetContentFrame, or lost it, from
// ClearContentFrame, in which case the frame token is empty and written as
// null. The record is not a DOM transition: it takes no transition ID.
COMPONENT_EXPORT(RECORDER_BRIDGE)
void RecordBlinkDomFrameOwnerChanged(int document_node_id,
                                     std::string document_token,
                                     int owner_node_id,
                                     std::string frame_token,
                                     bool remote);

// Records one node in preorder. The data of a character data node is recorded
// after it by RecordBlinkDomCheckpointNodeCharacterData.
COMPONENT_EXPORT(RECORDER_BRIDGE)
void RecordBlinkDomCheckpointNode(uint64_t checkpoint_sequence,
                                  int document_node_id,
                                  std::string document_token,
                                  int node_index,
                                  int node_id,
                                  int parent_node_id,
                                  int node_type,
                                  std::string node_name);

// Records one attribute of a node already emitted in the same checkpoint. The
// caller truncates the value at its own limit and reports the full length in
// UTF-16 code units so a partial observation is never mistaken for a complete
// one. An empty value is valid and is not the same as an absent attribute.
COMPONENT_EXPORT(RECORDER_BRIDGE)
void RecordBlinkDomCheckpointNodeAttribute(uint64_t checkpoint_sequence,
                                           int document_node_id,
                                           std::string document_token,
                                           int node_id,
                                           int attribute_index,
                                           std::string attribute_namespace,
                                           std::string attribute_name,
                                           std::string attribute_value,
                                           int attribute_value_length,
                                           bool attribute_value_truncated,
                                           int maximum_value_length);

// Records the data of a text, comment, CDATA section, or processing
// instruction node already emitted in the same checkpoint. The caller cuts the
// data at its own limit and reports the full length in UTF-16 code units.
COMPONENT_EXPORT(RECORDER_BRIDGE)
void RecordBlinkDomCheckpointNodeCharacterData(uint64_t checkpoint_sequence,
                                               int document_node_id,
                                               std::string document_token,
                                               int node_id,
                                               std::string data,
                                               int data_length,
                                               bool data_truncated,
                                               int maximum_value_length);

// Records the properties of a shadow root already emitted as a node of the
// same checkpoint. The shadow root's parent in the checkpoint is its host.
// reference_target is meaningful only when reference_target_present is true.
COMPONENT_EXPORT(RECORDER_BRIDGE)
void RecordBlinkDomCheckpointShadowRoot(uint64_t checkpoint_sequence,
                                        int document_node_id,
                                        std::string document_token,
                                        int node_id,
                                        int host_node_id,
                                        std::string mode,
                                        bool delegates_focus,
                                        std::string slot_assignment,
                                        bool clonable,
                                        bool serializable,
                                        bool declarative,
                                        bool available_to_element_internals,
                                        bool reference_target_present,
                                        std::string reference_target);

// Records the nodes currently assigned to a slot element already emitted in
// the same checkpoint, as Blink holds them without recalculating the
// assignment. assignment_current is false when Blink has marked the
// assignment for recalculation, so the recorded list may be stale.
COMPONENT_EXPORT(RECORDER_BRIDGE)
void RecordBlinkDomCheckpointSlotAssignment(uint64_t checkpoint_sequence,
                                            int document_node_id,
                                            std::string document_token,
                                            int node_id,
                                            std::vector<int> assigned_node_ids,
                                            int assigned_node_count,
                                            int maximum_assigned_nodes,
                                            bool assignment_current);

// Completes the checkpoint and explicitly reports whether its node limit or
// its per-node attribute limit was reached before the complete document tree
// and attribute set were emitted.
COMPONENT_EXPORT(RECORDER_BRIDGE)
void CompleteBlinkDomCheckpoint(uint64_t checkpoint_sequence,
                                int document_node_id,
                                std::string document_token,
                                std::string reason,
                                int node_count,
                                bool truncated,
                                int maximum_nodes,
                                int attribute_count,
                                bool attributes_truncated,
                                int maximum_attributes_per_node,
                                int maximum_value_length,
                                int shadow_root_count,
                                int slot_count,
                                int character_data_count);

// Starts one checkpoint for the accessibility updates Chromium is about to
// send from the renderer to the browser process. The shared document token
// correlates this evidence with the committed navigation.
COMPONENT_EXPORT(RECORDER_BRIDGE)
uint64_t BeginRendererAccessibilityCheckpoint(
    std::string document_token,
    std::string reason,
    int maximum_nodes,
    int update_count,
    int event_count);

// Records one serialized AXNodeData item. The role is recorded twice: as the
// numeric ax::mojom::Role value, whose ordinals are not stable across Chromium
// versions, and as Chromium's own stable role token from ui::ToString. The
// serialized properties retain Chromium's readable debug representation, which
// is a diagnostic aid and not a field contract.
COMPONENT_EXPORT(RECORDER_BRIDGE)
void RecordRendererAccessibilityCheckpointNode(
    uint64_t checkpoint_sequence,
    std::string document_token,
    int node_index,
    int accessibility_node_id,
    int parent_accessibility_node_id,
    int dom_node_id,
    int role,
    std::string role_name,
    std::string name,
    std::string description,
    std::string serialized_properties,
    bool focused);

// Completes the renderer serialization checkpoint and reports capture limits
// explicitly so a partial update cannot be mistaken for a complete one.
COMPONENT_EXPORT(RECORDER_BRIDGE)
void CompleteRendererAccessibilityCheckpoint(
    uint64_t checkpoint_sequence,
    std::string document_token,
    std::string reason,
    int node_count,
    bool truncated,
    int maximum_nodes,
    int update_count,
    int event_count);

// Records one accepted attribute mutation. The change type is 0 for an added
// attribute, 1 for a removed attribute, and 2 for a changed attribute, and it
// determines which of the two values is recorded as absent: an added attribute
// has no previous value and a removed attribute has no current value. A
// checkpoint coalesces an unknown number of mutations, so the specific
// attribute that changed is only recoverable from this record.
COMPONENT_EXPORT(RECORDER_BRIDGE)
void RecordBlinkDomAttributeChanged(int document_node_id,
                                    std::string document_token,
                                    int node_id,
                                    std::string node_name,
                                    std::string attribute_namespace,
                                    std::string attribute_name,
                                    int change_type,
                                    std::string attribute_value,
                                    int attribute_value_length,
                                    bool attribute_value_truncated,
                                    std::string previous_attribute_value,
                                    int previous_attribute_value_length,
                                    bool previous_attribute_value_truncated,
                                    int maximum_value_length);

// Records one accepted character-data mutation on a text, comment, or other
// character-data node.
COMPONENT_EXPORT(RECORDER_BRIDGE)
void RecordBlinkDomCharacterDataChanged(int document_node_id,
                                        std::string document_token,
                                        int node_id,
                                        int parent_node_id,
                                        int node_type,
                                        std::string text,
                                        int text_length,
                                        bool text_truncated,
                                        std::string previous_text,
                                        int previous_text_length,
                                        bool previous_text_truncated,
                                        int maximum_value_length);

// Protocol 0.34: the structural changes of a connected DOM tree, recorded as
// DOM transitions in the order Blink makes them, from the time a document's
// finished-parsing checkpoint is recorded.
//
// Records a node inserted into a connected container, or a shadow root
// attached to a connected host, and returns the transition's sequence, which
// names the records of the inserted subtree that follow. The insertion kind
// is "child" or "shadow-root". The previous sibling is the inserted node's
// previous sibling when the record is made, or 0 for none; a shadow root has
// none. Returns 0 when nothing was recorded.
COMPONENT_EXPORT(RECORDER_BRIDGE)
uint64_t RecordBlinkDomNodeInserted(int document_node_id,
                                    std::string document_token,
                                    std::string insertion_kind,
                                    int container_node_id,
                                    int node_id,
                                    int previous_sibling_node_id);

// Records one node of an inserted subtree, in the order a DOM checkpoint
// walks a document, with the same fields as a checkpoint node.
COMPONENT_EXPORT(RECORDER_BRIDGE)
void RecordBlinkDomInsertedNode(uint64_t insertion_sequence,
                                int document_node_id,
                                std::string document_token,
                                int node_index,
                                int node_id,
                                int parent_node_id,
                                int node_type,
                                std::string node_name);

COMPONENT_EXPORT(RECORDER_BRIDGE)
void RecordBlinkDomInsertedNodeAttribute(uint64_t insertion_sequence,
                                         int document_node_id,
                                         std::string document_token,
                                         int node_id,
                                         int attribute_index,
                                         std::string attribute_namespace,
                                         std::string attribute_name,
                                         std::string attribute_value,
                                         int attribute_value_length,
                                         bool attribute_value_truncated,
                                         int maximum_value_length);

COMPONENT_EXPORT(RECORDER_BRIDGE)
void RecordBlinkDomInsertedNodeCharacterData(uint64_t insertion_sequence,
                                             int document_node_id,
                                             std::string document_token,
                                             int node_id,
                                             std::string data,
                                             int data_length,
                                             bool data_truncated,
                                             int maximum_value_length);

COMPONENT_EXPORT(RECORDER_BRIDGE)
void RecordBlinkDomInsertedShadowRoot(uint64_t insertion_sequence,
                                      int document_node_id,
                                      std::string document_token,
                                      int node_id,
                                      int host_node_id,
                                      std::string mode,
                                      bool delegates_focus,
                                      std::string slot_assignment,
                                      bool clonable,
                                      bool serializable,
                                      bool declarative,
                                      bool available_to_element_internals,
                                      bool reference_target_present,
                                      std::string reference_target);

COMPONENT_EXPORT(RECORDER_BRIDGE)
void RecordBlinkDomInsertedSlotAssignment(uint64_t insertion_sequence,
                                          int document_node_id,
                                          std::string document_token,
                                          int node_id,
                                          std::vector<int> assigned_node_ids,
                                          int assigned_node_count,
                                          int maximum_assigned_nodes,
                                          bool assignment_current);

// Completes the records of an inserted subtree with their counts.
COMPONENT_EXPORT(RECORDER_BRIDGE)
void CompleteBlinkDomInsertion(uint64_t insertion_sequence,
                               int document_node_id,
                               std::string document_token,
                               int node_count,
                               int attribute_count,
                               int character_data_count,
                               int shadow_root_count,
                               int slot_count);

// Records a child removed from a connected container.
COMPONENT_EXPORT(RECORDER_BRIDGE)
void RecordBlinkDomNodeRemoved(int document_node_id,
                               std::string document_token,
                               int container_node_id,
                               int node_id);

// Records the removal of every child of a connected container.
COMPONENT_EXPORT(RECORDER_BRIDGE)
void RecordBlinkDomChildrenRemoved(int document_node_id,
                                   std::string document_token,
                                   int container_node_id);

// Records the state of a connected shadow root after its reference target
// changed, with the fields of a checkpoint's shadow root record.
COMPONENT_EXPORT(RECORDER_BRIDGE)
void RecordBlinkDomShadowRootChanged(int document_node_id,
                                     std::string document_token,
                                     int node_id,
                                     int host_node_id,
                                     std::string mode,
                                     bool delegates_focus,
                                     std::string slot_assignment,
                                     bool clonable,
                                     bool serializable,
                                     bool declarative,
                                     bool available_to_element_internals,
                                     bool reference_target_present,
                                     std::string reference_target);

// Records the nodes assigned to a connected slot after Blink recalculated
// the slot assignments of its shadow root.
COMPONENT_EXPORT(RECORDER_BRIDGE)
void RecordBlinkDomSlotAssignmentChanged(int document_node_id,
                                         std::string document_token,
                                         int node_id,
                                         std::vector<int> assigned_node_ids,
                                         int assigned_node_count,
                                         int maximum_assigned_nodes);

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

// Cookie evidence. Every cookie entry point below takes cookie names and
// non-value attributes only; none has a parameter that could carry a cookie
// value, so values are excluded by the shape of the interface rather than by a
// filter applied later. A hook that holds cookie text passes it through one of
// the reading functions first and forwards only what they return.

// Where a renderer cookie call came from and which JavaScript world made it,
// as Blink reported them at the call. The focus, selection, and text-control
// records of the interaction channel report their script origin in the same
// shape. The fields follow the same conventions
// as the trailing location and world parameters of the listener entry points:
// an empty string or a zero identifier, line, or column means not observed, and
// an empty world kind means Blink reported no current world.
struct CookieCallOrigin {
  std::string script_url;
  std::string function_name;
  int script_id = 0;
  int line_number = 0;
  int column_number = 0;
  std::string world_kind;
  int world_id = kExecutionWorldIdUnobserved;
  std::string world_name;
  std::string world_stable_id;
};

// The requested name and attributes of one cookie write, as script wrote them.
// Optional attributes use an empty string with a false presence flag for an
// attribute that was not written.
struct CookieWriteRequest {
  std::string name;
  bool domain_present = false;
  std::string domain;
  bool path_present = false;
  std::string path;
  bool same_site_present = false;
  std::string same_site;
  bool secure = false;
  bool http_only = false;
  bool partitioned = false;
  bool expires_present = false;
  bool max_age_present = false;
  std::vector<std::string> attribute_names;
};

// One cookie in a browser-process cookie access notification. A cookie Chromium
// parsed carries its canonical attributes; a Set-Cookie line Chromium could not
// parse carries only the name read from the line, and parsed is false. The
// inclusion status is Chromium's CookieInclusionStatus debug string, which the
// bridge splits into inclusion, exclusion reasons, warning reasons, and an
// exemption before recording.
struct CookieAccessEntry {
  bool parsed = false;
  std::string name;
  std::string domain;
  std::string path;
  std::string same_site;
  bool secure = false;
  bool http_only = false;
  bool host_only = false;
  bool partitioned = false;
  bool persistent = false;
  bool expired = false;
  std::string inclusion_status;
};

// Names the renderer cookie outcomes a hook can report. These are header
// constants for the same reason the target kinds are.
inline constexpr char kCookieOutcomeReturned[] = "returned";
inline constexpr char kCookieOutcomeSentToCookieManager[] =
    "sent-to-cookie-manager";
inline constexpr char kCookieOutcomeNoCookieUrl[] = "not-attempted-no-cookie-url";
inline constexpr char kCookieOutcomeCookieManagerCallFailed[] =
    "cookie-manager-call-failed";
inline constexpr char kCookieOutcomeCookiesDisabled[] =
    "refused-no-window-or-cookies-disabled";
inline constexpr char kCookieOutcomeSecurityError[] = "refused-security-error";
inline constexpr char kCookieOutcomeThrew[] = "threw";
inline constexpr char kCookieServedFromCookieManager[] = "cookie-manager";
inline constexpr char kCookieServedFromRendererCache[] = "renderer-cache";

// Returns the cookie names in a document.cookie getter string. The string is
// read once and not kept.
COMPONENT_EXPORT(RECORDER_BRIDGE)
std::vector<std::string> ReadCookieNamesFromCookieString(
    std::string_view cookie_string);

// Returns the requested name and attributes of a document.cookie assignment.
// The assignment is read once and its value is not kept.
COMPONENT_EXPORT(RECORDER_BRIDGE)
CookieWriteRequest ReadCookieWriteRequest(std::string_view cookie_line);

// Returns the cookie name of a Set-Cookie line Chromium could not parse. The
// line is read once and its value is not kept.
COMPONENT_EXPORT(RECORDER_BRIDGE)
std::string ReadCookieNameFromSetCookieLine(std::string_view cookie_line);

// Records one document.cookie read. An empty served-from means the read did
// not reach the cookie jar's cache or cookie manager, and the outcome names
// why.
COMPONENT_EXPORT(RECORDER_BRIDGE)
void RecordBlinkDocumentCookieRead(int document_node_id,
                                   std::string document_token,
                                   std::string cookie_url,
                                   std::string outcome,
                                   std::string served_from,
                                   std::vector<std::string> cookie_names,
                                   CookieCallOrigin origin);

// Records one document.cookie assignment. The renderer sends a write to the
// cookie manager without waiting for a reply, so the stored result is reported
// by the browser-process cookie access record rather than here.
COMPONENT_EXPORT(RECORDER_BRIDGE)
void RecordBlinkDocumentCookieWrite(int document_node_id,
                                    std::string document_token,
                                    std::string cookie_url,
                                    std::string outcome,
                                    CookieWriteRequest request,
                                    CookieCallOrigin origin);

// Records a Cookie Store API read call once Blink has either sent it to the
// cookie manager or thrown. A sent call passes the address of its promise
// resolver, which later identifies the matching result; a thrown call passes
// zero. The name and URL are the call's filters, empty when absent.
COMPONENT_EXPORT(RECORDER_BRIDGE)
void RecordBlinkCookieStoreRead(uintptr_t resolver_identity,
                                std::string method,
                                std::string context_kind,
                                int document_node_id,
                                std::string document_token,
                                bool name_present,
                                std::string name,
                                bool url_present,
                                std::string url,
                                std::string outcome,
                                CookieCallOrigin origin);

// Notes the promise resolver of a Cookie Store API write just before Blink
// sends the write to the cookie manager. The write call that follows takes the
// noted resolver on the same thread, so a set and a delete, which share one
// Blink write path, are told apart at the call rather than inferred from their
// attributes.
COMPONENT_EXPORT(RECORDER_BRIDGE)
void NoteBlinkCookieStoreWriteResolver(uintptr_t resolver_identity);

// Records a Cookie Store API write call once Blink has either sent it to the
// cookie manager or thrown.
COMPONENT_EXPORT(RECORDER_BRIDGE)
void RecordBlinkCookieStoreWrite(std::string method,
                                 std::string context_kind,
                                 int document_node_id,
                                 std::string document_token,
                                 bool threw,
                                 CookieWriteRequest request,
                                 CookieCallOrigin origin);

// Records the cookie manager's reply to a Cookie Store API read before Blink
// resolves the promise. A reply that arrives after its script context was
// destroyed is recorded with context_valid false, since Blink then drops it.
COMPONENT_EXPORT(RECORDER_BRIDGE)
void RecordBlinkCookieStoreReadResult(uintptr_t resolver_identity,
                                      bool context_valid,
                                      std::vector<std::string> cookie_names);

// Records the cookie manager's reply to a Cookie Store API write.
COMPONENT_EXPORT(RECORDER_BRIDGE)
void RecordBlinkCookieStoreWriteResult(uintptr_t resolver_identity,
                                       bool success);

// Records one cookie change the cookie manager reported to a CookieStore that
// has change listeners, and whether Blink dispatched a change event for it.
COMPONENT_EXPORT(RECORDER_BRIDGE)
void RecordBlinkCookieStoreChange(std::string context_kind,
                                  int document_node_id,
                                  std::string document_token,
                                  std::string name,
                                  std::string domain,
                                  std::string path,
                                  std::string cause,
                                  bool dispatched);

// Records one cookie access notification the network service sent to the
// browser for a frame's document. A read is cookies attached to a request or
// returned to script; a change is cookies set by a response or by script.
COMPONENT_EXPORT(RECORDER_BRIDGE)
void RecordBrowserFrameCookieAccess(int page_frame_tree_node_id,
                                    int frame_tree_node_id,
                                    int64_t document_navigation_id,
                                    std::string document_token,
                                    int renderer_process_id,
                                    bool change,
                                    std::string url,
                                    std::string frame_origin,
                                    std::string top_frame_origin,
                                    std::string request_id,
                                    bool ad_tagged,
                                    std::vector<CookieAccessEntry> cookies);

// Records one cookie access notification for a navigation request, before the
// navigation has committed a document.
COMPONENT_EXPORT(RECORDER_BRIDGE)
void RecordBrowserNavigationCookieAccess(int64_t navigation_id,
                                         int page_frame_tree_node_id,
                                         int frame_tree_node_id,
                                         bool change,
                                         std::string url,
                                         std::string frame_origin,
                                         std::string top_frame_origin,
                                         std::string request_id,
                                         bool ad_tagged,
                                         std::vector<CookieAccessEntry> cookies);

// Records the outcome of one Document::SetFocusedElement call that could change
// focus, once the call has returned and every blur, focusout, focus, and focusin
// handler it ran has finished. The previous node is the element focused when
// the call started, the requested node is the element the call was asked to
// focus, and the focused node is the element focused when it returned. A zero
// node identifier means no element. The active descendant is the element the
// focused element's aria-activedescendant resolved to when the call returned,
// through either the content attribute or an element set by reflection.
COMPONENT_EXPORT(RECORDER_BRIDGE)
void RecordBlinkFocusChanged(int document_node_id,
                             std::string document_token,
                             int previous_node_id,
                             int requested_node_id,
                             int focused_node_id,
                             int active_descendant_node_id,
                             std::string focus_type,
                             std::string focus_trigger,
                             bool prevent_scroll,
                             bool focus_visible_present,
                             bool focus_visible,
                             CookieCallOrigin origin);

// Records a frame selection Blink committed, after focus has followed it and
// before Blink notifies accessibility, the compositor, and page event handlers.
// The anchor and focus are container nodes and offsets as Blink holds them in
// the DOM tree, which inside a text control are nodes of the control's
// user-agent shadow tree. A zero text-control identifier means the selection
// anchor is not inside a text control, and its offsets and direction are then
// not reported.
COMPONENT_EXPORT(RECORDER_BRIDGE)
void RecordBlinkSelectionChanged(int document_node_id,
                                 std::string document_token,
                                 std::string set_by,
                                 std::string selection_type,
                                 int anchor_node_id,
                                 int anchor_offset,
                                 int focus_node_id,
                                 int focus_offset,
                                 bool directional,
                                 int text_control_node_id,
                                 int text_control_selection_start,
                                 int text_control_selection_end,
                                 std::string text_control_selection_direction,
                                 CookieCallOrigin origin);

// Records the value of a text control after a value set or a user edit changed
// it. The value is bounded by the caller, which reports the full length in
// UTF-16 code units and whether the recorded value was cut. The selection
// offsets and direction are the control's own, read when the record is made.
COMPONENT_EXPORT(RECORDER_BRIDGE)
void RecordBlinkTextControlValueChanged(int document_node_id,
                                        std::string document_token,
                                        int node_id,
                                        std::string control_type,
                                        std::string source,
                                        std::string value,
                                        int value_length,
                                        bool value_truncated,
                                        int maximum_value_length,
                                        int selection_start,
                                        int selection_end,
                                        std::string selection_direction,
                                        CookieCallOrigin origin);

// Records an element Blink stored as an element's aria-activedescendant
// through element reflection. Reflection writes an empty content attribute and
// keeps the element itself outside the DOM attribute state, so the referenced
// element is only observable here.
COMPONENT_EXPORT(RECORDER_BRIDGE)
void RecordBlinkActiveDescendantReferenceSet(int document_node_id,
                                             std::string document_token,
                                             int node_id,
                                             int referenced_node_id,
                                             CookieCallOrigin origin);

// A rectangle in screen DIPs, or in a frame's local root, as Blink's
// gfx::Rect holds it.
struct PagePopupRect {
  int x = 0;
  int y = 0;
  int width = 0;
  int height = 0;
};

// Records a page popup, such as the list of an open select, once its
// document is installed. The document named is the popup's own; the owner is
// the element that opened it, in the owner document, whose frame's token is
// the owner frame token (protocol 0.44). The kind is
// "select-list", "date-time", "color", or "other". The rectangles are those
// WebPagePopupImpl computes: the owner's visible bounds in its local root, the
// owner's local root view and the anchor in screen DIPs, and the popup's first
// window rectangle.
COMPONENT_EXPORT(RECORDER_BRIDGE)
void RecordBlinkPagePopupOpened(int document_node_id,
                                std::string document_token,
                                std::string kind,
                                int owner_document_node_id,
                                std::string owner_document_token,
                                std::string owner_frame_token,
                                int owner_node_id,
                                PagePopupRect owner_visible_bounds_in_local_root,
                                PagePopupRect owner_local_root_rect_in_screen,
                                PagePopupRect anchor_rect_in_screen,
                                PagePopupRect initial_window_rect,
                                double zoom_factor);

// Records a window rectangle a page popup asked for, after the emulation is
// reversed, deferred when it was asked for before the popup was shown. Where
// the browser put the window is recorded by the browser's popup widget
// records (protocol 0.44).
COMPONENT_EXPORT(RECORDER_BRIDGE)
void RecordBlinkPagePopupWindowRect(int document_node_id,
                                    std::string document_token,
                                    bool deferred,
                                    PagePopupRect window_rect);

// Records a page popup closing, by "renderer" or "browser".
COMPONENT_EXPORT(RECORDER_BRIDGE)
void RecordBlinkPagePopupClosed(int document_node_id,
                                std::string document_token,
                                std::string closed_by);

// The browser's records of a renderer's popup widget (protocol 0.44). A popup
// widget is named by its frame sink, which the browser makes from the routing
// ID it allocates and the renderer never receives.
struct PopupWidgetSink {
  uint32_t client_id = 0;
  uint32_t sink_id = 0;
};

// Records a popup widget made for a frame's request, after
// RenderFrameHostImpl::CreateNewPopupWidget makes it. The context names the
// opener frame's document; the opener frame token is the renderer's local
// frame token of the frame that asked.
COMPONENT_EXPORT(RECORDER_BRIDGE)
void RecordBrowserPopupWidgetCreated(int page_frame_tree_node_id,
                                     int frame_tree_node_id,
                                     int64_t document_navigation_id,
                                     std::string document_token,
                                     int renderer_process_id,
                                     std::string opener_frame_token,
                                     PopupWidgetSink sink);

// Records WebContentsImpl::ShowCreatedWidget for a popup widget: the
// rectangle and anchor as received, the rectangle after the transform for
// nested web contents and after ConstrainPopupBounds, and the outcome,
// "shown" with the view's bounds after InitAsPopup, or the reason the popup
// was refused: "window-not-active", "not-visible", or
// "permission-exclusion". A rectangle not reached before a refusal is
// absent.
struct PopupWidgetShown {
  PopupWidgetSink sink;
  std::string outcome;
  PagePopupRect received_rect;
  PagePopupRect received_anchor_rect;
  bool has_transformed = false;
  PagePopupRect transformed_rect;
  PagePopupRect transformed_anchor_rect;
  bool has_constrained = false;
  PagePopupRect constrained_rect;
  bool has_view_bounds = false;
  PagePopupRect view_bounds;
};
COMPONENT_EXPORT(RECORDER_BRIDGE)
void RecordBrowserPopupWidgetShown(PopupWidgetShown shown);

// Records RenderWidgetHostImpl::SetPopupBounds: the rectangle the renderer
// asked for, and the rectangle set on the view after ConstrainPopupBounds and
// the display clamp, or none when the request was ignored.
COMPONENT_EXPORT(RECORDER_BRIDGE)
void RecordBrowserPopupWidgetBoundsRequested(PopupWidgetSink sink,
                                             PagePopupRect requested_rect,
                                             bool has_set_rect,
                                             PagePopupRect set_rect);

// Records the screen rectangles RenderWidgetHostImpl::SendScreenRects sends to
// a popup widget: the view and window bounds in screen DIPs and the view's
// device scale factor. The native window is the popup's HWND, or zero for
// none; the bridge reads its window rectangle and client area in screen
// pixels from Windows when the record is made.
COMPONENT_EXPORT(RECORDER_BRIDGE)
void RecordBrowserPopupWidgetScreenRects(PopupWidgetSink sink,
                                         PagePopupRect view_rect,
                                         PagePopupRect window_rect,
                                         uintptr_t native_window,
                                         double device_scale_factor);

// Records a popup widget's view hiding its window (protocol 0.45):
// RenderWidgetHostViewAura::Hide, cause "hidden", or the view's clean-up
// before it is destroyed, cause "destroyed", each just after the view's
// window was hidden and only when it had been shown. The native window is
// the popup's HWND, or zero for none; the bridge asks Windows whether it is
// still visible when the record is made.
COMPONENT_EXPORT(RECORDER_BRIDGE)
void RecordBrowserPopupWidgetHidden(PopupWidgetSink sink,
                                    std::string cause,
                                    uintptr_t native_window);

// Records a change of an option's selectedness, which sets no attribute. The
// select node is zero for an option with no owner select.
COMPONENT_EXPORT(RECORDER_BRIDGE)
void RecordBlinkOptionSelectednessChanged(int document_node_id,
                                          std::string document_token,
                                          int node_id,
                                          int select_node_id,
                                          bool selected,
                                          CookieCallOrigin origin);

// The document-level interaction state read for one interaction checkpoint.
// Node identifiers are Blink DOM node ids, and zero means no node. The focus
// type is Blink's record of how focus last moved in the document, named as in
// RecordBlinkFocusChanged. The selection is the frame selection in the DOM
// tree, named as in RecordBlinkSelectionChanged, and its positions are
// reported only when the selection type is not "none".
struct InteractionCheckpointState {
  bool document_has_focus = false;
  int focused_node_id = 0;
  bool focus_visible = false;
  int active_descendant_node_id = 0;
  std::string last_focus_type;
  std::string selection_type;
  int anchor_node_id = 0;
  int anchor_offset = 0;
  int focus_node_id = 0;
  int focus_offset = 0;
  bool directional = false;
};

// Starts one snapshot of a document's interaction state, taken immediately
// after the named DOM or layout checkpoint completed. The source channel is
// "browser.dom" or "browser.layout", and the reason is the source checkpoint's
// reason. From protocol 0.35 the source may also be a mutation delivery that
// was not walked, with sequence zero and the reason "post-mutation", or a
// layout change set, with the sequence RecordBlinkLayoutChanges returned. Returns zero when the snapshot is not recorded, in which case no
// text-control or completion record may follow.
COMPONENT_EXPORT(RECORDER_BRIDGE)
uint64_t BeginBlinkInteractionCheckpoint(int document_node_id,
                                         std::string document_token,
                                         std::string source_channel,
                                         uint64_t source_checkpoint_sequence,
                                         std::string reason,
                                         InteractionCheckpointState state,
                                         int maximum_text_controls,
                                         int maximum_value_length);

// Records one text control of a started interaction checkpoint in
// composed-tree order. The value is bounded by the caller, which reports the
// full length in UTF-16 code units and whether the recorded value was cut.
COMPONENT_EXPORT(RECORDER_BRIDGE)
void RecordBlinkInteractionCheckpointTextControl(
    uint64_t checkpoint_sequence,
    int document_node_id,
    std::string document_token,
    int text_control_index,
    int node_id,
    std::string control_type,
    std::string value,
    int value_length,
    bool value_truncated,
    int selection_start,
    int selection_end,
    std::string selection_direction);

// Completes a started interaction checkpoint.
COMPONENT_EXPORT(RECORDER_BRIDGE)
void CompleteBlinkInteractionCheckpoint(uint64_t checkpoint_sequence,
                                        int document_node_id,
                                        std::string document_token,
                                        int text_control_count,
                                        bool truncated,
                                        int maximum_text_controls);

// Frame geometry read at a layout checkpoint. Viewport and scroll values are
// in CSS pixels, the units of getBoundingClientRect, innerWidth, and scrollX.
// The device pixel ratio is the value window.devicePixelRatio reports, and
// the layout zoom factor is the browser zoom Blink applies to CSS pixels.
struct LayoutCheckpointFrame {
  double viewport_width = 0;
  double viewport_height = 0;
  double scroll_x = 0;
  double scroll_y = 0;
  double device_pixel_ratio = 0;
  double layout_zoom_factor = 0;
};

// LayoutCheckpointStyleValue and LayoutCheckpointNode are declared in
// layout_changes.h.

// Starts one layout and computed-style checkpoint for a document whose
// rendering update reached the paint-clean state. The two counters are Blink's
// cumulative style resolution count for the document and layout count for its
// frame view. Returns zero when the recorder is not connected or when neither
// counter has changed since the previous update of the same document, so an
// unchanged rendering update produces no evidence. From protocol 0.35 it also
// returns zero when the document is not walked at this update, which is
// then recorded by its change set: a document is walked at its first update,
// after a layout record was lost, and at the recording's check interval.
COMPONENT_EXPORT(RECORDER_BRIDGE)
uint64_t BeginBlinkLayoutCheckpoint(
    int document_node_id,
    std::string document_token,
    unsigned style_resolution_count,
    unsigned layout_count,
    LayoutCheckpointFrame frame,
    const std::vector<std::string>& style_properties,
    int maximum_nodes);

// Records one element or text node of a started layout checkpoint in preorder.
COMPONENT_EXPORT(RECORDER_BRIDGE)
void RecordBlinkLayoutCheckpointNode(uint64_t checkpoint_sequence,
                                     int document_node_id,
                                     std::string document_token,
                                     LayoutCheckpointNode node);

// Where a layout checkpoint's traversal spent its time, measured by the
// traversal itself. Each time is a sum over the checkpoint's nodes.
struct LayoutCheckpointCost {
  // Node identity, type and name, display lock, and containing shadow root.
  int64_t node_fields_nanoseconds = 0;
  // The rectangle.
  int64_t geometry_nanoseconds = 0;
  // Reading and serializing every listed computed-style value.
  int64_t style_values_nanoseconds = 0;
  // The part of style_values_nanoseconds spent on values Blink reports as
  // depending on layout for that element.
  int64_t layout_dependent_values_nanoseconds = 0;
  // Reading the text laid out inside pseudo-elements.
  int64_t generated_text_nanoseconds = 0;
  // Finding each element's pseudo-elements.
  int64_t pseudo_element_search_nanoseconds = 0;
  // Handing node records to the bridge.
  int64_t record_nanoseconds = 0;
  // Elements whose styles were read, the values recorded, and how many of
  // those depend on layout.
  int64_t styled_nodes = 0;
  int64_t style_values = 0;
  int64_t layout_dependent_values = 0;
  // Reuse of computed-style readings between checkpoints: the time spent
  // finding and keeping readings; the styled elements also styled in the
  // document's previous checkpoint; those that kept the same style object;
  // the values copied from the previous reading instead of being read; and,
  // in checkpoints that read every value to verify the reuse, those
  // checkpoints, the values that would have been copied, and how many of
  // those differed from the value read.
  int64_t style_cache_nanoseconds = 0;
  int64_t previously_styled_nodes = 0;
  int64_t same_style_objects = 0;
  int64_t reused_style_values = 0;
  int64_t verification_checkpoints = 0;
  int64_t verified_style_values = 0;
  int64_t verified_style_values_differed = 0;
};

// Adds one layout checkpoint's measured traversal cost to the bridge's cost
// report. Nothing is recorded as evidence.
COMPONENT_EXPORT(RECORDER_BRIDGE)
void RecordBlinkLayoutCheckpointCost(const LayoutCheckpointCost& cost);

// Notes, in the diagnostic log, a computed-style value that a verifying
// checkpoint read differently from the reading reuse would have copied,
// although the element kept the same style object and the value does not
// depend on layout. Only the property name is written, and at most a fixed
// number of lines per process. Nothing is recorded as evidence.
COMPONENT_EXPORT(RECORDER_BRIDGE)
void RecordBlinkLayoutStyleReuseDifference(const std::string& property_name);

// Completes the layout checkpoint and reports whether the node limit was
// reached before every element and laid-out text node was recorded.
COMPONENT_EXPORT(RECORDER_BRIDGE)
void CompleteBlinkLayoutCheckpoint(uint64_t checkpoint_sequence,
                                   int document_node_id,
                                   std::string document_token,
                                   int node_count,
                                   bool truncated,
                                   int maximum_nodes,
                                   int pseudo_element_count,
                                   int shadow_root_count);

// Records one layout change set of a document whose rendering update reached
// the paint-clean state: the transform nodes and the noted nodes whose record
// differs from the last one recorded for them in this renderer process, the
// scroll offsets stored during the update (protocol 0.34), and the counts.
// The set names the layout checkpoint recorded for the document since the
// previous call for it, if any. Nothing is recorded when no record differs,
// unless the update recalculated the document's style or layout and was not
// walked in full (protocol 0.35), and nothing when the recorder is not
// connected.
//
// Returns the change set as a presentation and interaction source, marked by
// kLayoutChangeSetSourceBit, when the update recorded no layout checkpoint;
// the caller then requests the update's presentation and interaction
// snapshot with it. Returns zero otherwise.
COMPONENT_EXPORT(RECORDER_BRIDGE)
uint64_t RecordBlinkLayoutChanges(
    int document_node_id,
    std::string document_token,
    LayoutChangesFrame frame,
    int noted_node_count,
    std::vector<LayoutTransformNode> transform_nodes,
    std::vector<LayoutChangedNode> nodes,
    std::vector<LayoutScrollOffset> scroll_offsets);

// The local-root widget a presentation request was queued on. The frame sink
// is the viz::FrameSinkId whose compositor frames the frame tokens number, and
// the frame token is the LocalFrameToken of the widget's local root. A request
// made without a widget carries no identity. A page popup's widget is not
// told its frame sink, so its identity names only its frame.
struct PresentationWidgetIdentity {
  bool present = false;
  bool page_popup = false;
  uint32_t frame_sink_client_id = 0;
  uint32_t frame_sink_id = 0;
  std::string local_root_frame_token;
};

// Chromium's timing for one presented compositor frame, as microseconds from
// the base::TimeTicks origin. Zero means Chromium reported a null time. The
// flags are gfx::PresentationFeedback::Flags.
struct PresentationFeedbackTiming {
  int64_t presented_microseconds = 0;
  int64_t interval_microseconds = 0;
  uint32_t flags = 0;
  int64_t received_compositor_frame_microseconds = 0;
  int64_t draw_start_microseconds = 0;
  int64_t swap_start_microseconds = 0;
  int64_t swap_end_microseconds = 0;
};

// Records a request for the presentation of the compositor frame that carries
// the named layout checkpoint's rendering update, or, when the sequence is
// marked by kLayoutChangeSetSourceBit, the named layout change set's
// (protocol 0.35). An empty not-queued reason
// means the caller queues a swap promise when a nonzero sequence is returned;
// otherwise it is "no-widget" or "not-compositing" and nothing is queued.
// The source frame number is LayerTreeHost::SourceFrameNumber(), or -1 when
// there is no layer tree host. Returns zero when the request is not recorded.
COMPONENT_EXPORT(RECORDER_BRIDGE)
uint64_t BeginBlinkPresentationRequest(int document_node_id,
                                       std::string document_token,
                                       uint64_t layout_checkpoint_sequence,
                                       PresentationWidgetIdentity widget,
                                       std::string not_queued_reason,
                                       int source_frame_number,
                                       bool is_main_frame_widget,
                                       bool high_resolution_ticks);

// Records one cc::SwapPromise::DidNotSwap call for a queued request. The
// reason is "swap-fails", "commit-fails", "commit-no-update", or
// "activation-fails"; a kept-active promise waits for a later frame, and a
// broken one ends the request. The index counts calls from zero, and the
// timestamp is Chromium's, in TimeTicks microseconds, or zero when null.
COMPONENT_EXPORT(RECORDER_BRIDGE)
void RecordBlinkPresentationNotSwapped(uint64_t request_sequence,
                                       int document_node_id,
                                       std::string document_token,
                                       PresentationWidgetIdentity widget,
                                       std::string reason,
                                       bool kept_active,
                                       int not_swapped_index,
                                       int64_t timestamp_microseconds,
                                       bool high_resolution_ticks);

// Records that the compositor frame carrying a queued request was submitted,
// from cc::SwapPromise::DidSwap, so the record's own timestamp is the swap.
COMPONENT_EXPORT(RECORDER_BRIDGE)
void RecordBlinkPresentationSwapped(uint64_t request_sequence,
                                    int document_node_id,
                                    std::string document_token,
                                    PresentationWidgetIdentity widget,
                                    uint32_t frame_token,
                                    int not_swapped_count);

// Records viz's presentation feedback for the frame carrying a request.
COMPONENT_EXPORT(RECORDER_BRIDGE)
void RecordBlinkPresentationFeedback(uint64_t request_sequence,
                                     int document_node_id,
                                     std::string document_token,
                                     PresentationWidgetIdentity widget,
                                     uint32_t frame_token,
                                     PresentationFeedbackTiming timing,
                                     int not_swapped_count,
                                     bool high_resolution_ticks);

// Protocol 0.48 (slice 4b, "The frame's moment, held"): the compositor's
// drawn values, on the browser.compositor channel. A compositor is named by
// its cc::LayerTreeHost ID, which its LayerTreeHostImpl shares, and by the
// widget its Blink presentation requests name.

// One cc::FilterOperation as the compositor holds it: its type, and its
// numbers in the order "What is recorded" in the bridge README gives.
struct CompositorFilterOperation {
  std::string type;
  std::vector<double> numbers;
};

// One property of one element of the active tree as drawn. The property is
// "transform" (numbers: the 16 matrix entries, row by row), "opacity" (one
// number), "filter" or "backdrop-filter" (the operations), or
// "scroll-offset" (x and y, and from protocol 0.50 1 or 0 for whether the
// compositor scrolls the node and a bitmask of its main thread repaint
// reasons), or "background-color-progress" or
// "clip-path-progress" (the compositor's progress a native paint worklet's
// drawn record was painted with, one number, or none when it was painted
// with no compositor progress), or "image-frame" (protocol 0.48 part 1c:
// the frame index of an animated paint image on the active tree, one number,
// with element_id holding the PaintImage::Id). An element whose node or paint
// worklet, or an image the image animation controller no longer holds, is
// given with present false.
struct CompositorDrawnValue {
  uint64_t element_id = 0;
  std::string property;
  bool present = true;
  std::vector<double> numbers;
  std::vector<CompositorFilterOperation> filters;
};

// One cc::KeyframeModel of an animation started on the compositor: its ID,
// its cc::TargetProperty, and the compositor element it animates, with the
// element ID's Blink namespace.
struct CompositorKeyframeModelFacts {
  int keyframe_model_id = 0;
  std::string target_property;
  uint64_t element_id = 0;
  std::string element_id_namespace;
};

// Names, on the main thread, the widget whose compositor has this ID, so
// that its compositor frames name the widget its presentation records name.
COMPONENT_EXPORT(RECORDER_BRIDGE)
void RegisterCompositorWidget(int layer_tree_host_id,
                              PresentationWidgetIdentity widget);

// Records, on Blink's main thread, an animation started on the compositor
// (CompositorAnimations::StartAnimationOnCompositor).
COMPONENT_EXPORT(RECORDER_BRIDGE)
void RecordCompositorAnimationStarted(
    int document_node_id,
    std::string document_token,
    int node_id,
    int compositor_animation_id,
    std::vector<CompositorKeyframeModelFacts> keyframe_models);

// Records, on Blink's main thread, that an animation's keyframe models were
// removed from the compositor (KeyframeEffect::CancelAnimationOnCompositor),
// which is how both a cancelled and a finished animation leave it.
COMPONENT_EXPORT(RECORDER_BRIDGE)
void RecordCompositorAnimationEnded(int document_node_id,
                                    std::string document_token,
                                    int node_id,
                                    int compositor_animation_id,
                                    std::vector<int> keyframe_model_ids);

// One Blink animation as Animation::NotifyProbe sees it (protocol 0.53,
// slice 4g): a CSS animation, a CSS transition, or a Web Animation, with its
// target, play state, times on its timeline, and its effect's timing, as
// DevTools' Animations panel describes it. Times are milliseconds; an
// unresolved time is absent. The timeline's zero time is TimeTicks
// microseconds, for a document timeline only.
struct AnimationFacts {
  int document_node_id = 0;
  std::string document_token;
  unsigned sequence_number = 0;
  // "css-animation", "css-transition", or "web-animation".
  std::string kind;
  // The animation's id, else its animation-name, else the transitioned
  // property, as DevTools names it; empty when it has none.
  std::string name;
  std::string id;
  int target_node_id = 0;
  std::string pseudo_element;
  // The play state, "idle", "running", "paused", or "finished", and whether
  // a play or pause is pending.
  std::string play_state;
  bool pending = false;
  double playback_rate = 1;
  std::optional<double> start_time_milliseconds;
  std::optional<double> current_time_milliseconds;
  // "document", "scroll", "view", "other", or "none".
  std::string timeline_kind;
  int64_t timeline_zero_microseconds = 0;
  bool high_resolution_ticks = false;
  double timeline_playback_rate = 1;
  int timeline_source_node_id = 0;
  int timeline_subject_node_id = 0;
  std::string timeline_axis;
  bool has_effect = false;
  double delay_milliseconds = 0;
  double end_delay_milliseconds = 0;
  double iteration_start = 0;
  // Absent when infinite.
  std::optional<double> iterations;
  double duration_milliseconds = 0;
  std::string direction;
  std::string fill;
  std::string easing;
  // Blink's computed progress, after the easing, and current iteration, at
  // the call; absent when not in effect.
  std::optional<double> progress;
  std::optional<double> current_iteration;
  int compositor_animation_id = 0;
};

// Records, on Blink's main thread, from Animation::NotifyProbe, an
// animation's first call and each later call in which anything other than
// its current time, progress, and current iteration changed, as
// animation-updated on browser.animation (protocol 0.53).
COMPONENT_EXPORT(RECORDER_BRIDGE)
void RecordAnimationUpdated(AnimationFacts facts);

// Records, on Blink's main thread, that an animation recorded before was
// released (Animation::Dispose) or its document's context was destroyed
// (Animation::ContextDestroyed), as animation-removed.
COMPONENT_EXPORT(RECORDER_BRIDGE)
void RecordAnimationRemoved(unsigned sequence_number);

// Records, on the compositor thread in LayerTreeHostImpl::DrawLayers, the
// frame about to be submitted: the values given are those of the active
// tree as drawn, and only those changed since this compositor's last
// recorded frame are written. A frame with no change is not recorded. The
// begin frame time is TimeTicks microseconds.
COMPONENT_EXPORT(RECORDER_BRIDGE)
void RecordCompositorFrame(int layer_tree_host_id,
                           uint32_t frame_token,
                           int64_t begin_frame_microseconds,
                           int source_frame_number,
                           bool high_resolution_ticks,
                           std::vector<CompositorDrawnValue> values);

// Records viz's presentation of a recorded compositor frame, from
// LayerTreeHostImpl::DidPresentCompositorFrame. A frame not recorded is
// ignored.
COMPONENT_EXPORT(RECORDER_BRIDGE)
void RecordCompositorFramePresented(int layer_tree_host_id,
                                    uint32_t frame_token,
                                    int64_t presented_microseconds,
                                    bool failed,
                                    bool high_resolution_ticks);

// What a native paint worklet painted, on the worklet's thread
// (BackgroundColorPaintDefinition::Paint or ClipPathPaintDefinition::Paint):
// the compositor element and property, the compositor progress it was
// given, if any, and the value it drew. A background color is its four
// floats (SkColor4f); a clip path is the path as Skia holds it, its fill
// type, verbs, points (x, y, ...), and conic weights, with the translation
// the paint applied and whether it was drawn as a rounded rectangle.
struct PaintWorkletPaintedFacts {
  uint64_t element_id = 0;
  std::string property;
  std::optional<double> progress;
  std::vector<double> color;
  std::string fill_type;
  std::vector<std::string> verbs;
  std::vector<double> points;
  std::vector<double> conic_weights;
  double translate_x = 0;
  double translate_y = 0;
  bool drawn_as_rounded_rect = false;
};

COMPONENT_EXPORT(RECORDER_BRIDGE)
void RecordPaintWorkletPainted(PaintWorkletPaintedFacts facts);

// Network metadata. Every record on the browser.network channel carries
// request and response metadata only: no request body, no response body, no
// cookie value, and no value of a header that carries a credential. Header
// names are always recorded; header values pass through network_text.h, which
// withholds a value by header, by name, or by shape. Enumerated Chromium values
// arrive as the names the hooks give them, so this bridge records them as
// given.

// One HTTP header as Chromium holds it at the hooked point.
struct NetworkHeader {
  std::string name;
  std::string value;
};

// Every header is recorded. The bound is the largest count the protocol's
// 32-bit counts hold; a list is cut and marked, with its full length
// recorded, only where the length cannot be stated.
inline constexpr size_t kMaximumNetworkHeadersPerRecord = 2147483647;

// Marks a time Chromium did not record.
inline constexpr int64_t kNetworkTimeUnobserved =
    std::numeric_limits<int64_t>::min();

// Blink's resource load timing for one response. The request start is given as
// microseconds before the record was made; every phase is microseconds after
// the request start, or kNetworkTimeUnobserved.
struct NetworkLoadTiming {
  bool present = false;
  int64_t request_start_before_record = kNetworkTimeUnobserved;
  int64_t proxy_start = kNetworkTimeUnobserved;
  int64_t proxy_end = kNetworkTimeUnobserved;
  int64_t domain_lookup_start = kNetworkTimeUnobserved;
  int64_t domain_lookup_end = kNetworkTimeUnobserved;
  int64_t connect_start = kNetworkTimeUnobserved;
  int64_t connect_end = kNetworkTimeUnobserved;
  int64_t ssl_start = kNetworkTimeUnobserved;
  int64_t ssl_end = kNetworkTimeUnobserved;
  int64_t worker_start = kNetworkTimeUnobserved;
  int64_t worker_ready = kNetworkTimeUnobserved;
  int64_t worker_fetch_start = kNetworkTimeUnobserved;
  int64_t worker_respond_with_settled = kNetworkTimeUnobserved;
  int64_t worker_router_evaluation_start = kNetworkTimeUnobserved;
  int64_t worker_cache_lookup_start = kNetworkTimeUnobserved;
  int64_t send_start = kNetworkTimeUnobserved;
  int64_t send_end = kNetworkTimeUnobserved;
  int64_t receive_headers_start = kNetworkTimeUnobserved;
  int64_t receive_headers_end = kNetworkTimeUnobserved;
  int64_t receive_non_informational_headers_start = kNetworkTimeUnobserved;
  int64_t receive_early_hints_start = kNetworkTimeUnobserved;
  int64_t push_start = kNetworkTimeUnobserved;
  int64_t push_end = kNetworkTimeUnobserved;
  int64_t response_end = kNetworkTimeUnobserved;
};

// The script context a renderer network record came from. A frame's loads
// carry its document; a worker's loads carry the worker's DevTools token and
// global object URL and no document.
struct NetworkScope {
  std::string context_kind;
  int document_node_id = 0;
  std::string document_token;
  std::string worker_token;
  std::string global_object_url;
};

// One request as Blink holds it when it is about to be sent.
struct NetworkRequestFacts {
  uint64_t inspector_id = 0;
  std::string request_id;
  std::string url;
  std::string method;
  std::string resource_type;
  std::string initiator_type;
  std::string initiator_url;
  int initiator_line = 0;
  int initiator_column = 0;
  bool link_preload = false;
  bool internal = false;
  std::string destination;
  std::string mode;
  std::string credentials_mode;
  std::string redirect_mode;
  std::string cache_mode;
  std::string priority;
  std::string initial_priority;
  std::string fetch_priority_hint;
  std::string render_blocking;
  std::string referrer;
  std::string referrer_policy;
  bool keepalive = false;
  bool user_gesture = false;
  bool ad_resource = false;
  bool form_submission = false;
  std::vector<NetworkHeader> headers;
};

// One response as Blink holds it.
struct NetworkResponseFacts {
  std::string url;
  std::string response_url;
  int status_code = 0;
  std::string status_text;
  std::string mime_type;
  std::string charset;
  std::string alpn_protocol;
  std::string connection_info;
  std::string remote_ip;
  int remote_port = 0;
  uint32_t connection_id = 0;
  bool connection_reused = false;
  bool was_cached = false;
  bool fetched_via_service_worker = false;
  std::string service_worker_response_source;
  bool in_prefetch_cache = false;
  bool network_accessed = false;
  bool from_archive = false;
  bool cookie_in_request = false;
  std::string response_type;
  int64_t encoded_data_length = 0;
  int64_t expected_content_length = -1;
  std::vector<NetworkHeader> headers;
  NetworkLoadTiming timing;
};

// One load failure as Blink reports it.
struct NetworkFailureFacts {
  std::string url;
  int net_error = 0;
  std::string net_error_name;
  bool cancellation = false;
  bool timeout = false;
  bool access_check = false;
  bool blocked_by_response = false;
  bool blocked_by_orb = false;
  bool has_copy_in_cache = false;
  bool cancelled_from_http_error = false;
  bool internal = false;
  std::string blocked_reason;
  std::string cors_error;
  std::string cors_failed_parameter;
};

// Browser navigation timing, as microseconds after the navigation start. The
// navigation start is given as microseconds before the record was made.
struct NavigationResponseTiming {
  bool present = false;
  int64_t navigation_start_before_record = kNetworkTimeUnobserved;
  int64_t loader_start = kNetworkTimeUnobserved;
  int64_t first_request_start = kNetworkTimeUnobserved;
  int64_t first_response_start = kNetworkTimeUnobserved;
  int64_t first_loader_callback = kNetworkTimeUnobserved;
  int64_t final_request_start = kNetworkTimeUnobserved;
  int64_t final_response_start = kNetworkTimeUnobserved;
  int64_t final_non_informational_response_start = kNetworkTimeUnobserved;
  int64_t final_loader_callback = kNetworkTimeUnobserved;
  int64_t request_failed = kNetworkTimeUnobserved;
  int64_t commit_sent = kNetworkTimeUnobserved;
  int64_t commit_received = kNetworkTimeUnobserved;
  int64_t commit_reply_sent = kNetworkTimeUnobserved;
  int64_t did_commit = kNetworkTimeUnobserved;
  int64_t final_request_domain_lookup_start = kNetworkTimeUnobserved;
  int64_t final_request_domain_lookup_end = kNetworkTimeUnobserved;
  int64_t final_request_connect_start = kNetworkTimeUnobserved;
  int64_t final_request_connect_end = kNetworkTimeUnobserved;
  int64_t final_request_ssl_start = kNetworkTimeUnobserved;
};

// One finished navigation's request and response metadata as the browser
// holds it when WebContentsImpl reports the navigation finished.
struct NavigationResponseFacts {
  std::string request_id;
  std::string url;
  std::string method;
  bool committed = false;
  bool error_page = false;
  bool same_document = false;
  bool download = false;
  bool back_forward_cache = false;
  int net_error = 0;
  std::string net_error_name;
  bool response_present = false;
  int status_code = 0;
  std::string status_text;
  std::string mime_type;
  bool was_cached = false;
  std::string remote_ip;
  int remote_port = 0;
  std::string connection_info;
  std::vector<std::string> redirect_chain;
  std::vector<NetworkHeader> request_headers;
  std::vector<NetworkHeader> response_headers;
  NavigationResponseTiming timing;
};

// Reports whether this process has a recorder connection. Hooks that change
// Chromium behaviour for the recorder, rather than only reading it, test this
// first so that a browser started without the recorder behaves as stock.
COMPONENT_EXPORT(RECORDER_BRIDGE)
bool IsRecorderActive();

// Records a request Blink is about to send: the initial request, or the next
// request of a redirect, which carries the redirect response.
COMPONENT_EXPORT(RECORDER_BRIDGE)
void RecordBlinkNetworkRequest(NetworkScope scope,
                               NetworkRequestFacts request,
                               bool redirect,
                               NetworkResponseFacts redirect_response,
                               CookieCallOrigin origin);

// Records a response Blink received for a request, including one Blink served
// from its memory cache while DevTools callbacks were enabled.
COMPONENT_EXPORT(RECORDER_BRIDGE)
void RecordBlinkNetworkResponse(NetworkScope scope,
                                uint64_t inspector_id,
                                std::string request_id,
                                bool from_memory_cache,
                                NetworkResponseFacts response);

// Records that Blink finished a load. The finish time is microseconds before
// the record was made, or kNetworkTimeUnobserved.
COMPONENT_EXPORT(RECORDER_BRIDGE)
void RecordBlinkNetworkFinished(NetworkScope scope,
                                uint64_t inspector_id,
                                int64_t encoded_data_length,
                                int64_t decoded_body_length,
                                int64_t finish_before_record);

// Records that Blink failed or cancelled a load.
COMPONENT_EXPORT(RECORDER_BRIDGE)
void RecordBlinkNetworkFailed(NetworkScope scope,
                              uint64_t inspector_id,
                              NetworkFailureFacts failure);

// Records one use of a resource Blink already held in its memory cache. No
// request leaves the renderer for such a use.
COMPONENT_EXPORT(RECORDER_BRIDGE)
void RecordBlinkMemoryCacheUse(NetworkScope scope,
                               bool static_data,
                               NetworkRequestFacts request,
                               NetworkResponseFacts response);

// Records the request headers the network service reported sending, with the
// cookies it attached or excluded. A frame record has frame tree node
// identifiers; a worker record has negative ones and the DevTools agent id.
COMPONENT_EXPORT(RECORDER_BRIDGE)
void RecordBrowserNetworkRequestHeaders(int page_frame_tree_node_id,
                                        int frame_tree_node_id,
                                        std::string devtools_agent_id,
                                        std::string request_id,
                                        int64_t sent_before_record,
                                        std::vector<NetworkHeader> headers,
                                        std::vector<CookieAccessEntry> cookies);

// Records the response headers the network service reported receiving, with
// the cookies the response set or tried to set.
COMPONENT_EXPORT(RECORDER_BRIDGE)
void RecordBrowserNetworkResponseHeaders(
    int page_frame_tree_node_id,
    int frame_tree_node_id,
    std::string devtools_agent_id,
    std::string request_id,
    int status_code,
    std::vector<NetworkHeader> headers,
    std::vector<CookieAccessEntry> cookies);

// Records the request and response metadata of a finished navigation.
COMPONENT_EXPORT(RECORDER_BRIDGE)
void RecordBrowserNavigationResponse(int64_t navigation_id,
                                     int page_frame_tree_node_id,
                                     int frame_tree_node_id,
                                     int64_t document_navigation_id,
                                     std::string document_token,
                                     NavigationResponseFacts facts);

// Traffic outside the resource loader. WebSocket, EventSource, and
// WebTransport records share the browser.network channel. Message, event, and
// close-reason text passes through network_text::ReadMessageText, which keeps
// up to network_text::kMessageTextLimit UTF-16 code units and replaces the
// parts that look like a credential. Binary messages record their length only.
// Handshake headers follow the header rules above, and the names of cookies in
// Cookie and Set-Cookie headers are recorded without their values.

// One WebSocket or WebTransport handshake response as Blink received it.
struct RealtimeHandshakeResponseFacts {
  std::string url;
  std::string http_version;
  int status_code = 0;
  std::string status_text;
  std::string remote_ip;
  int remote_port = 0;
  std::string selected_protocol;
  std::string extensions;
  // Negative when the response set no maximum.
  int64_t max_datagram_size = -1;
  std::vector<NetworkHeader> headers;
};

// Records a WebSocket Blink is about to connect, with the script call that
// constructed it.
COMPONENT_EXPORT(RECORDER_BRIDGE)
void RecordBlinkWebSocketCreated(NetworkScope scope,
                                 uint64_t inspector_id,
                                 std::string url,
                                 std::string requested_protocols,
                                 CookieCallOrigin origin);

// Records the opening handshake request the network service reported.
COMPONENT_EXPORT(RECORDER_BRIDGE)
void RecordBlinkWebSocketHandshakeRequest(NetworkScope scope,
                                          uint64_t inspector_id,
                                          std::string url,
                                          std::vector<NetworkHeader> headers);

// Records the opening handshake response that established a connection.
COMPONENT_EXPORT(RECORDER_BRIDGE)
void RecordBlinkWebSocketHandshakeResponse(
    NetworkScope scope,
    uint64_t inspector_id,
    RealtimeHandshakeResponseFacts response);

// Records one message a script sent or Blink received. The opcode is "text" or
// "binary". The text is read only for a text message. A received message has
// no script call, so its origin is empty.
COMPONENT_EXPORT(RECORDER_BRIDGE)
void RecordBlinkWebSocketMessage(NetworkScope scope,
                                 uint64_t inspector_id,
                                 bool sent,
                                 std::string opcode,
                                 int64_t payload_length,
                                 std::string text,
                                 CookieCallOrigin origin);

// Records a script's close() call. A negative code means the script gave none.
COMPONENT_EXPORT(RECORDER_BRIDGE)
void RecordBlinkWebSocketCloseRequested(NetworkScope scope,
                                        uint64_t inspector_id,
                                        int code,
                                        std::string reason,
                                        CookieCallOrigin origin);

// Records a failure Blink reported for a connection, with Blink's message.
COMPONENT_EXPORT(RECORDER_BRIDGE)
void RecordBlinkWebSocketError(NetworkScope scope,
                               uint64_t inspector_id,
                               std::string message);

// Records the end of a connection. The cause is "dropped" when the network
// service closed the channel, which reports whether the close was clean, the
// code, and the reason, or "disconnected" when Blink disposed of the channel.
COMPONENT_EXPORT(RECORDER_BRIDGE)
void RecordBlinkWebSocketClosed(NetworkScope scope,
                                uint64_t inspector_id,
                                std::string cause,
                                bool was_clean,
                                int code,
                                std::string reason);

// Records one event an EventSource is about to dispatch. The inspector id is
// the one the stream's request carries on the network channel.
COMPONENT_EXPORT(RECORDER_BRIDGE)
void RecordBlinkEventSourceMessage(NetworkScope scope,
                                   uint64_t inspector_id,
                                   std::string url,
                                   std::string event_type,
                                   std::string last_event_id,
                                   std::string data);

// Records a WebTransport session a script constructed.
COMPONENT_EXPORT(RECORDER_BRIDGE)
void RecordBlinkWebTransportCreated(NetworkScope scope,
                                    uint64_t transport_id,
                                    std::string url,
                                    CookieCallOrigin origin);

// Records that a WebTransport session was established.
COMPONENT_EXPORT(RECORDER_BRIDGE)
void RecordBlinkWebTransportEstablished(
    NetworkScope scope,
    uint64_t transport_id,
    RealtimeHandshakeResponseFacts response);

// Records a script's close() call on an open or connecting session.
COMPONENT_EXPORT(RECORDER_BRIDGE)
void RecordBlinkWebTransportCloseRequested(NetworkScope scope,
                                           uint64_t transport_id,
                                           bool close_info_present,
                                           int64_t code,
                                           std::string reason,
                                           CookieCallOrigin origin);

// Records the end of a session. An abrupt end carries no close information.
COMPONENT_EXPORT(RECORDER_BRIDGE)
void RecordBlinkWebTransportClosed(NetworkScope scope,
                                   uint64_t transport_id,
                                   bool abrupt,
                                   int64_t code,
                                   std::string reason);

// Page resources (protocol 0.40), on browser.resources. A font file or an
// image is identified by the SHA-256 digest of its bytes, and a renderer
// records the bytes once for each digest.

// Reports the font file recorded for a Skia typeface, by the typeface's
// unique identifier, which Skia does not reuse within a process. Returns false
// when the typeface has not been met; a met typeface whose file could not be
// read has an empty digest.
COMPONENT_EXPORT(RECORDER_BRIDGE)
bool LookUpFontFile(uint32_t typeface_id,
                    std::string* digest,
                    int* collection_index);

// Digests a typeface's font file, records a font-file record the first time
// the renderer meets the digest, and keeps the digest for the typeface.
// Returns the digest, or an empty string when the file was not readable. In
// the recreation mode, it records nothing and keeps the digest.
COMPONENT_EXPORT(RECORDER_BRIDGE)
std::string RecordFontFile(uint32_t typeface_id,
                           int collection_index,
                           bool readable,
                           std::string bytes);

// A number for a FontFace, unique in the renderer, so the face's records
// refer to the same face.
COMPONENT_EXPORT(RECORDER_BRIDGE)
uint64_t AssignFontFaceNumber();

// A FontFace's descriptors as Blink serializes them, and its source.
struct FontFaceFacts {
  FontFaceFacts();
  FontFaceFacts(FontFaceFacts&&);
  FontFaceFacts& operator=(FontFaceFacts&&);
  ~FontFaceFacts();

  std::string family;
  std::string style;
  std::string weight;
  std::string stretch;
  std::string unicode_range;
  std::string variant;
  std::string feature_settings;
  std::string display;
  std::string ascent_override;
  std::string descent_override;
  std::string line_gap_override;
  std::string size_adjust;
  // "url", "data-url", "binary", or "local"; empty when the face has no
  // source.
  std::string source_kind;
  // The source's URL, for a url source.
  std::string source_url;
  // The digest of the font file the face loaded, or empty when Blink holds
  // no file for it, as for a local source.
  std::string font_file_digest;
  int font_file_index = 0;
};

// Records that a FontFace joined a document's set of faces.
COMPONENT_EXPORT(RECORDER_BRIDGE)
void RecordBlinkFontFaceAdded(int document_node_id,
                              std::string document_token,
                              uint64_t face_number);

// Records that a FontFace of a document finished loading.
COMPONENT_EXPORT(RECORDER_BRIDGE)
void RecordBlinkFontFaceLoaded(int document_node_id,
                               std::string document_token,
                               uint64_t face_number,
                               FontFaceFacts face);

// Records that a FontFace left a document's set of faces.
COMPONENT_EXPORT(RECORDER_BRIDGE)
void RecordBlinkFontFaceRemoved(int document_node_id,
                                std::string document_token,
                                uint64_t face_number);

// An image resource that finished loading, with its encoded bytes.
struct ImageResourceFacts {
  ImageResourceFacts();
  ImageResourceFacts(ImageResourceFacts&&);
  ImageResourceFacts& operator=(ImageResourceFacts&&);
  ~ImageResourceFacts();

  std::string url;
  std::string response_url;
  int status = 0;
  std::string mime_type;
  std::string bytes;
  // Protocol 0.48: the Blink image's own ID (Image::paint_image_id()) once
  // the bytes were given to it, or none when no image was made.
  std::optional<int64_t> image_id;
};

// Records an image-resource record, and an image-data record the first time
// the renderer meets the bytes' digest.
COMPONENT_EXPORT(RECORDER_BRIDGE)
void RecordBlinkImageResource(ImageResourceFacts image);

// Protocol 0.51 (slice 4e): a style sheet resource Blink parsed
// (StyleSheetContents::ParseAuthorStyleSheet), with the text it decoded.
struct StyleSheetResourceFacts {
  StyleSheetResourceFacts();
  StyleSheetResourceFacts(StyleSheetResourceFacts&&);
  StyleSheetResourceFacts& operator=(StyleSheetResourceFacts&&);
  ~StyleSheetResourceFacts();

  std::string url;
  std::string response_url;
  int status = 0;
  std::string mime_type;
  // The decoded text, in UTF-8.
  std::string text;
};

// Records a style-sheet-resource record, and a style-sheet-text record the
// first time the renderer meets the text's digest. Returns the digest, which
// the parsed contents keep. Records nothing when the recorder is not active,
// and returns an empty digest.
COMPONENT_EXPORT(RECORDER_BRIDGE)
std::string RecordBlinkStyleSheetResource(StyleSheetResourceFacts sheet);

// Protocol 0.51: a style sheet's CSSOM text, as DevTools builds it (each
// rule's cssText on a line of its own). Records a style-sheet-text record the
// first time the renderer meets the digest, and returns the digest.
COMPONENT_EXPORT(RECORDER_BRIDGE)
std::string RecordBlinkStyleSheetText(std::string text);

// Protocol 0.51: a number for a CSSStyleSheet, unique in the renderer, so the
// sheet's records refer to the same sheet. Numbers start at 1.
COMPONENT_EXPORT(RECORDER_BRIDGE)
uint64_t AssignStyleSheetNumber();

// Protocol 0.51: one style sheet of a tree scope at an update of the active
// style sheets. Only the number is recorded for a sheet whose state is
// unchanged since the document's last record (full is false).
struct StyleSheetFacts {
  StyleSheetFacts();
  StyleSheetFacts(const StyleSheetFacts&);
  StyleSheetFacts(StyleSheetFacts&&);
  StyleSheetFacts& operator=(const StyleSheetFacts&);
  StyleSheetFacts& operator=(StyleSheetFacts&&);
  ~StyleSheetFacts();

  uint64_t sheet_number = 0;
  bool full = false;
  // "link", "style", "import", "constructed", "processing-instruction", or
  // "other".
  std::string kind;
  // The owner node's DOM node ID, or 0 for none.
  int owner_node_id = 0;
  // For an import: the parent sheet's number and the import rule's index.
  uint64_t parent_sheet_number = 0;
  int rule_index = -1;
  // The address, or empty for none.
  std::string href;
  std::string media;
  std::string title;
  bool disabled = false;
  bool active = false;
  // "arrived" (the text the resource arrived with), "element" (the owner
  // element's text, recorded in the DOM), "cssom" (the CSSOM text), or "none".
  std::string text_source;
  // The digest of the text, for "arrived" and "cssom"; empty otherwise.
  std::string text_digest;
};

// Protocol 0.51: a tree scope's style sheets at an update: the scope's root
// node (the document or a shadow root), its sheets in document.styleSheets
// order with each import after the sheet that imports it, and its adopted
// sheets in order.
struct StyleSheetScopeFacts {
  StyleSheetScopeFacts();
  StyleSheetScopeFacts(StyleSheetScopeFacts&&);
  StyleSheetScopeFacts& operator=(StyleSheetScopeFacts&&);
  ~StyleSheetScopeFacts();

  int scope_node_id = 0;
  std::vector<StyleSheetFacts> sheets;
  std::vector<StyleSheetFacts> adopted;
};

// Protocol 0.51: records a style-sheets-updated record for the tree scopes an
// update of a document's active style sheets touched
// (StyleEngine::UpdateActiveStyleSheets).
COMPONENT_EXPORT(RECORDER_BRIDGE)
void RecordBlinkStyleSheetsUpdated(int document_node_id,
                                   std::string document_token,
                                   std::vector<StyleSheetScopeFacts> scopes);

// Protocol 0.52 (slice 4f): one frame of the script stack at a call, as V8
// reports it. An unobserved URL or function name is empty, and an unobserved
// script identifier, line, or column is zero; lines and columns are one-based.
struct ScriptFrameFacts {
  ScriptFrameFacts();
  ScriptFrameFacts(const ScriptFrameFacts&);
  ScriptFrameFacts(ScriptFrameFacts&&);
  ScriptFrameFacts& operator=(const ScriptFrameFacts&);
  ScriptFrameFacts& operator=(ScriptFrameFacts&&);
  ~ScriptFrameFacts();

  int script_id = 0;
  std::string url;
  std::string function_name;
  int line_number = 0;
  int column_number = 0;
  bool is_eval = false;
};

// The most stack frames a timer-origin record carries, innermost first.
inline constexpr size_t kMaximumTimerOriginFrames = 16;

// Protocol 0.52: who scheduled a window timer, read in the setTimeout or
// setInterval call. An empty world kind means no script was running. The
// callback is the function the timer runs, read from the function itself; a
// string handler has none.
struct TimerOriginFacts {
  TimerOriginFacts();
  TimerOriginFacts(TimerOriginFacts&&);
  TimerOriginFacts& operator=(TimerOriginFacts&&);
  ~TimerOriginFacts();

  std::string world_kind;
  int world_id = kExecutionWorldIdUnobserved;
  std::string world_name;
  std::string world_stable_id;
  std::vector<ScriptFrameFacts> stack;
  bool string_handler = false;
  bool has_callback = false;
  ScriptFrameFacts callback;
};

// Notes who scheduled a timer, for the timer-scheduled record the same call
// makes next (RecordBlinkTimerScheduled), which carries the callback location
// and is followed by a timer-origin record.
COMPONENT_EXPORT(RECORDER_BRIDGE)
void NoteBlinkTimerOrigin(uintptr_t timer_identity, TimerOriginFacts facts);

inline constexpr char kScriptSourceKindClassic[] = "classic";
inline constexpr char kScriptSourceKindModule[] = "module";
inline constexpr char kScriptSourceKindEventHandlerAttribute[] =
    "event-handler-attribute";

// Protocol 0.52: the markup a V8 script came from: a script element's classic
// or module script, or an on... attribute's handler. A window handler set by
// a body or frameset attribute is given the document's body element; a zero
// element node ID is an element Blink did not give. The URL is empty for an
// inline script.
struct ScriptSourceFacts {
  ScriptSourceFacts();
  ScriptSourceFacts(const ScriptSourceFacts&);
  ScriptSourceFacts(ScriptSourceFacts&&);
  ScriptSourceFacts& operator=(const ScriptSourceFacts&);
  ScriptSourceFacts& operator=(ScriptSourceFacts&&);
  ~ScriptSourceFacts();

  int document_node_id = 0;
  std::string document_token;
  int script_id = 0;
  std::string kind;
  int element_node_id = 0;
  std::string attribute_name;
  std::string url;
  int line_number = 0;
  int column_number = 0;
};

// Records a script-compiled record for a script whose V8 script ID is known.
COMPONENT_EXPORT(RECORDER_BRIDGE)
void RecordBlinkScriptSource(ScriptSourceFacts facts);

// A classic script is compiled inside its run, so the element running it is
// noted by the script's identity before the run and forgotten after it, and
// the compile records the script ID (RecordBlinkClassicScriptCompiled).
COMPONENT_EXPORT(RECORDER_BRIDGE)
void PushBlinkScriptElement(uintptr_t script_identity, ScriptSourceFacts facts);

COMPONENT_EXPORT(RECORDER_BRIDGE)
void PopBlinkScriptElement(uintptr_t script_identity);

COMPONENT_EXPORT(RECORDER_BRIDGE)
void RecordBlinkClassicScriptCompiled(uintptr_t script_identity,
                                      int script_id);

inline constexpr char kScriptParsedKindClassic[] = "classic";
inline constexpr char kScriptParsedKindModule[] = "module";
inline constexpr char kScriptParsedKindEval[] = "eval";
// A function made by new Function or wrapped by Blink, such as an attribute
// handler.
inline constexpr char kScriptParsedKindFunction[] = "function";

// Protocol 0.54 (slice 4h): a script V8 instantiated, or failed to compile,
// in a document of the main thread, with its source as UTF-8. Line and column
// are one-based; an eval-from script ID of zero is none. Empty texts are
// recorded as null.
struct ScriptParsedFacts {
  ScriptParsedFacts();
  ScriptParsedFacts(ScriptParsedFacts&&);
  ScriptParsedFacts& operator=(ScriptParsedFacts&&);
  ~ScriptParsedFacts();

  int document_node_id = 0;
  std::string document_token;
  std::string world_kind;
  int world_id = kExecutionWorldIdUnobserved;
  std::string world_name;
  std::string world_stable_id;
  int script_id = 0;
  std::string kind;
  std::string source;
  std::string url;
  std::string source_url;
  std::string source_map_url;
  int line_number = 0;
  int column_number = 0;
  int eval_from_script_id = 0;
  bool compile_error = false;
};

// Notes a script ID of the process's main thread, and returns whether it was
// not noted before, so that a script V8 reports again, as it does each time a
// cached script or eval code is instantiated again, is recorded once.
COMPONENT_EXPORT(RECORDER_BRIDGE)
bool ClaimScriptParsed(int script_id);

// Records script-parsed on browser.script, with a script-text record the
// first time the process meets the source's digest.
COMPONENT_EXPORT(RECORDER_BRIDGE)
void RecordScriptParsed(ScriptParsedFacts facts);

// Bracket a DevTools protocol command dispatched on the main thread, so that
// the scripts DevTools compiles, such as a Console expression, are not
// recorded as the page's.
COMPONENT_EXPORT(RECORDER_BRIDGE)
void EnterDevToolsCommand();

COMPONENT_EXPORT(RECORDER_BRIDGE)
void LeaveDevToolsCommand();

COMPONENT_EXPORT(RECORDER_BRIDGE)
bool InDevToolsCommand();

// Protocol 0.48: a paint image Blink made from an image
// (BitmapImage::PaintImageForCurrentFrameWithInfo): the image's own ID, the
// paint image's ID, whether its animation sequence is the image's shared one
// or an element's own, the element it was made for, and the paint image it is
// synchronised to.
// IDs are PaintImage::Id values, which start at 0; a negative one, such as
// PaintImage::kInvalidId, names none.
struct ImagePaintImageFacts {
  int64_t image_id = -1;
  int64_t paint_image_id = -1;
  bool own_sequence = false;
  std::optional<int> node_id;
  std::optional<int64_t> sync_target_paint_image_id;
};

// Records an image-paint-image record the first time the renderer makes a
// paint image with the ID.
COMPONENT_EXPORT(RECORDER_BRIDGE)
void RecordBlinkImagePaintImage(ImagePaintImageFacts facts);

// Protocol 0.48: times a recorder hook's whole work, before and including its
// bridge call, as a kind of its own in the bridge's cost lines. A hook writes
// A11Y_RECORDER_HOOK_COST("hook:name") at the start of its block.
COMPONENT_EXPORT(RECORDER_BRIDGE)
int RegisterHookCostKind(const char* name);
// Returns the start in nanoseconds, or -1 when cost is not being reported.
COMPONENT_EXPORT(RECORDER_BRIDGE)
int64_t StartHookCost(int slot);
COMPONENT_EXPORT(RECORDER_BRIDGE)
void StopHookCost(int slot, int64_t started);

class HookCost {
 public:
  explicit HookCost(int slot) : slot_(slot), started_(StartHookCost(slot)) {}
  ~HookCost() { StopHookCost(slot_, started_); }
  HookCost(const HookCost&) = delete;
  HookCost& operator=(const HookCost&) = delete;

 private:
  const int slot_;
  const int64_t started_;
};

#define A11Y_RECORDER_HOOK_COST(name)                               \
  static const int recorder_hook_cost_slot =                        \
      ::a11y_recorder::RegisterHookCostKind(name);                  \
  const ::a11y_recorder::HookCost recorder_hook_cost(recorder_hook_cost_slot)

// Protocol 0.56 (accessibility preferences, stage 2). See
// docs/architecture/accessibility-preferences.md.
//
// Records the preferences a page's view is sent, on browser.preferences as
// web-preferences-sent. The point is "view-created" (RenderViewHostImpl::
// CreateRenderView, the full set), "web-preferences"
// (SendWebPreferencesToRenderer) or "renderer-preferences"
// (SendRendererPreferencesToRenderer). The fields are the listed values of
// WebPreferences or RendererPreferences, named as in the record. The bridge
// keeps the last fields sent to each view, by its identity, and records only
// the fields that differ from them; a send that changes none is not recorded.
// The view identity is the RenderViewHostImpl's address, used only to tell
// views apart; "view-created" starts a new set for that identity.
COMPONENT_EXPORT(RECORDER_BRIDGE)
void RecordBrowserWebPreferencesSent(int page_frame_tree_node_id,
                                     bool primary_page,
                                     int renderer_process_id,
                                     uintptr_t view_identity,
                                     std::string point,
                                     base::DictValue fields);

// Records the listed browser preferences of a profile as it finishes loading
// (ProfileImpl::DoFinalInit), on browser.preferences as browser-preferences.
// Each preference is a dictionary of its value, whether that is the default,
// and the problem that stopped its reading, keyed by its name in the record.
COMPONENT_EXPORT(RECORDER_BRIDGE)
void RecordBrowserPreferences(std::string profile_directory,
                              bool new_profile,
                              base::DictValue preferences);

// Records a change of one listed browser preference, read as for
// RecordBrowserPreferences, with the reading the bridge last recorded for it
// as the previous one. A reading equal to the previous one is not recorded.
COMPONENT_EXPORT(RECORDER_BRIDGE)
void RecordBrowserPreferenceChanged(std::string profile_directory,
                                    std::string preference,
                                    base::DictValue reading);

// Records a zoom level change of a HostZoomMapImpl, just before its change
// callbacks run. The mode is "host", "scheme-and-host", "temporary" or
// "default"; follows_default is true for a page that uses the default level
// and so changed with it. The level is Chromium's zoom level, of which the
// bridge records the percentage as well.
COMPONENT_EXPORT(RECORDER_BRIDGE)
void RecordBrowserZoomLevelChanged(std::string mode,
                                   bool follows_default,
                                   std::string host,
                                   std::string scheme,
                                   double zoom_level);

// Protocol 0.57 (accessibility preferences, stage 3). Records the color maps
// a page's view is sent, on browser.preferences as color-maps-sent. The
// point is "view-created" (RenderViewHostImpl::CreateRenderView, all three
// maps) or "color-providers" (WebContentsImpl::
// HandleColorRelatedStateChanges, after UpdateColorProviders). The maps are
// a dictionary of "light", "dark", and "forcedColors", each a dictionary of
// RendererColorId names to colors written "#AARRGGBB". The bridge keeps the
// last maps sent to each view and records only the maps that differ from
// them, each whole; a send that changes none is not recorded. The view
// identity is as for RecordBrowserWebPreferencesSent, and "view-created"
// starts a new set for it.
COMPONENT_EXPORT(RECORDER_BRIDGE)
void RecordBrowserColorMapsSent(int page_frame_tree_node_id,
                                bool primary_page,
                                int renderer_process_id,
                                uintptr_t view_identity,
                                std::string point,
                                base::DictValue maps);

}  // namespace a11y_recorder

#endif  // WINDOWS_A11Y_RECORDER_CHROMIUM_RECORDER_BRIDGE_BROWSER_BRIDGE_H_
