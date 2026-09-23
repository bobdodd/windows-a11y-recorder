#ifndef WINDOWS_A11Y_RECORDER_CHROMIUM_RECORDER_BRIDGE_BROWSER_BRIDGE_H_
#define WINDOWS_A11Y_RECORDER_CHROMIUM_RECORDER_BRIDGE_BROWSER_BRIDGE_H_

#include <stdint.h>

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
// as Blink reported them at the call. The fields follow the same conventions
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

}  // namespace a11y_recorder

#endif  // WINDOWS_A11Y_RECORDER_CHROMIUM_RECORDER_BRIDGE_BROWSER_BRIDGE_H_
