#ifndef WINDOWS_A11Y_RECORDER_CHROMIUM_RECORDER_BRIDGE_BROWSER_BRIDGE_H_
#define WINDOWS_A11Y_RECORDER_CHROMIUM_RECORDER_BRIDGE_BROWSER_BRIDGE_H_

#include <stdint.h>

#include <limits>

#include <string>
#include <string_view>
#include <vector>

#include "base/component_export.h"
#include "chromium/recorder_bridge/recorder_switches.h"

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
// The final four parameters of each listener entry point describe the
// JavaScript world the callback belongs to, which is the world the registration
// was made from rather than the world that happened to be current when the
// record was written. An empty world kind means Blink reported no world, which
// is the case for a listener Blink installed itself, and a record with no world
// reports a null world rather than a world of nulls. An empty world name or
// stable identifier means Blink holds none for that world.
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
                                   std::string world_stable_id);

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
                                std::string world_stable_id);

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
                                        std::string world_stable_id);

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

// Adds the Window entry that terminates a Node dispatch path. Blink keeps the
// Window outside its NodeEventContexts, so it is appended separately rather
// than represented as a Node.
COMPONENT_EXPORT(RECORDER_BRIDGE)
void RecordBlinkDispatchPathWindow(uintptr_t event_identity,
                                   int document_node_id,
                                   uintptr_t target_identity,
                                   std::string interface_name);

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
// Returns zero when the recorder is not connected.
COMPONENT_EXPORT(RECORDER_BRIDGE)
uint64_t BeginBlinkDomCheckpoint(int document_node_id,
                                 std::string document_token,
                                 std::string reason,
                                 int maximum_nodes);

// Records one node in preorder. Text content is carried by character-data
// evidence rather than by the node record.
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
                                int maximum_value_length);

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

// One computed-style value of an element. An absent value means Blink
// produced no serialization for the property from the element's style.
struct LayoutCheckpointStyleValue {
  std::string property_name;
  bool value_present = false;
  std::string value;
};

// One element or text node at a layout checkpoint. The rectangle is the value
// getBoundingClientRect would return at the checkpoint, in CSS pixels relative
// to the frame's viewport, and is only meaningful when a layout object exists.
// A display-locked node sits under a content-visibility ancestor that skipped
// its layout, so its rectangle may be stale. The computed style is the style
// Blink already held for the element; the checkpoint never computes one.
struct LayoutCheckpointNode {
  int node_index = -1;
  int node_id = 0;
  int node_type = 0;
  std::string node_name;
  bool layout_object_present = false;
  bool display_locked = false;
  double x = 0;
  double y = 0;
  double width = 0;
  double height = 0;
  bool computed_style_present = false;
  std::vector<LayoutCheckpointStyleValue> computed_style;
};

// Starts one layout and computed-style checkpoint for a document whose
// rendering update reached the paint-clean state. The two counters are Blink's
// cumulative style resolution count for the document and layout count for its
// frame view. Returns zero when the recorder is not connected or when neither
// counter has changed since the previous checkpoint of the same document, so
// an unchanged rendering update produces no evidence.
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

// Completes the layout checkpoint and reports whether the node limit was
// reached before every element and laid-out text node was recorded.
COMPONENT_EXPORT(RECORDER_BRIDGE)
void CompleteBlinkLayoutCheckpoint(uint64_t checkpoint_sequence,
                                   int document_node_id,
                                   std::string document_token,
                                   int node_count,
                                   bool truncated,
                                   int maximum_nodes);

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

// The most headers one list in a record carries. A longer list is cut and
// marked, with its full length recorded.
inline constexpr size_t kMaximumNetworkHeadersPerRecord = 256;

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

}  // namespace a11y_recorder

#endif  // WINDOWS_A11Y_RECORDER_CHROMIUM_RECORDER_BRIDGE_BROWSER_BRIDGE_H_
