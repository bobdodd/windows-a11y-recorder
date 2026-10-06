#include "chromium/recorder_bridge/browser_bridge.h"

#include <windows.h>

#include <algorithm>
#include <array>
#include <atomic>
#include <cmath>
#include <initializer_list>
#include <map>
#include <memory>
#include <optional>
#include <set>
#include <string>
#include <string_view>
#include <unordered_map>
#include <unordered_set>
#include <utility>
#include <vector>

#include "base/base64.h"
#include "base/check.h"
#include "base/command_line.h"
#include "base/containers/span.h"
#include "base/environment.h"
#include "base/memory/raw_ptr.h"
#include "base/memory/read_only_shared_memory_region.h"
#include "base/memory/shared_memory_switch.h"
#include "base/no_destructor.h"
#include "base/numerics/safe_conversions.h"
#include "base/process/launch.h"
#include "base/strings/string_number_conversions.h"
#include "base/synchronization/lock.h"
#include "base/win/windows_handle_util.h"
#include "chromium/recorder_bridge/cookie_text.h"
#include "chromium/recorder_bridge/evidence_cost.h"
#include "chromium/recorder_bridge/animation_settings.h"
#include "chromium/recorder_bridge/full_walks.h"
#include "chromium/recorder_bridge/recreation_input.h"
#include "chromium/recorder_bridge/network_text.h"
#include "chromium/recorder_bridge/recorder_protocol.h"
#include "chromium/recorder_bridge/recorder_switches.h"
#include "components/version_info/version_info.h"
#include "crypto/hash.h"

namespace a11y_recorder {
namespace {

// Records an execution context kind outside the recorded set as other, so an
// unexpected global scope is named honestly instead of as one it is not.
std::string NormalizeEventScopeKind(std::string context_kind) {
  if (context_kind == "window" || context_kind == "dedicated-worker" ||
      context_kind == "shared-worker" || context_kind == "service-worker" ||
      context_kind == "worklet") {
    return context_kind;
  }
  return "other";
}

// A worker or worklet global scope has no document, so records in it name the
// scope rather than a document.
bool IsDocumentFreeScopeKind(std::string_view context_kind) {
  return context_kind == "dedicated-worker" ||
         context_kind == "shared-worker" ||
         context_kind == "service-worker" || context_kind == "worklet";
}

// Decides whether one reported EventTarget can be recorded without inventing
// identity. A target in a window scope belongs to a document, because the
// record names that document. A target in a worker or worklet scope has no
// document and is recorded only when it is not a Node, since no DOM exists
// there. A Node must also report its own node identifier; a Window or other
// non-Node EventTarget has none and is identified by its kind and its
// process-local target identifier instead.
bool IsRecordableEventTarget(std::string_view kind,
                             int document_node_id,
                             int target_node_id,
                             bool document_free_scope = false) {
  if (document_node_id <= 0) {
    return document_free_scope && kind == kEventTargetKindOther;
  }
  if (kind == kEventTargetKindWindow || kind == kEventTargetKindOther) {
    return true;
  }
  return target_node_id > 0;
}


void WriteDiagnosticLine(std::string_view message) {
  std::array<wchar_t, 32768> path = {};
  const DWORD path_length =
      ::GetEnvironmentVariableW(kBridgeLogFileEnvironmentWide, path.data(),
                                static_cast<DWORD>(path.size()));
  HANDLE file = INVALID_HANDLE_VALUE;
  bool close_file = false;
  if (path_length > 0 && path_length < path.size()) {
    file = ::CreateFileW(
        path.data(), FILE_APPEND_DATA,
        FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE, nullptr,
        OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
    close_file = file != INVALID_HANDLE_VALUE;
  } else {
    const base::CommandLine& command_line =
        *base::CommandLine::ForCurrentProcess();
    uint32_t log_handle_value = 0;
    if (command_line.GetSwitchValueASCII(kChromiumEnableLoggingSwitch) ==
            kChromiumLoggingToHandle &&
        base::StringToUint(
            command_line.GetSwitchValueASCII(kChromiumLogFileSwitch),
            &log_handle_value) &&
        log_handle_value != 0) {
      file = base::win::Uint32ToHandle(log_handle_value);
    }
  }
  if (file == INVALID_HANDLE_VALUE) {
    return;
  }

  const std::string line =
      "pid=" + base::NumberToString(::GetCurrentProcessId()) +
      " ticks=" + base::NumberToString(::GetTickCount64()) + " " +
      std::string(message) + "\r\n";
  DWORD written = 0;
  ::WriteFile(file, line.data(), static_cast<DWORD>(line.size()), &written,
              nullptr);
  if (close_file) {
    ::CloseHandle(file);
  }
}

constexpr int64_t kCostReportIntervalNanoseconds = 5'000'000'000;

void WriteCostReport(const std::string& report) {
  WriteDiagnosticLine(report);
}

// Spans cover the work Blink does for the recorder between two bridge calls on
// one thread: the path walk of a dispatch and the traversal of a checkpoint.
int CostSpanSlot(const char* name) {
  return RegisterCostKind(name);
}

std::unique_ptr<RecorderPipeClient>& ProcessClientStorage() {
  static base::NoDestructor<std::unique_ptr<RecorderPipeClient>> client;
  return *client;
}

base::ReadOnlySharedMemoryRegion& ChildBootstrapStorage() {
  static base::NoDestructor<base::ReadOnlySharedMemoryRegion> region;
  return *region;
}

struct EvidenceIdentityStorage {
  base::Lock lock;
  uint64_t next_listener_id = 1;
  uint64_t next_dispatch_id = 1;
  uint64_t next_timer_id = 1;
  uint64_t next_dom_checkpoint_id = 1;
  uint64_t next_dom_transition_id = 1;
  uint64_t next_accessibility_checkpoint_id = 1;
  uint64_t next_interaction_checkpoint_id = 1;
  uint64_t next_presentation_request_id = 1;
  uint64_t next_event_target_id = 1;
  std::unordered_map<uintptr_t, std::string> listener_ids;

  // Non-Node EventTargets have no DOM node identifier, so a Window or other
  // EventTarget is identified by a process-local identifier allocated the first
  // time Blink reports that target. The key is the address Blink uses for the
  // target, which is stable while the target is alive and is only ever compared
  // within one renderer process.
  std::unordered_map<uintptr_t, std::string> event_target_ids;

  // The transitions recorded for a document since its last completed
  // checkpoint. A checkpoint reports the range it covers, so no record ever
  // names evidence that may not exist.
  struct DomTransitionCoverage {
    uint64_t first_transition_id = 0;
    uint64_t last_transition_id = 0;
    int transition_count = 0;
  };

  std::unordered_map<int, DomTransitionCoverage> dom_transition_coverage;

  struct NodeState {
    int document_node_id;
    int node_id;
    std::string tag_name;
    std::string element_id;
    std::string kind;
    std::string interface_name;
    std::string target_id;
  };

  // The tree scope a composed path entry is dispatched in, with the target
  // and related target retargeted for that scope and the path entries that
  // scope's composedPath() exposes.
  struct PathScope {
    int tree_scope_root_node_id = 0;
    std::string shadow_root_mode;
    int target_node_id = 0;
    int related_target_node_id = 0;
    std::vector<int> visible_path_indexes;
    int unmatched_visible_target_count = 0;
  };

  struct DispatchState {
    std::string dispatch_id;
    int document_node_id;
    NodeState original_target;
    std::string event_name;
    bool trusted;
    EventScope scope;
    std::vector<NodeState> composed_path;
    std::vector<PathScope> path_scopes;
    bool observed_default_prevented = false;
    bool observed_propagation_stopped = false;
    bool observed_immediate_propagation_stopped = false;
  };
  struct InvocationState {
    std::string listener_id;
    NodeState current_target;
  };
  struct TimerState {
    std::string timer_id;
    int document_node_id;
    std::string timer_kind;
    std::optional<double> requested_delay_milliseconds;
    std::optional<double> effective_delay_milliseconds;
    int nesting_level;
    // Protocol 0.52: where the callback function is defined.
    std::optional<ScriptFrameFacts> callback = std::nullopt;
  };
  std::unordered_map<uintptr_t, DispatchState> dispatches;
  std::unordered_map<uintptr_t, InvocationState> active_invocations;
  std::unordered_map<uintptr_t, TimerState> timers;
  // Protocol 0.52: who scheduled a timer, noted in the call that schedules it
  // and taken by its timer-scheduled record.
  std::unordered_map<uintptr_t, TimerOriginFacts> timer_origins;
  // Protocol 0.52: the script elements whose classic scripts are running, by
  // the script's identity.
  std::unordered_map<uintptr_t, ScriptSourceFacts> running_script_elements;
};

EvidenceIdentityStorage& EvidenceIdentities() {
  static base::NoDestructor<EvidenceIdentityStorage> identities;
  return *identities;
}

int64_t QueryEvidenceFrequency() {
  static const int64_t frequency = [] {
    LARGE_INTEGER value = {};
    CHECK(::QueryPerformanceFrequency(&value));
    return value.QuadPart;
  }();
  return frequency;
}

std::string DocumentId(int document_node_id) {
  return "dom-document-" + base::NumberToString(document_node_id);
}

std::string DomCheckpointId(uint64_t checkpoint_sequence) {
  return "dom-checkpoint-" + base::NumberToString(checkpoint_sequence);
}

std::string DomTransitionId(uint64_t transition_sequence) {
  return "dom-transition-" + base::NumberToString(transition_sequence);
}

std::string AccessibilityCheckpointId(uint64_t checkpoint_sequence) {
  return "accessibility-checkpoint-" +
         base::NumberToString(checkpoint_sequence);
}

uint64_t AssignDomCheckpointIdentity() {
  EvidenceIdentityStorage& identities = EvidenceIdentities();
  base::AutoLock lock(identities.lock);
  return identities.next_dom_checkpoint_id++;
}

uint64_t AssignInteractionCheckpointIdentity() {
  EvidenceIdentityStorage& identities = EvidenceIdentities();
  base::AutoLock lock(identities.lock);
  return identities.next_interaction_checkpoint_id++;
}

uint64_t AssignPresentationRequestIdentity() {
  EvidenceIdentityStorage& identities = EvidenceIdentities();
  base::AutoLock lock(identities.lock);
  return identities.next_presentation_request_id++;
}

uint64_t AssignAccessibilityCheckpointIdentity() {
  EvidenceIdentityStorage& identities = EvidenceIdentities();
  base::AutoLock lock(identities.lock);
  return identities.next_accessibility_checkpoint_id++;
}

// Assigns the identity of one recorded transition and accumulates it into the
// coverage of the document's next completed checkpoint.
//
// An earlier protocol had the transition name the checkpoint its delivery pass
// was expected to produce. That was a forward reference that could not be
// guaranteed: a document can be discarded before any checkpoint is produced,
// which left the transition naming evidence absent from the archive. The join
// now runs from the checkpoint to the transitions it actually covers, so an
// uncovered transition is stated by omission instead of by a dead reference.
uint64_t AssignDomTransitionIdentity(int document_node_id) {
  EvidenceIdentityStorage& identities = EvidenceIdentities();
  base::AutoLock lock(identities.lock);
  const uint64_t assigned = identities.next_dom_transition_id++;
  EvidenceIdentityStorage::DomTransitionCoverage& coverage =
      identities.dom_transition_coverage[document_node_id];
  if (coverage.transition_count == 0) {
    coverage.first_transition_id = assigned;
  }
  coverage.last_transition_id = assigned;
  ++coverage.transition_count;
  return assigned;
}

EvidenceIdentityStorage::DomTransitionCoverage TakeDomTransitionCoverage(
    int document_node_id) {
  EvidenceIdentityStorage& identities = EvidenceIdentities();
  base::AutoLock lock(identities.lock);
  auto found = identities.dom_transition_coverage.find(document_node_id);
  if (found == identities.dom_transition_coverage.end()) {
    return EvidenceIdentityStorage::DomTransitionCoverage();
  }
  const EvidenceIdentityStorage::DomTransitionCoverage coverage =
      found->second;
  identities.dom_transition_coverage.erase(found);
  return coverage;
}

base::DictValue CreateContext(const RecorderPipeClient& client,
                              int document_node_id,
                              std::string document_token = {}) {
  base::DictValue context;
  context.Set("browserInstanceId", client.browser_instance_id());
  context.Set("processId", static_cast<int>(::GetCurrentProcessId()));
  context.Set("processType", client.process_type());
  context.Set("profileId", base::Value());
  context.Set("browserContextId", base::Value());
  context.Set("pageId", base::Value());
  context.Set("frameId", base::Value());
  if (document_node_id > 0) {
    context.Set("documentId", DocumentId(document_node_id));
  } else {
    context.Set("documentId", base::Value());
  }
  context.Set("executionWorldId", base::Value());
  if (!document_token.empty()) {
    context.Set("documentToken", std::move(document_token));
  } else {
    context.Set("documentToken", base::Value());
  }
  return context;
}

base::DictValue CreateNavigationContext(
    const RecorderPipeClient& client,
    int page_frame_tree_node_id,
    int frame_tree_node_id,
    int64_t document_navigation_id,
    std::string document_token) {
  base::DictValue context =
      CreateContext(client, 0, std::move(document_token));
  context.Set("pageId",
              "frame-" + base::NumberToString(page_frame_tree_node_id));
  context.Set("frameId",
              "frame-" + base::NumberToString(frame_tree_node_id));
  if (document_navigation_id > 0) {
    context.Set(
        "documentId",
        "document-navigation-" +
            base::NumberToString(document_navigation_id));
  }
  return context;
}

base::DictValue CreateNavigationPayload(
    const RecorderPipeClient& client,
    int64_t navigation_id,
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
    bool same_document) {
  base::DictValue payload;
  payload.Set("context",
              CreateNavigationContext(client, page_frame_tree_node_id,
                                      frame_tree_node_id,
                                      document_navigation_id,
                                      std::move(document_token)));
  payload.Set(
      "parentFrameId",
      parent_frame_tree_node_id >= 0
          ? base::Value("frame-" +
                        base::NumberToString(parent_frame_tree_node_id))
          : base::Value());
  payload.Set(
      "parentOrOuterDocumentFrameId",
      parent_or_outer_document_frame_tree_node_id >= 0
          ? base::Value(
                "frame-" +
                base::NumberToString(
                    parent_or_outer_document_frame_tree_node_id))
          : base::Value());
  payload.Set("frameType", std::move(frame_type));
  payload.Set("primaryPage", primary_page);
  payload.Set("navigationId",
              "navigation-" + base::NumberToString(navigation_id));
  payload.Set("url", std::move(url));
  payload.Set("navigationKind",
              same_document ? "same-document" : "cross-document");
  payload.Set("rendererInitiated", renderer_initiated);
  payload.Set("sameDocument", same_document);
  if (renderer_process_id > 0) {
    payload.Set("rendererProcessId", renderer_process_id);
  } else {
    payload.Set("rendererProcessId", base::Value());
  }
  return payload;
}

// Describes the EventTarget a listener or dispatch record is about. A Node
// carries its DOM node identifier. A Window or other non-Node EventTarget has
// no DOM node identifier, so its node identifier is absent rather than zero,
// and it is identified by its kind, its Blink interface name, and a stable
// process-local target identifier.
base::DictValue CreateEventTarget(std::string kind,
                                  std::string interface_name,
                                  std::string target_id,
                                  int document_node_id,
                                  int target_node_id,
                                  std::string target_tag_name,
                                  std::string target_element_id) {
  base::DictValue target;
  target.Set("kind", kind.empty() ? std::string(kEventTargetKindNode)
                                  : std::move(kind));
  if (interface_name.empty()) {
    target.Set("interfaceName", base::Value());
  } else {
    target.Set("interfaceName", std::move(interface_name));
  }
  if (target_id.empty()) {
    target.Set("targetId", base::Value());
  } else {
    target.Set("targetId", std::move(target_id));
  }
  // A target in a worker or worklet scope belongs to no document.
  if (document_node_id > 0) {
    target.Set("documentId", DocumentId(document_node_id));
  } else {
    target.Set("documentId", base::Value());
  }
  if (target_node_id > 0) {
    target.Set("nodeId", target_node_id);
  } else {
    target.Set("nodeId", base::Value());
  }
  target.Set("backendNodeId", base::Value());
  if (target_tag_name.empty()) {
    target.Set("tagName", base::Value());
  } else {
    target.Set("tagName", std::move(target_tag_name));
  }
  if (target_element_id.empty()) {
    target.Set("elementId", base::Value());
  } else {
    target.Set("elementId", std::move(target_element_id));
  }
  target.Set("classes", base::ListValue());
  return target;
}

base::DictValue CreateEventTarget(
    const EvidenceIdentityStorage::NodeState& state) {
  return CreateEventTarget(state.kind, state.interface_name, state.target_id,
                           state.document_node_id, state.node_id,
                           state.tag_name, state.element_id);
}

// Describes the execution context of a listener or dispatch record, in the
// same shape network records use for the context that issued a request.
base::DictValue CreateEventScope(const EventScope& scope) {
  base::DictValue value;
  value.Set("contextKind", NormalizeEventScopeKind(scope.context_kind));
  if (scope.worker_token.empty()) {
    value.Set("workerToken", base::Value());
  } else {
    value.Set("workerToken", scope.worker_token);
  }
  if (scope.global_object_url.empty()) {
    value.Set("globalObjectUrl", base::Value());
  } else {
    value.Set("globalObjectUrl", scope.global_object_url);
  }
  return value;
}

std::string DomNodeTypeName(int node_type) {
  switch (node_type) {
    case 1:
      return "element";
    case 3:
      return "text";
    case 8:
      return "comment";
    case 9:
      return "document";
    case 11:
      return "shadow-root";
    default:
      return "other";
  }
}

std::string DomAttributeChangeTypeName(int change_type) {
  switch (change_type) {
    case 0:
      return "added";
    case 1:
      return "removed";
    default:
      return "changed";
  }
}

// Records a value that the caller may have truncated, alongside the full
// length it had before truncation, so a partial observation is explicit.
void SetTruncatedTextProperties(base::DictValue& payload,
                                const std::string& value_property,
                                const std::string& length_property,
                                const std::string& truncated_property,
                                std::string value,
                                int value_length,
                                bool value_truncated) {
  payload.Set(value_property, std::move(value));
  payload.Set(length_property, value_length);
  payload.Set(truncated_property, value_truncated);
}

base::DictValue CreateDomStateChangeBasePayload(
    const RecorderPipeClient& client,
    int document_node_id,
    std::string document_token) {
  base::DictValue payload;
  payload.Set("context",
              CreateContext(client, document_node_id,
                            std::move(document_token)));
  const uint64_t assigned = AssignDomTransitionIdentity(document_node_id);
  payload.Set("transitionId", DomTransitionId(assigned));
  return payload;
}

base::DictValue CreateDomCheckpointBasePayload(
    const RecorderPipeClient& client,
    uint64_t checkpoint_sequence,
    int document_node_id,
    std::string document_token) {
  base::DictValue payload;
  payload.Set("context",
              CreateContext(client, document_node_id,
                            std::move(document_token)));
  payload.Set("checkpointId", DomCheckpointId(checkpoint_sequence));
  return payload;
}

base::DictValue CreateAccessibilityCheckpointBasePayload(
    const RecorderPipeClient& client,
    uint64_t checkpoint_sequence,
    std::string document_token) {
  base::DictValue payload;
  payload.Set("context",
              CreateContext(client, 0, std::move(document_token)));
  payload.Set("checkpointId",
              AccessibilityCheckpointId(checkpoint_sequence));
  return payload;
}

// Returns the process-local identifier for a non-Node EventTarget, allocating
// one the first time Blink reports that target. A Node is identified by its DOM
// node identifier instead, so it never reaches here.
std::string RegisterEventTargetIdentity(uintptr_t target_identity) {
  if (target_identity == 0) {
    return std::string();
  }
  EvidenceIdentityStorage& identities = EvidenceIdentities();
  base::AutoLock lock(identities.lock);
  auto found = identities.event_target_ids.find(target_identity);
  if (found != identities.event_target_ids.end()) {
    return found->second;
  }
  std::string target_id =
      "event-target-" + base::NumberToString(identities.next_event_target_id++);
  identities.event_target_ids.insert_or_assign(target_identity, target_id);
  return target_id;
}

// Accepts only the registration kinds the archive schema defines. A patched
// Chromium source names a kind with a bridge header constant, so an unexpected
// value means the two sides were built from different revisions, and recording
// it would put a value in the archive that validation rejects for the whole
// session. The addEventListener kind is the one Blink reaches without an
// attribute or handler-property setter, so it is the safe fallback.
std::string NormalizeRegistrationKind(std::string registration_kind) {
  if (registration_kind == kListenerRegistrationKindInlineAttribute ||
      registration_kind == kListenerRegistrationKindEventHandlerProperty ||
      registration_kind == kListenerRegistrationKindAddEventListener) {
    return registration_kind;
  }
  return kListenerRegistrationKindAddEventListener;
}

// Accepts only the world kinds the archive schema defines, for the same reason
// registration kinds are normalized: a patched Chromium source names a kind
// with a bridge header constant, so an unexpected value means the two sides were
// built from different revisions. An unrecognized kind is recorded as other,
// which is the schema value for a world Blink classifies as none of the named
// ones. An empty kind is preserved, because a hook that observed no world must
// not be turned into a record that claims one.
std::string NormalizeExecutionWorldKind(std::string world_kind) {
  if (world_kind.empty() || world_kind == kExecutionWorldKindMain ||
      world_kind == kExecutionWorldKindIsolated ||
      world_kind == kExecutionWorldKindInspectorIsolated ||
      world_kind == kExecutionWorldKindWorkerOrWorklet ||
      world_kind == kExecutionWorldKindShadowRealm ||
      world_kind == kExecutionWorldKindOther) {
    return world_kind;
  }
  return kExecutionWorldKindOther;
}

// Returns the correlatable identifier for a world, which is what the record's
// context reports. Blink's world identifiers are per-thread, so the identifier
// is only comparable within the renderer process that reported it, which is the
// same scope as the process-local event-target identifiers.
std::string ExecutionWorldId(int world_id) {
  return "world-" + base::NumberToString(world_id);
}

// Builds the world value for a listener record. An empty kind means Blink
// reported no world for the callback, which is recorded as a null world rather
// than as a world of nulls. A name or stable identifier Blink does not hold is
// recorded as null rather than as an empty string.
base::Value CreateExecutionWorld(const std::string& world_kind,
                                 int world_id,
                                 std::string world_name,
                                 std::string world_stable_id) {
  if (world_kind.empty()) {
    return base::Value();
  }

  base::DictValue world;
  world.Set("kind", world_kind);
  world.Set("blinkWorldId", world_id);
  world.Set("name", world_name.empty()
                        ? base::Value()
                        : base::Value(std::move(world_name)));
  world.Set("stableId", world_stable_id.empty()
                            ? base::Value()
                            : base::Value(std::move(world_stable_id)));
  return base::Value(std::move(world));
}

std::string RegisterListenerIdentity(uintptr_t listener_identity) {
  EvidenceIdentityStorage& identities = EvidenceIdentities();
  base::AutoLock lock(identities.lock);
  std::string listener_id =
      "listener-" + base::NumberToString(identities.next_listener_id++);
  identities.listener_ids.insert_or_assign(listener_identity, listener_id);
  return listener_id;
}

std::optional<std::string> FindListenerIdentity(uintptr_t listener_identity) {
  EvidenceIdentityStorage& identities = EvidenceIdentities();
  base::AutoLock lock(identities.lock);
  auto found = identities.listener_ids.find(listener_identity);
  if (found == identities.listener_ids.end()) {
    return std::nullopt;
  }
  return found->second;
}

std::optional<std::string> TakeListenerIdentity(uintptr_t listener_identity) {
  EvidenceIdentityStorage& identities = EvidenceIdentities();
  base::AutoLock lock(identities.lock);
  auto found = identities.listener_ids.find(listener_identity);
  if (found == identities.listener_ids.end()) {
    return std::nullopt;
  }
  std::string listener_id = std::move(found->second);
  identities.listener_ids.erase(found);
  return listener_id;
}

EvidenceIdentityStorage::DispatchState CreateDispatchState(
    EvidenceIdentityStorage& identities,
    int document_node_id,
    EvidenceIdentityStorage::NodeState original_target,
    std::string event_name,
    bool trusted,
    EventScope scope) {
  scope.context_kind = NormalizeEventScopeKind(std::move(scope.context_kind));
  return EvidenceIdentityStorage::DispatchState{
      .dispatch_id =
          "dispatch-" + base::NumberToString(identities.next_dispatch_id++),
      .document_node_id = document_node_id,
      .original_target = std::move(original_target),
      .event_name = std::move(event_name),
      .trusted = trusted,
      .scope = std::move(scope),
  };
}

void RegisterDispatchIdentity(
    uintptr_t event_identity,
    int document_node_id,
    int target_node_id,
    std::string event_name,
    std::string target_tag_name,
    std::string target_element_id,
    bool trusted,
    EventScope scope) {
  EvidenceIdentityStorage& identities = EvidenceIdentities();
  base::AutoLock lock(identities.lock);
  identities.dispatches.insert_or_assign(
      event_identity,
      CreateDispatchState(
          identities, document_node_id,
          EvidenceIdentityStorage::NodeState{
              .document_node_id = document_node_id,
              .node_id = target_node_id,
              .tag_name = std::move(target_tag_name),
              .element_id = std::move(target_element_id),
              .kind = kEventTargetKindNode,
          },
          std::move(event_name), trusted, std::move(scope)));
}

// Whether the dispatch an invocation belongs to runs in a worker or worklet
// scope, where a current target has no document.
bool DispatchIsDocumentFree(uintptr_t event_identity) {
  EvidenceIdentityStorage& identities = EvidenceIdentities();
  base::AutoLock lock(identities.lock);
  auto found = identities.dispatches.find(event_identity);
  return found != identities.dispatches.end() &&
         IsDocumentFreeScopeKind(found->second.scope.context_kind);
}

std::optional<EvidenceIdentityStorage::DispatchState> FindDispatchIdentity(
    uintptr_t event_identity) {
  EvidenceIdentityStorage& identities = EvidenceIdentities();
  base::AutoLock lock(identities.lock);
  auto found = identities.dispatches.find(event_identity);
  if (found == identities.dispatches.end()) {
    return std::nullopt;
  }
  return found->second;
}

std::optional<EvidenceIdentityStorage::DispatchState> ObserveDispatchState(
    uintptr_t event_identity,
    bool default_prevented,
    bool propagation_stopped,
    bool immediate_propagation_stopped) {
  EvidenceIdentityStorage& identities = EvidenceIdentities();
  base::AutoLock lock(identities.lock);
  auto found = identities.dispatches.find(event_identity);
  if (found == identities.dispatches.end()) {
    return std::nullopt;
  }
  found->second.observed_default_prevented |= default_prevented;
  found->second.observed_propagation_stopped |= propagation_stopped;
  found->second.observed_immediate_propagation_stopped |=
      immediate_propagation_stopped;
  return found->second;
}

std::optional<EvidenceIdentityStorage::DispatchState> TakeDispatchIdentity(
    uintptr_t event_identity) {
  EvidenceIdentityStorage& identities = EvidenceIdentities();
  base::AutoLock lock(identities.lock);
  auto found = identities.dispatches.find(event_identity);
  if (found == identities.dispatches.end()) {
    return std::nullopt;
  }
  EvidenceIdentityStorage::DispatchState state = std::move(found->second);
  identities.dispatches.erase(found);
  return state;
}

void RegisterActiveInvocation(uintptr_t event_identity,
                              std::string listener_id,
                              std::string current_target_kind,
                              std::string current_target_interface_name,
                              std::string current_target_id,
                              int current_document_node_id,
                              int current_target_node_id,
                              std::string current_target_tag_name,
                              std::string current_target_element_id) {
  EvidenceIdentityStorage& identities = EvidenceIdentities();
  base::AutoLock lock(identities.lock);
  identities.active_invocations.insert_or_assign(
      event_identity,
      EvidenceIdentityStorage::InvocationState{
          .listener_id = std::move(listener_id),
          .current_target = EvidenceIdentityStorage::NodeState{
              .document_node_id = current_document_node_id,
              .node_id = current_target_node_id,
              .tag_name = std::move(current_target_tag_name),
              .element_id = std::move(current_target_element_id),
              .kind = std::move(current_target_kind),
              .interface_name = std::move(current_target_interface_name),
              .target_id = std::move(current_target_id),
          },
      });
}

std::optional<EvidenceIdentityStorage::InvocationState> TakeActiveInvocation(
    uintptr_t event_identity) {
  EvidenceIdentityStorage& identities = EvidenceIdentities();
  base::AutoLock lock(identities.lock);
  auto found = identities.active_invocations.find(event_identity);
  if (found == identities.active_invocations.end()) {
    return std::nullopt;
  }
  EvidenceIdentityStorage::InvocationState invocation =
      std::move(found->second);
  identities.active_invocations.erase(found);
  return invocation;
}

EvidenceIdentityStorage::TimerState RegisterTimerIdentity(
    uintptr_t timer_identity,
    int document_node_id,
    std::string timer_kind,
    std::optional<double> requested_delay_milliseconds,
    std::optional<double> effective_delay_milliseconds,
    int nesting_level,
    std::optional<ScriptFrameFacts> callback = std::nullopt) {
  EvidenceIdentityStorage& identities = EvidenceIdentities();
  base::AutoLock lock(identities.lock);
  EvidenceIdentityStorage::TimerState state{
      .timer_id =
          "timer-" + base::NumberToString(identities.next_timer_id++),
      .document_node_id = document_node_id,
      .timer_kind = std::move(timer_kind),
      .requested_delay_milliseconds = requested_delay_milliseconds,
      .effective_delay_milliseconds = effective_delay_milliseconds,
      .nesting_level = nesting_level,
      .callback = std::move(callback),
  };
  identities.timers.insert_or_assign(timer_identity, state);
  return state;
}

std::optional<EvidenceIdentityStorage::TimerState> FindTimerIdentity(
    uintptr_t timer_identity) {
  EvidenceIdentityStorage& identities = EvidenceIdentities();
  base::AutoLock lock(identities.lock);
  auto found = identities.timers.find(timer_identity);
  if (found == identities.timers.end()) {
    return std::nullopt;
  }
  return found->second;
}

std::optional<EvidenceIdentityStorage::TimerState> TakeTimerIdentity(
    uintptr_t timer_identity) {
  EvidenceIdentityStorage& identities = EvidenceIdentities();
  base::AutoLock lock(identities.lock);
  auto found = identities.timers.find(timer_identity);
  if (found == identities.timers.end()) {
    return std::nullopt;
  }
  EvidenceIdentityStorage::TimerState state = std::move(found->second);
  identities.timers.erase(found);
  return state;
}

std::string EventPhaseName(int event_phase) {
  switch (event_phase) {
    case 1:
      return "capturing";
    case 2:
      return "at-target";
    case 3:
      return "bubbling";
    default:
      return "none";
  }
}

std::string DispatchOutcomeName(int dispatch_result) {
  switch (dispatch_result) {
    case 1:
      return "canceled-by-event-handler";
    case 2:
      return "canceled-by-default-event-handler";
    case 3:
      return "canceled-before-dispatch";
    default:
      return "not-canceled";
  }
}

std::string DefaultActionOutcomeName(int outcome) {
  switch (outcome) {
    case 1:
      return "suppressed-by-event-handler";
    case 2:
      return "already-handled";
    case 3:
      return "ineligible-untrusted-event";
    default:
      return "invoked";
  }
}

std::string PageLifecycleStateName(int page_lifecycle_state) {
  switch (page_lifecycle_state) {
    case 1:
      return "visible";
    case 2:
      return "hidden";
    case 3:
      return "frozen";
    default:
      return "unknown";
  }
}

std::string SchedulerQueueName(int queue_type) {
  switch (queue_type) {
    case 0:
      return "control";
    case 1:
      return "default";
    case 5:
      return "frame-loading";
    case 8:
      return "compositor";
    case 9:
      return "idle";
    case 12:
      return "frame-throttleable";
    case 13:
      return "frame-deferrable";
    case 14:
      return "frame-pausable";
    case 15:
      return "frame-unpausable";
    case 16:
      return "v8";
    case 18:
      return "input";
    case 19:
      return "detached";
    case 24:
      return "web-scheduling";
    case 25:
      return "non-waking";
    case 26:
      return "ipc-tracking-for-cached-pages";
    case 27:
      return "v8-user-visible";
    case 28:
      return "v8-best-effort";
    default:
      return "other";
  }
}

std::string SchedulerThrottlingTypeName(int throttling_type) {
  switch (throttling_type) {
    case 1:
      return "foreground-unimportant";
    case 2:
      return "background";
    case 3:
      return "background-intensive";
    default:
      return "none";
  }
}

std::string SchedulerBlockTypeName(int block_type) {
  return block_type == 0 ? "all-tasks" : "new-tasks-only";
}

// Builds the script-location value for a listener record. Blink reports an
// unobserved URL as an empty string and an unobserved script identifier, line,
// or column as zero, and those are carried through as nulls rather than as
// zeroes, which would read as line zero of an unnamed script. A location that
// is unknown in every field is reported as a null location, so a consumer does
// not have to distinguish an object of nulls from the absence of evidence.
base::Value CreateScriptLocation(std::string script_url,
                                 std::string function_name,
                                 int script_id,
                                 int line_number,
                                 int column_number) {
  if (script_url.empty() && function_name.empty() && script_id <= 0 &&
      line_number <= 0 && column_number <= 0) {
    return base::Value();
  }

  base::DictValue location;
  location.Set("scriptId", script_id > 0 ? base::Value(base::NumberToString(
                                               script_id))
                                         : base::Value());
  location.Set("url",
               script_url.empty() ? base::Value()
                                  : base::Value(std::move(script_url)));
  location.Set("line", line_number > 0 ? base::Value(line_number)
                                       : base::Value());
  location.Set("column", column_number > 0 ? base::Value(column_number)
                                           : base::Value());
  location.Set("functionName",
               function_name.empty()
                   ? base::Value()
                   : base::Value(std::move(function_name)));
  // The recorder does not read script text, so it cannot report a source hash.
  location.Set("sourceHash", base::Value());
  return base::Value(std::move(location));
}

base::DictValue CreateListenerPayload(
    const RecorderPipeClient& client,
    std::string listener_id,
    std::string registration_kind,
    std::string target_kind,
    std::string target_interface_name,
    std::string target_id,
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
    const EventScope& scope) {
  base::DictValue payload;
  base::DictValue context = CreateContext(client, document_node_id);
  // The context reports the world the record is about only when Blink reported
  // one. Every other channel leaves the field null, so a populated value is
  // evidence rather than a default.
  if (!world_kind.empty()) {
    context.Set("executionWorldId", ExecutionWorldId(world_id));
  }
  payload.Set("context", std::move(context));
  payload.Set("listenerId", std::move(listener_id));
  payload.Set("eventName", std::move(event_name));
  payload.Set("registrationKind", std::move(registration_kind));
  payload.Set("target", CreateEventTarget(
                            std::move(target_kind),
                            std::move(target_interface_name),
                            std::move(target_id), document_node_id,
                            target_node_id, std::move(target_tag_name),
                            std::move(target_element_id)));
  payload.Set("capture", capture);
  payload.Set("passive", passive);
  payload.Set("once", once);
  payload.Set("location",
              CreateScriptLocation(std::move(script_url),
                                   std::move(function_name), script_id,
                                   line_number, column_number));
  payload.Set("world",
              CreateExecutionWorld(world_kind, world_id,
                                   std::move(world_name),
                                   std::move(world_stable_id)));
  payload.Set("scope", CreateEventScope(scope));
  return payload;
}

base::DictValue CreateDispatchPayload(
    const RecorderPipeClient& client,
    const EvidenceIdentityStorage::DispatchState& state,
    std::optional<std::string> listener_id,
    std::string phase,
    bool default_prevented,
    bool propagation_stopped,
    bool immediate_propagation_stopped,
    std::optional<std::string> outcome,
    const EvidenceIdentityStorage::NodeState* current_target = nullptr,
    std::optional<std::string> default_action = std::nullopt) {
  base::DictValue payload;
  payload.Set("context", CreateContext(client, state.document_node_id));
  payload.Set("dispatchId", state.dispatch_id);
  payload.Set("eventName", state.event_name);
  payload.Set("trusted", state.trusted);
  payload.Set("originalTarget", CreateEventTarget(state.original_target));
  payload.Set("scope", CreateEventScope(state.scope));
  base::ListValue composed_path;
  for (const auto& entry : state.composed_path) {
    composed_path.Append(CreateEventTarget(entry));
  }
  payload.Set("composedPath", std::move(composed_path));
  base::ListValue path_scopes;
  for (const auto& scope : state.path_scopes) {
    base::DictValue entry;
    entry.Set("treeScopeRootNodeId",
              scope.tree_scope_root_node_id > 0
                  ? base::Value(scope.tree_scope_root_node_id)
                  : base::Value());
    entry.Set("shadowRootMode", scope.shadow_root_mode.empty()
                                    ? base::Value()
                                    : base::Value(scope.shadow_root_mode));
    entry.Set("targetNodeId", scope.target_node_id > 0
                                  ? base::Value(scope.target_node_id)
                                  : base::Value());
    entry.Set("relatedTargetNodeId",
              scope.related_target_node_id > 0
                  ? base::Value(scope.related_target_node_id)
                  : base::Value());
    base::ListValue visible;
    for (int index : scope.visible_path_indexes) {
      visible.Append(index);
    }
    entry.Set("visiblePathIndexes", std::move(visible));
    entry.Set("unmatchedVisibleTargetCount",
              scope.unmatched_visible_target_count);
    path_scopes.Append(std::move(entry));
  }
  payload.Set("pathScopes", std::move(path_scopes));
  if (current_target) {
    payload.Set("currentTarget", CreateEventTarget(*current_target));
  } else {
    payload.Set("currentTarget", base::Value());
  }
  payload.Set("phase", std::move(phase));
  if (listener_id) {
    payload.Set("listenerId", std::move(*listener_id));
  } else {
    payload.Set("listenerId", base::Value());
  }
  payload.Set("defaultPrevented", default_prevented);
  payload.Set("propagationStopped", propagation_stopped);
  payload.Set("immediatePropagationStopped", immediate_propagation_stopped);
  if (default_action) {
    payload.Set("defaultAction", std::move(*default_action));
  } else {
    payload.Set("defaultAction", base::Value());
  }
  if (outcome) {
    payload.Set("outcome", std::move(*outcome));
  } else {
    payload.Set("outcome", base::Value());
  }
  return payload;
}

// Protocol 0.52: takes who scheduled a timer, noted by the call that
// schedules it.
std::optional<TimerOriginFacts> TakeTimerOrigin(uintptr_t timer_identity) {
  EvidenceIdentityStorage& identities = EvidenceIdentities();
  base::AutoLock lock(identities.lock);
  auto found = identities.timer_origins.find(timer_identity);
  if (found == identities.timer_origins.end()) {
    return std::nullopt;
  }
  TimerOriginFacts origin = std::move(found->second);
  identities.timer_origins.erase(found);
  return origin;
}

// One stack frame of a timer-origin record. Unobserved values are null, as in
// a script location.
base::DictValue CreateScriptFrame(ScriptFrameFacts frame) {
  base::DictValue value;
  value.Set("scriptId", frame.script_id > 0
                            ? base::Value(base::NumberToString(frame.script_id))
                            : base::Value());
  value.Set("url", frame.url.empty() ? base::Value()
                                     : base::Value(std::move(frame.url)));
  value.Set("functionName",
            frame.function_name.empty()
                ? base::Value()
                : base::Value(std::move(frame.function_name)));
  value.Set("line", frame.line_number > 0 ? base::Value(frame.line_number)
                                          : base::Value());
  value.Set("column", frame.column_number > 0
                          ? base::Value(frame.column_number)
                          : base::Value());
  value.Set("isEval", frame.is_eval);
  return value;
}

base::DictValue CreateTimerOriginPayload(
    const RecorderPipeClient& client,
    const EvidenceIdentityStorage::TimerState& state,
    TimerOriginFacts origin) {
  const std::string world_kind =
      NormalizeExecutionWorldKind(std::move(origin.world_kind));
  base::DictValue context = CreateContext(client, state.document_node_id);
  if (!world_kind.empty()) {
    context.Set("executionWorldId", ExecutionWorldId(origin.world_id));
  }
  base::DictValue payload;
  payload.Set("context", std::move(context));
  payload.Set("timerId", state.timer_id);
  payload.Set("world", CreateExecutionWorld(world_kind, origin.world_id,
                                            std::move(origin.world_name),
                                            std::move(origin.world_stable_id)));
  base::ListValue stack;
  if (origin.stack.size() > kMaximumTimerOriginFrames) {
    origin.stack.resize(kMaximumTimerOriginFrames);
  }
  for (ScriptFrameFacts& frame : origin.stack) {
    stack.Append(CreateScriptFrame(std::move(frame)));
  }
  payload.Set("stack", std::move(stack));
  payload.Set("handler", origin.string_handler ? "string" : "function");
  return payload;
}

base::DictValue CreateTimerPayload(
    const RecorderPipeClient& client,
    const EvidenceIdentityStorage::TimerState& state,
    std::optional<std::string> cancellation_reason,
    std::optional<bool> did_timeout,
    int page_lifecycle_state) {
  base::DictValue payload;
  payload.Set("context", CreateContext(client, state.document_node_id));
  payload.Set("timerId", state.timer_id);
  payload.Set("timerKind", state.timer_kind);
  if (state.requested_delay_milliseconds) {
    payload.Set("requestedDelayMilliseconds",
                *state.requested_delay_milliseconds);
  } else {
    payload.Set("requestedDelayMilliseconds", base::Value());
  }
  if (state.effective_delay_milliseconds) {
    payload.Set("effectiveDelayMilliseconds",
                *state.effective_delay_milliseconds);
  } else {
    payload.Set("effectiveDelayMilliseconds", base::Value());
  }
  payload.Set("nestingLevel", state.nesting_level);
  payload.Set("throttled", base::Value());
  payload.Set("pageLifecycleState",
              PageLifecycleStateName(page_lifecycle_state));
  if (state.callback) {
    payload.Set("callbackLocation",
                CreateScriptLocation(state.callback->url,
                                     state.callback->function_name,
                                     state.callback->script_id,
                                     state.callback->line_number,
                                     state.callback->column_number));
  } else {
    payload.Set("callbackLocation", base::Value());
  }
  if (cancellation_reason) {
    payload.Set("cancellationReason", std::move(*cancellation_reason));
  } else {
    payload.Set("cancellationReason", base::Value());
  }
  if (did_timeout) {
    payload.Set("didTimeout", *did_timeout);
  } else {
    payload.Set("didTimeout", base::Value());
  }
  return payload;
}

inline constexpr char kOmissionEventType[] = "collector-omission";
inline constexpr char kEvidenceWriteFailedOmissionReason[] =
    "browser-evidence-write-failed";

// A record whose write failed never reaches the archive, and a diagnostic line
// in the bridge log is not evidence a reader of the archive can see. The count
// of lost records is held per channel and stated by an omission record on that
// same channel as soon as the pipe accepts a write again. A loss the process
// never gets to report, because it is shutting down or its pipe never recovers,
// stays unreported in the archive, which no in-process reporter can fix.
// The count of records of the DOM and layout channels this process could not
// write. A document whose channel lost a record since its last full walk is
// walked again at its next update (protocol 0.35), so its change records
// continue from a state they apply to.
std::atomic<uint64_t>& DomEvidenceLosses() {
  static std::atomic<uint64_t> losses{0};
  return losses;
}

std::atomic<uint64_t>& LayoutEvidenceLosses() {
  static std::atomic<uint64_t> losses{0};
  return losses;
}

void CountEvidenceLoss(const std::string& channel, int count) {
  if (count <= 0) {
    return;
  }
  if (channel == "browser.dom") {
    DomEvidenceLosses().fetch_add(static_cast<uint64_t>(count));
  } else if (channel == "browser.layout") {
    LayoutEvidenceLosses().fetch_add(static_cast<uint64_t>(count));
  }
}

// When each document is walked in full. The interval comes from the
// recording's bootstrap, and is set once the process connects.
struct FullWalkStorage {
  base::Lock lock;
  std::unique_ptr<FullWalkSchedule> schedule;
};

FullWalkStorage& FullWalks() {
  static base::NoDestructor<FullWalkStorage> storage;
  return *storage;
}

void SetFullWalkInterval(int interval) {
  FullWalkStorage& storage = FullWalks();
  base::AutoLock lock(storage.lock);
  storage.schedule = std::make_unique<FullWalkSchedule>(interval);
}

std::string DomWalkReason(int document_node_id, const std::string& requested) {
  FullWalkStorage& storage = FullWalks();
  base::AutoLock lock(storage.lock);
  if (!storage.schedule) {
    storage.schedule = std::make_unique<FullWalkSchedule>(0);
  }
  return storage.schedule->DomWalkReason(document_node_id, requested,
                                         DomEvidenceLosses().load());
}

std::string LayoutWalkReason(int document_node_id) {
  FullWalkStorage& storage = FullWalks();
  base::AutoLock lock(storage.lock);
  if (!storage.schedule) {
    storage.schedule = std::make_unique<FullWalkSchedule>(0);
  }
  return storage.schedule->LayoutWalkReason(document_node_id,
                                            LayoutEvidenceLosses().load());
}

base::Lock& OmittedEvidenceLock() {
  static base::NoDestructor<base::Lock> lock;
  return *lock;
}

std::map<std::string, int>& OmittedEvidenceCounts() {
  static base::NoDestructor<std::map<std::string, int>> counts;
  return *counts;
}

void HoldOmittedEvidence(const std::string& channel, int count) {
  CountEvidenceLoss(channel, count);
  base::AutoLock lock(OmittedEvidenceLock());
  OmittedEvidenceCounts()[channel] += count;
}

int TakeOmittedEvidence(const std::string& channel) {
  base::AutoLock lock(OmittedEvidenceLock());
  std::map<std::string, int>& counts = OmittedEvidenceCounts();
  auto held = counts.find(channel);
  if (held == counts.end()) {
    return 0;
  }
  const int count = held->second;
  counts.erase(held);
  return count;
}

// The omission record is not itself captured evidence, so a failed omission
// write restates the original count instead of counting the omission as one
// more lost record. The record is queued ahead of the record that follows it,
// so it still precedes that record in the archive.
void ReportOmittedEvidence(RecorderPipeClient* client,
                           const std::string& channel) {
  const int count = TakeOmittedEvidence(channel);
  if (count <= 0) {
    return;
  }
  base::DictValue payload;
  payload.Set("context", CreateContext(*client, 0));
  payload.Set("reason", std::string(kEvidenceWriteFailedOmissionReason));
  payload.Set("count", count);
  std::string error;
  if (!client->SendEvidence(channel, std::string(kOmissionEventType),
                            std::move(payload), &error,
                            /*lost_records_on_failure=*/count)) {
    HoldOmittedEvidence(channel, count);
  }
}

// Runs on the writer thread when a queued record could not be written. The
// count is reported by the next record queued on the same channel.
void HoldFailedEvidenceWrite(const std::string& channel,
                             int lost_records,
                             const std::string& error) {
  HoldOmittedEvidence(channel, lost_records);
  WriteDiagnosticLine("Blink evidence write failed: " + error);
}

// Queues a record for the writer thread. The timestamp is taken when the
// record reaches the queue, on the thread that observed the evidence, as for
// SendBlinkEvidence, so the writer thread's delay does not move it.
void QueueBlinkEvidence(RecorderPipeClient* client,
                        std::unique_ptr<PendingEvidence> evidence) {
  ReportOmittedEvidence(client, evidence->channel);
  const std::string channel = evidence->channel;
  std::string error;
  if (!client->QueueEvidence(std::move(evidence), &error)) {
    HoldOmittedEvidence(channel, 1);
    WriteDiagnosticLine("Blink evidence write failed: " + error);
  }
}

void SendBlinkEvidence(std::string channel,
                       std::string event_type,
                       base::DictValue payload) {
  RecorderPipeClient* client = GetProcessRecorderClient();
  if (!client) {
    return;
  }
  ReportOmittedEvidence(client, channel);
  const std::string reported_channel = channel;
  std::string error;
  if (!client->SendEvidence(std::move(channel), std::move(event_type),
                            std::move(payload), &error)) {
    HoldOmittedEvidence(reported_channel, 1);
    WriteDiagnosticLine("Blink evidence write failed: " + error);
  }
}

bool IsSupportedChildProcess(std::string_view process_type) {
  return process_type == kChromiumRendererProcess;
}

bool StoreChildBootstrap(const BootstrapConfiguration& configuration,
                         std::string* error) {
  std::string json;
  if (!SerializeBootstrapConfiguration(configuration, &json, error)) {
    return false;
  }

  base::MappedReadOnlyRegion mapped_region =
      base::ReadOnlySharedMemoryRegion::Create(json.size());
  if (!mapped_region.IsValid()) {
    *error = "Could not allocate the child recorder bootstrap.";
    return false;
  }
  mapped_region.mapping.GetMemoryAsSpan<uint8_t>().copy_from(
      base::as_byte_span(json));
  ChildBootstrapStorage() = std::move(mapped_region.region);

  // Publish only Chromium's opaque serialized handle metadata through the
  // process environment so the child-launch boundary does not depend on a
  // module-local pointer. The authentication token remains exclusively inside
  // the read-only shared-memory region.
  base::CommandLine metadata_command_line(base::CommandLine::NO_PROGRAM);
  base::LaunchOptions metadata_launch_options;
  base::shared_memory::SharedMemorySwitch bootstrap_switch(
      kChildBootstrapHandleSwitch, 0, 0);
  bootstrap_switch.AddToLaunchParameters(ChildBootstrapStorage(),
                                         &metadata_command_line,
                                         &metadata_launch_options);
  const std::string metadata =
      metadata_command_line.GetSwitchValueASCII(kChildBootstrapHandleSwitch);
  if (metadata.empty() || !base::Environment::Create()->SetVar(
                              kChildBootstrapMetadataEnvironment, metadata)) {
    ChildBootstrapStorage() = base::ReadOnlySharedMemoryRegion();
    *error = "Could not publish the child recorder bootstrap metadata.";
    return false;
  }
  return true;
}

bool ReadChildBootstrap(const base::CommandLine& command_line,
                        BootstrapConfiguration* configuration,
                        std::string* error) {
  WriteDiagnosticLine("Reading inherited child recorder bootstrap.");
  auto region = base::shared_memory::ReadOnlySharedMemoryRegionFrom(
      command_line.GetSwitchValueASCII(kChildBootstrapHandleSwitch));
  if (!region.has_value() || !region->IsValid()) {
    *error = "The inherited child recorder bootstrap was invalid.";
    WriteDiagnosticLine(*error);
    return false;
  }
  WriteDiagnosticLine("Inherited child recorder bootstrap handle is valid.");
  base::ReadOnlySharedMemoryMapping mapping = region->Map();
  if (!mapping.IsValid()) {
    *error = "The inherited child recorder bootstrap could not be mapped.";
    WriteDiagnosticLine(*error);
    return false;
  }
  WriteDiagnosticLine("Inherited child recorder bootstrap was mapped.");
  const auto chars = base::as_chars(mapping.GetMemoryAsSpan<uint8_t>());
  if (!ParseBootstrapConfiguration(
          std::string_view(chars.data(), chars.size()), configuration, error)) {
    WriteDiagnosticLine("Inherited child recorder bootstrap parsing failed: " +
                        *error);
    return false;
  }
  WriteDiagnosticLine("Inherited child recorder bootstrap was parsed.");
  return true;
}


// Every cookie is recorded. The bound is the largest count the protocol's
// 32-bit counts hold; a record states its full count and whether its list was
// cut, which happens only where the count cannot be stated.
constexpr size_t kMaximumCookiesPerRecord = 2147483647;

struct CookieStoreRequestState {
  std::string request_id;
  std::string method;
};

struct CookieEvidenceStorage {
  base::Lock lock;
  uint64_t next_access_id = 1;
  uint64_t next_request_id = 1;
  // A Cookie Store API promise resolver stays alive until its reply arrives,
  // so its address identifies one request between the call and the reply
  // within one renderer process.
  std::unordered_map<uintptr_t, CookieStoreRequestState> requests;
};

CookieEvidenceStorage& CookieEvidence() {
  static base::NoDestructor<CookieEvidenceStorage> storage;
  return *storage;
}

std::string AssignCookieAccessId() {
  CookieEvidenceStorage& storage = CookieEvidence();
  base::AutoLock lock(storage.lock);
  return "cookie-access-" + base::NumberToString(storage.next_access_id++);
}

std::string AssignCookieStoreRequestId(uintptr_t resolver_identity,
                                       const std::string& method) {
  CookieEvidenceStorage& storage = CookieEvidence();
  base::AutoLock lock(storage.lock);
  std::string request_id = "cookie-store-request-" +
                           base::NumberToString(storage.next_request_id++);
  if (resolver_identity != 0) {
    storage.requests.insert_or_assign(
        resolver_identity, CookieStoreRequestState{request_id, method});
  }
  return request_id;
}

std::optional<CookieStoreRequestState> TakeCookieStoreRequest(
    uintptr_t resolver_identity) {
  CookieEvidenceStorage& storage = CookieEvidence();
  base::AutoLock lock(storage.lock);
  auto found = storage.requests.find(resolver_identity);
  if (found == storage.requests.end()) {
    return std::nullopt;
  }
  CookieStoreRequestState state = std::move(found->second);
  storage.requests.erase(found);
  return state;
}

// The resolver a Cookie Store API write noted just before it was sent. Blink
// runs each CookieStore on one thread, and the write call records immediately
// after its write path returns, so a per-thread slot pairs the two without a
// lock.
constinit thread_local uintptr_t g_pending_cookie_store_write_resolver = 0;

base::Value OptionalString(bool present, std::string value) {
  return present ? base::Value(std::move(value)) : base::Value();
}

base::Value NonEmptyString(std::string value) {
  return value.empty() ? base::Value() : base::Value(std::move(value));
}

base::ListValue StringList(std::vector<std::string> values) {
  base::ListValue list;
  for (std::string& value : values) {
    list.Append(std::move(value));
  }
  return list;
}

// Sets the cookie count, the names up to the record limit, and whether the
// list was cut.
void SetCookieNames(base::DictValue& payload,
                    std::vector<std::string> cookie_names) {
  const size_t count = cookie_names.size();
  const bool truncated = count > kMaximumCookiesPerRecord;
  if (truncated) {
    cookie_names.resize(kMaximumCookiesPerRecord);
  }
  payload.Set("cookieCount", base::checked_cast<int>(count));
  payload.Set("cookieNames", StringList(std::move(cookie_names)));
  payload.Set("cookieNamesTruncated", truncated);
}

base::DictValue CreateCookieRendererContext(const RecorderPipeClient& client,
                                            int document_node_id,
                                            std::string document_token,
                                            const CookieCallOrigin& origin) {
  base::DictValue context =
      CreateContext(client, document_node_id, std::move(document_token));
  if (!NormalizeExecutionWorldKind(origin.world_kind).empty()) {
    context.Set("executionWorldId", ExecutionWorldId(origin.world_id));
  }
  return context;
}

void SetCookieCallOrigin(base::DictValue& payload, CookieCallOrigin origin) {
  const std::string world_kind =
      NormalizeExecutionWorldKind(std::move(origin.world_kind));
  payload.Set("location",
              CreateScriptLocation(std::move(origin.script_url),
                                   std::move(origin.function_name),
                                   origin.script_id, origin.line_number,
                                   origin.column_number));
  payload.Set("world", CreateExecutionWorld(world_kind, origin.world_id,
                                            std::move(origin.world_name),
                                            std::move(origin.world_stable_id)));
}

base::DictValue CreateCookieWriteAttributes(CookieWriteRequest request,
                                            bool include_script_only_flags) {
  base::DictValue attributes;
  attributes.Set("domain", OptionalString(request.domain_present,
                                          std::move(request.domain)));
  attributes.Set("path",
                 OptionalString(request.path_present, std::move(request.path)));
  attributes.Set("sameSite", OptionalString(request.same_site_present,
                                            std::move(request.same_site)));
  attributes.Set("partitioned", request.partitioned);
  attributes.Set("expiresPresent", request.expires_present);
  if (include_script_only_flags) {
    attributes.Set("secure", request.secure);
    attributes.Set("httpOnly", request.http_only);
    attributes.Set("maxAgePresent", request.max_age_present);
    attributes.Set("attributeNames",
                   StringList(std::move(request.attribute_names)));
  }
  return attributes;
}

base::DictValue CreateCookieAccessEntry(CookieAccessEntry entry) {
  const cookie_text::CookieInclusionText inclusion =
      cookie_text::ParseInclusionDebugString(entry.inclusion_status);
  base::DictValue cookie;
  cookie.Set("name", std::move(entry.name));
  cookie.Set("parsed", entry.parsed);
  if (entry.parsed) {
    cookie.Set("domain", std::move(entry.domain));
    cookie.Set("path", std::move(entry.path));
    cookie.Set("sameSite", std::move(entry.same_site));
    cookie.Set("secure", entry.secure);
    cookie.Set("httpOnly", entry.http_only);
    cookie.Set("hostOnly", entry.host_only);
    cookie.Set("partitioned", entry.partitioned);
    cookie.Set("persistent", entry.persistent);
    cookie.Set("expired", entry.expired);
  } else {
    for (const char* key :
         {"domain", "path", "sameSite", "secure", "httpOnly", "hostOnly",
          "partitioned", "persistent", "expired"}) {
      cookie.Set(key, base::Value());
    }
  }
  cookie.Set("included", inclusion.included);
  cookie.Set("exclusionReasons", StringList(inclusion.exclusion_reasons));
  cookie.Set("warningReasons", StringList(inclusion.warning_reasons));
  cookie.Set("exemptionReason",
             inclusion.exemption_reason
                 ? base::Value(*inclusion.exemption_reason)
                 : base::Value());
  return cookie;
}

void SetCookieAccess(base::DictValue& payload,
                     bool change,
                     std::string url,
                     std::string frame_origin,
                     std::string top_frame_origin,
                     std::string request_id,
                     bool ad_tagged,
                     std::vector<CookieAccessEntry> cookies) {
  payload.Set("accessType", change ? "change" : "read");
  payload.Set("url", std::move(url));
  payload.Set("frameOrigin", NonEmptyString(std::move(frame_origin)));
  payload.Set("topFrameOrigin", NonEmptyString(std::move(top_frame_origin)));
  payload.Set("requestId", NonEmptyString(std::move(request_id)));
  payload.Set("adTagged", ad_tagged);
  const size_t count = cookies.size();
  const bool truncated = count > kMaximumCookiesPerRecord;
  if (truncated) {
    cookies.resize(kMaximumCookiesPerRecord);
  }
  base::ListValue entries;
  for (CookieAccessEntry& entry : cookies) {
    entries.Append(CreateCookieAccessEntry(std::move(entry)));
  }
  payload.Set("cookieCount", base::checked_cast<int>(count));
  payload.Set("cookies", std::move(entries));
  payload.Set("cookiesTruncated", truncated);
}

std::string NormalizeCookieContextKind(std::string context_kind) {
  if (context_kind == "window" || context_kind == "service-worker") {
    return context_kind;
  }
  return "other";
}

}  // namespace

void WriteRecorderBridgeDiagnostic(std::string_view message) {
  WriteDiagnosticLine(message);
}

bool WriteProtocolVersionIfRequested() {
  const base::CommandLine& command_line =
      *base::CommandLine::ForCurrentProcess();
  if (!command_line.HasSwitch(kPrintProtocolVersionSwitch)) {
    return false;
  }

  // The recorder redirects standard output for this query. Writing to the
  // handle directly avoids depending on Chromium's logging or standard stream
  // setup, neither of which has run at this point.
  const std::string reported =
      std::string(kProtocolVersionOutputPrefix) + kProtocolVersion + "\r\n";
  const HANDLE output = ::GetStdHandle(STD_OUTPUT_HANDLE);
  if (output && output != INVALID_HANDLE_VALUE) {
    DWORD written = 0;
    ::WriteFile(output, reported.data(),
                base::checked_cast<DWORD>(reported.size()), &written, nullptr);
  }
  WriteDiagnosticLine(std::string("Recorder protocol version query answered ") +
                      kProtocolVersion + ".");
  return true;
}

bool InitializeProcessBridge(std::string* error) {
  if (!error) {
    return false;
  }
  error->clear();

  const base::CommandLine& command_line =
      *base::CommandLine::ForCurrentProcess();
  const std::string command_line_process_type =
      command_line.GetSwitchValueASCII(kChromiumProcessTypeSwitch);
  WriteDiagnosticLine(
      "Recorder process bridge initialization entered for " +
      (command_line_process_type.empty()
           ? std::string("browser")
           : command_line_process_type) +
      ".");
  if (ProcessClientStorage()) {
    *error = "The recorder bridge was initialized more than once.";
    return false;
  }

  BootstrapConfiguration configuration;
  std::string process_type =
      command_line.GetSwitchValueASCII(kChromiumProcessTypeSwitch);
  if (process_type.empty()) {
    if (!command_line.HasSwitch(kBootstrapSwitch)) {
      base::Environment::Create()->UnSetVar(kChildBootstrapMetadataEnvironment);
      return true;
    }
    if (command_line.GetSwitchValueASCII(kBootstrapSwitch) !=
        kBootstrapFromStandardInput) {
      *error = "The recorder bootstrap switch must have the value 'stdin'.";
      return false;
    }
    if (!ReadBootstrapFromStandardInput(&configuration, error)) {
      return false;
    }
    configuration.parent_process_id.reset();
    configuration.child_process_id.reset();
    BootstrapConfiguration child_configuration = configuration;
    child_configuration.parent_process_id =
        static_cast<int>(::GetCurrentProcessId());
    if (!StoreChildBootstrap(child_configuration, error)) {
      return false;
    }
    process_type = "browser";
  } else {
    if (!command_line.HasSwitch(kChildBootstrapHandleSwitch)) {
      WriteDiagnosticLine(
          "Recorder child process did not receive a bootstrap handle.");
      return true;
    }
    if (!IsSupportedChildProcess(process_type)) {
      *error =
          "Recorder bootstrap was supplied to an unsupported child process.";
      return false;
    }
    if (!ReadChildBootstrap(command_line, &configuration, error)) {
      WriteDiagnosticLine("Recorder child bootstrap read failed: " + *error);
      return false;
    }
    if (!configuration.parent_process_id ||
        *configuration.parent_process_id <= 0 ||
        configuration.child_process_id) {
      *error = "The inherited child recorder process metadata was invalid.";
      return false;
    }
    int child_process_id = 0;
    if (!command_line.HasSwitch(kChildProcessIdSwitch) ||
        !base::StringToInt(
            command_line.GetSwitchValueASCII(kChildProcessIdSwitch),
            &child_process_id) ||
        child_process_id <= 0) {
      *error = "The Chromium child process identifier was invalid.";
      return false;
    }
    configuration.child_process_id = child_process_id;
    WriteDiagnosticLine(
        "Recorder child process metadata validation completed.");
  }

  SetFullWalkInterval(configuration.full_walk_interval);
  auto client = std::make_unique<RecorderPipeClient>(std::move(configuration));
  client->SetWriteFailureHandler(&HoldFailedEvidenceWrite);
  WriteDiagnosticLine("Recorder process bridge is connecting to the pipe.");
  if (!client->ConnectAndSynchronize(
          process_type, std::string(version_info::GetVersionNumber()), error)) {
    WriteDiagnosticLine("Recorder process bridge pipe connection failed: " +
                        *error);
    return false;
  }
  // A connection that had to wait means several processes reached the pipe at
  // once. It is not a failure, but it is the only trace that the contention
  // happened, so it is recorded rather than left to be inferred from timing.
  if (client->connect_wait_count() > 0) {
    WriteDiagnosticLine(
        "Recorder process bridge waited for a free pipe instance " +
        base::NumberToString(client->connect_wait_count()) + " times over " +
        base::NumberToString(client->connect_wait_milliseconds()) + " ms.");
  }
  ProcessClientStorage() = std::move(client);
  WriteDiagnosticLine("Recorder process bridge initialized for " +
                      process_type + ".");
  // The cost of each kind of evidence to the threads that observe it is
  // written to the diagnostic log, which is not evidence, so the kinds that
  // slow the page can be found by measurement.
  SetCostReporter(&WriteCostReport, kCostReportIntervalNanoseconds);
  return true;
}

bool AppendRecorderBootstrapToChildProcess(base::CommandLine* command_line,
                                           base::LaunchOptions* launch_options,
                                           int child_process_id,
                                           std::string* error) {
  if (!command_line || !launch_options || !error || child_process_id <= 0) {
    WriteDiagnosticLine(
        "Recorder child bootstrap received invalid launch arguments.");
    return false;
  }
  error->clear();

  const std::string process_type =
      command_line->GetSwitchValueASCII(kChromiumProcessTypeSwitch);
  WriteDiagnosticLine(
      "Recorder child bootstrap hook entered for " +
      (process_type.empty() ? std::string("<empty>") : process_type) +
      " child " + base::NumberToString(child_process_id) + ".");

  // The recreation mode needs no recorder connection, so it is passed to a
  // renderer before the bootstrap is looked for.
  if (process_type == kChromiumRendererProcess &&
      base::CommandLine::ForCurrentProcess()->HasSwitch(kRecreationSwitch) &&
      !command_line->HasSwitch(kRecreationSwitch)) {
    command_line->AppendSwitch(kRecreationSwitch);
    WriteDiagnosticLine("Recorder passed the recreation mode to renderer " +
                        base::NumberToString(child_process_id) + ".");
  }

  const std::optional<std::string> metadata =
      base::Environment::Create()->GetVar(kChildBootstrapMetadataEnvironment);
  if (!metadata.has_value()) {
    WriteDiagnosticLine("Recorder child bootstrap metadata was unavailable.");
    return true;
  }
  if (process_type == kChromiumUtilityProcess &&
      command_line->GetSwitchValueASCII(kChromiumUtilitySubTypeSwitch) ==
          kChromiumNetworkServiceSubType &&
      !command_line->HasSwitch(kRecordingNetworkServiceSwitch)) {
    // The network service takes no recorder connection. The switch only
    // tells it to report handshake cookies by name.
    command_line->AppendSwitch(kRecordingNetworkServiceSwitch);
    WriteDiagnosticLine(
        "Recorder marked network service child " +
        base::NumberToString(child_process_id) + " as recording.");
  }
  if (!IsSupportedChildProcess(process_type)) {
    WriteDiagnosticLine(
        "Recorder child bootstrap skipped unsupported process type " +
        (process_type.empty() ? std::string("<empty>") : process_type) + ".");
    return true;
  }
  if (command_line->HasSwitch(kChildBootstrapHandleSwitch)) {
    *error = "Child recorder bootstrap switch was already present.";
    return false;
  }
  if (command_line->HasSwitch(kChildProcessIdSwitch)) {
    *error = "Child recorder process identifier switch was already present.";
    return false;
  }

  const size_t first_separator = metadata->find(',');
  uint32_t handle_value = 0;
  if (first_separator == std::string::npos ||
      metadata->compare(first_separator, 3, ",i,") != 0 ||
      !base::StringToUint(
          std::string_view(*metadata).substr(0, first_separator),
          &handle_value) ||
      handle_value == 0) {
    *error = "Child recorder bootstrap handle metadata was invalid.";
    return false;
  }
  if (launch_options->elevated) {
    *error = "Recorder bootstrap does not support elevated child processes.";
    return false;
  }

  launch_options->handles_to_inherit.push_back(
      base::win::Uint32ToHandle(handle_value));
  launch_options->environment[kChildBootstrapMetadataEnvironmentWide] = L"";
  command_line->AppendSwitchASCII(kChildBootstrapHandleSwitch, *metadata);
  command_line->AppendSwitchASCII(kChildProcessIdSwitch,
                                  base::NumberToString(child_process_id));
  WriteDiagnosticLine("Attached recorder bootstrap to " + process_type +
                      " child " + base::NumberToString(child_process_id) + ".");
  return true;
}

bool IsRecreationMode() {
  static const bool recreation_mode =
      base::CommandLine::ForCurrentProcess()->HasSwitch(kRecreationSwitch);
  return recreation_mode;
}

namespace {
// Set once on the main thread, read on the compositor thread.
std::atomic<bool> g_recreation_browser_page_process{false};
}  // namespace

bool IsRecreationBrowserPageScheme(std::string_view scheme) {
  return IsBrowserPageScheme(scheme);
}

void MarkRecreationBrowserPageProcess() {
  g_recreation_browser_page_process.store(true, std::memory_order_relaxed);
}

bool RecreationRefusesCompositorInput() {
  return IsRecreationMode() &&
         !g_recreation_browser_page_process.load(std::memory_order_relaxed);
}

bool RecreationHoldsTime() {
  return IsRecreationMode() &&
         !g_recreation_browser_page_process.load(std::memory_order_relaxed);
}

namespace {

using RecreationImageFrames = HeldImageFrames<base::Lock, base::AutoLock>;

RecreationImageFrames& HeldRecreationImageFrames() {
  static base::NoDestructor<RecreationImageFrames> frames;
  return *frames;
}

}  // namespace

void HoldRecreationImageFrame(int64_t paint_image_id, std::string frame_header) {
  if (!IsRecreationMode()) {
    return;
  }
  if (const std::optional<size_t> frame_index =
          ParseRecreationImageFrame(frame_header)) {
    HeldRecreationImageFrames().Hold(paint_image_id, *frame_index);
  }
}

std::optional<size_t> RecreationHeldImageFrame(int64_t paint_image_id) {
  if (!IsRecreationMode()) {
    return std::nullopt;
  }
  return HeldRecreationImageFrames().Find(paint_image_id);
}

RecreationCompositorValues RecreationCompositorValuesOf(std::string_view text) {
  if (!IsRecreationMode()) {
    return {};
  }
  return ParseRecreationCompositorValues(
      text, [](std::string_view number, double* value) {
        return base::StringToDouble(number, value);
      });
}

RecreationPaintWorkletValues RecreationPaintWorkletValuesOf(
    std::string_view text) {
  if (!IsRecreationMode()) {
    return {};
  }
  return ParseRecreationPaintWorkletValues(
      text, [](std::string_view number, double* value) {
        return base::StringToDouble(number, value);
      });
}

RecorderPipeClient* GetProcessRecorderClient() {
  return ProcessClientStorage().get();
}

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
                                   EventScope scope) {
  A11Y_RECORDER_COST("RecordBlinkListenerRegistered");
  scope.context_kind = NormalizeEventScopeKind(std::move(scope.context_kind));
  const bool document_free_scope =
      IsDocumentFreeScopeKind(scope.context_kind);
  RecorderPipeClient* client = GetProcessRecorderClient();
  if (!client || !IsRecordableEventTarget(target_kind, document_node_id,
                                          target_node_id,
                               document_free_scope)) {
    return;
  }

  const bool node_target = target_kind == kEventTargetKindNode;
  base::DictValue payload = CreateListenerPayload(
      *client, RegisterListenerIdentity(listener_identity),
      NormalizeRegistrationKind(std::move(registration_kind)),
      std::move(target_kind), std::move(target_interface_name),
      node_target ? std::string()
                  : RegisterEventTargetIdentity(target_identity),
      document_node_id, target_node_id, std::move(event_name),
      std::move(target_tag_name), std::move(target_element_id), capture,
      passive, once, std::move(script_url), std::move(function_name),
      script_id, line_number, column_number,
      NormalizeExecutionWorldKind(std::move(world_kind)), world_id,
      std::move(world_name), std::move(world_stable_id), scope);
  SendBlinkEvidence("browser.listener", "listener-registered",
                    std::move(payload));
}

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
                                EventScope scope) {
  A11Y_RECORDER_COST("RecordBlinkListenerRemoved");
  scope.context_kind = NormalizeEventScopeKind(std::move(scope.context_kind));
  const bool document_free_scope =
      IsDocumentFreeScopeKind(scope.context_kind);
  RecorderPipeClient* client = GetProcessRecorderClient();
  std::optional<std::string> listener_id =
      TakeListenerIdentity(listener_identity);
  if (!client || !listener_id ||
      !IsRecordableEventTarget(target_kind, document_node_id,
                               target_node_id,
                               document_free_scope)) {
    return;
  }

  const bool node_target = target_kind == kEventTargetKindNode;
  base::DictValue payload = CreateListenerPayload(
      *client, std::move(*listener_id),
      NormalizeRegistrationKind(std::move(registration_kind)),
      std::move(target_kind), std::move(target_interface_name),
      node_target ? std::string()
                  : RegisterEventTargetIdentity(target_identity),
      document_node_id, target_node_id, std::move(event_name),
      std::move(target_tag_name), std::move(target_element_id), capture,
      passive, once, std::move(script_url), std::move(function_name),
      script_id, line_number, column_number,
      NormalizeExecutionWorldKind(std::move(world_kind)), world_id,
      std::move(world_name), std::move(world_stable_id), scope);
  SendBlinkEvidence("browser.listener", "listener-removed",
                    std::move(payload));
}

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
                                         EventScope scope) {
  A11Y_RECORDER_COST("RecordBlinkListenerCallbackReplaced");
  scope.context_kind = NormalizeEventScopeKind(std::move(scope.context_kind));
  const bool document_free_scope =
      IsDocumentFreeScopeKind(scope.context_kind);
  RecorderPipeClient* client = GetProcessRecorderClient();
  if (!client || !IsRecordableEventTarget(target_kind, document_node_id,
                                          target_node_id,
                               document_free_scope)) {
    return;
  }

  // A replacement is only meaningful for a registration this session recorded,
  // and the registration keeps its identity, so the identity is looked up
  // rather than allocated or consumed.
  std::optional<std::string> listener_id =
      FindListenerIdentity(listener_identity);
  if (!listener_id) {
    return;
  }

  const bool node_target = target_kind == kEventTargetKindNode;
  base::DictValue payload = CreateListenerPayload(
      *client, std::move(*listener_id),
      NormalizeRegistrationKind(std::move(registration_kind)),
      std::move(target_kind), std::move(target_interface_name),
      node_target ? std::string()
                  : RegisterEventTargetIdentity(target_identity),
      document_node_id, target_node_id, std::move(event_name),
      std::move(target_tag_name), std::move(target_element_id), capture,
      passive, once, std::move(script_url), std::move(function_name),
      script_id, line_number, column_number,
      NormalizeExecutionWorldKind(std::move(world_kind)), world_id,
      std::move(world_name), std::move(world_stable_id), scope);
  SendBlinkEvidence("browser.listener", "listener-callback-replaced",
                    std::move(payload));
}

void RecordBlinkDispatchStarted(uintptr_t event_identity,
                                int document_node_id,
                                int target_node_id,
                                std::string event_name,
                                std::string target_tag_name,
                                std::string target_element_id,
                                bool trusted,
                                EventScope scope) {
  static const int recorder_span_slot = CostSpanSlot("span:dispatch-path");
  BeginCostSpan(recorder_span_slot);
  A11Y_RECORDER_COST("RecordBlinkDispatchStarted");
  RecorderPipeClient* client = GetProcessRecorderClient();
  if (!client || document_node_id <= 0 || target_node_id <= 0) {
    return;
  }

  RegisterDispatchIdentity(event_identity, document_node_id, target_node_id,
                           std::move(event_name), std::move(target_tag_name),
                           std::move(target_element_id), trusted,
                           std::move(scope));
}

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
                              EventScope scope) {
  static const int recorder_span_slot = CostSpanSlot("span:dispatch-path");
  BeginCostSpan(recorder_span_slot);
  A11Y_RECORDER_COST("BeginBlinkTargetDispatch");
  RecorderPipeClient* client = GetProcessRecorderClient();
  if (!client) {
    return false;
  }
  scope.context_kind = NormalizeEventScopeKind(std::move(scope.context_kind));
  if (!IsRecordableEventTarget(target_kind, document_node_id, target_node_id,
                               IsDocumentFreeScopeKind(scope.context_kind))) {
    return false;
  }
  const bool node_target = target_kind == kEventTargetKindNode;
  // A Node original target is described as the Node dispatcher describes one,
  // by its node identifier alone.
  EvidenceIdentityStorage::NodeState original_target{
      .document_node_id = std::max(document_node_id, 0),
      .node_id = node_target ? target_node_id : 0,
      .tag_name = node_target ? std::move(target_tag_name) : std::string(),
      .element_id = node_target ? std::move(target_element_id) : std::string(),
      .kind = std::move(target_kind),
      .interface_name =
          node_target ? std::string() : std::move(target_interface_name),
      .target_id = node_target ? std::string()
                               : RegisterEventTargetIdentity(target_identity),
  };
  EvidenceIdentityStorage& identities = EvidenceIdentities();
  base::AutoLock lock(identities.lock);
  // An event that is already being recorded belongs to the hook that opened
  // it, so a nested or repeated entry for the same event opens nothing.
  if (identities.dispatches.contains(event_identity)) {
    return false;
  }
  identities.dispatches.insert_or_assign(
      event_identity,
      CreateDispatchState(identities, std::max(document_node_id, 0),
                          std::move(original_target), std::move(event_name),
                          trusted, std::move(scope)));
  return true;
}

namespace {

EvidenceIdentityStorage::PathScope CreatePathScope(
    int tree_scope_root_node_id,
    std::string shadow_root_mode,
    int target_node_id,
    int related_target_node_id,
    const std::vector<int>& visible_path_indexes) {
  EvidenceIdentityStorage::PathScope scope;
  scope.tree_scope_root_node_id = std::max(tree_scope_root_node_id, 0);
  if (shadow_root_mode == "open" || shadow_root_mode == "closed" ||
      shadow_root_mode == "user-agent") {
    scope.shadow_root_mode = std::move(shadow_root_mode);
  }
  scope.target_node_id = std::max(target_node_id, 0);
  scope.related_target_node_id = std::max(related_target_node_id, 0);
  for (int index : visible_path_indexes) {
    if (index < 0) {
      ++scope.unmatched_visible_target_count;
    } else {
      scope.visible_path_indexes.push_back(index);
    }
  }
  return scope;
}

}  // namespace

void RecordBlinkDispatchPathNode(uintptr_t event_identity,
                                 int document_node_id,
                                 int node_id,
                                 std::string tag_name,
                                 std::string element_id,
                                 int tree_scope_root_node_id,
                                 std::string shadow_root_mode,
                                 int target_node_id,
                                 int related_target_node_id,
                                 std::vector<int> visible_path_indexes) {
  A11Y_RECORDER_COST("RecordBlinkDispatchPathNode");
  EvidenceIdentityStorage& identities = EvidenceIdentities();
  base::AutoLock lock(identities.lock);
  auto found = identities.dispatches.find(event_identity);
  if (found == identities.dispatches.end() || document_node_id <= 0 ||
      node_id <= 0) {
    return;
  }
  found->second.composed_path.push_back(
      {.document_node_id = document_node_id,
       .node_id = node_id,
       .tag_name = std::move(tag_name),
       .element_id = std::move(element_id),
       .kind = kEventTargetKindNode});
  found->second.path_scopes.push_back(CreatePathScope(
      tree_scope_root_node_id, std::move(shadow_root_mode), target_node_id,
      related_target_node_id, visible_path_indexes));
}

void RecordBlinkDispatchPathWindow(uintptr_t event_identity,
                                   int document_node_id,
                                   uintptr_t target_identity,
                                   std::string interface_name,
                                   int target_node_id,
                                   int related_target_node_id,
                                   std::vector<int> visible_path_indexes) {
  A11Y_RECORDER_COST("RecordBlinkDispatchPathWindow");
  if (document_node_id <= 0) {
    return;
  }
  std::string target_id = RegisterEventTargetIdentity(target_identity);
  EvidenceIdentityStorage& identities = EvidenceIdentities();
  base::AutoLock lock(identities.lock);
  auto found = identities.dispatches.find(event_identity);
  if (found == identities.dispatches.end()) {
    return;
  }
  found->second.composed_path.push_back(
      {.document_node_id = document_node_id,
       .node_id = 0,
       .kind = kEventTargetKindWindow,
       .interface_name = std::move(interface_name),
       .target_id = std::move(target_id)});
  found->second.path_scopes.push_back(CreatePathScope(
      0, std::string(), target_node_id, related_target_node_id,
      visible_path_indexes));
}

void RecordBlinkDispatchPathTarget(uintptr_t event_identity,
                                   std::string target_kind,
                                   std::string target_interface_name,
                                   uintptr_t target_identity,
                                   int document_node_id,
                                   int target_node_id,
                                   std::string target_tag_name,
                                   std::string target_element_id,
                                   int scope_target_node_id,
                                   bool visible_to_listener) {
  A11Y_RECORDER_COST("RecordBlinkDispatchPathTarget");
  const bool node_target = target_kind == kEventTargetKindNode;
  std::string target_id =
      node_target ? std::string() : RegisterEventTargetIdentity(target_identity);
  EvidenceIdentityStorage& identities = EvidenceIdentities();
  base::AutoLock lock(identities.lock);
  auto found = identities.dispatches.find(event_identity);
  if (found == identities.dispatches.end() ||
      !IsRecordableEventTarget(
          target_kind, document_node_id, target_node_id,
          IsDocumentFreeScopeKind(found->second.scope.context_kind))) {
    return;
  }
  EvidenceIdentityStorage::DispatchState& state = found->second;
  std::vector<int> visible_path_indexes;
  if (visible_to_listener) {
    visible_path_indexes.push_back(
        static_cast<int>(state.composed_path.size()));
  }
  state.composed_path.push_back(
      {.document_node_id = std::max(document_node_id, 0),
       .node_id = node_target ? target_node_id : 0,
       .tag_name = node_target ? std::move(target_tag_name) : std::string(),
       .element_id =
           node_target ? std::move(target_element_id) : std::string(),
       .kind = std::move(target_kind),
       .interface_name =
           node_target ? std::string() : std::move(target_interface_name),
       .target_id = std::move(target_id)});
  state.path_scopes.push_back(CreatePathScope(
      0, std::string(), scope_target_node_id, 0, visible_path_indexes));
}

void CompleteBlinkDispatchStart(uintptr_t event_identity) {
  static const int recorder_span_slot = CostSpanSlot("span:dispatch-path");
  // The span ends when this function returns, so it includes the completion.
  struct SpanEnd {
    ~SpanEnd() { EndCostSpan(recorder_span_slot); }
  } recorder_span_end;
  A11Y_RECORDER_COST("CompleteBlinkDispatchStart");
  RecorderPipeClient* client = GetProcessRecorderClient();
  std::optional<EvidenceIdentityStorage::DispatchState> state =
      FindDispatchIdentity(event_identity);
  if (!client || !state) {
    return;
  }
  base::DictValue payload = CreateDispatchPayload(
      *client, *state, std::nullopt, "none", false, false, false,
      std::nullopt);
  SendBlinkEvidence("browser.dispatch", "dispatch-started", std::move(payload));
}

void BeginBlinkListenerInvocation(uintptr_t event_identity,
                                  uintptr_t listener_identity,
                                  std::string current_target_kind,
                                  std::string current_target_interface_name,
                                  uintptr_t current_target_identity,
                                  int current_document_node_id,
                                  int current_target_node_id,
                                  std::string current_target_tag_name,
                                  std::string current_target_element_id) {
  A11Y_RECORDER_COST("BeginBlinkListenerInvocation");
  std::optional<std::string> listener_id =
      FindListenerIdentity(listener_identity);
  if (!listener_id ||
      !IsRecordableEventTarget(current_target_kind, current_document_node_id,
                               current_target_node_id,
                               DispatchIsDocumentFree(event_identity))) {
    return;
  }
  const bool node_target = current_target_kind == kEventTargetKindNode;
  RegisterActiveInvocation(
      event_identity, std::move(*listener_id), std::move(current_target_kind),
      std::move(current_target_interface_name),
      node_target ? std::string()
                  : RegisterEventTargetIdentity(current_target_identity),
      current_document_node_id, current_target_node_id,
      std::move(current_target_tag_name),
      std::move(current_target_element_id));
}

void RecordBlinkListenerInvoked(uintptr_t event_identity,
                                int event_phase,
                                bool default_prevented,
                                bool propagation_stopped,
                                bool immediate_propagation_stopped) {
  A11Y_RECORDER_COST("RecordBlinkListenerInvoked");
  RecorderPipeClient* client = GetProcessRecorderClient();
  std::optional<EvidenceIdentityStorage::DispatchState> state =
      ObserveDispatchState(event_identity, default_prevented,
                           propagation_stopped,
                           immediate_propagation_stopped);
  std::optional<EvidenceIdentityStorage::InvocationState> invocation =
      TakeActiveInvocation(event_identity);
  if (!client || !state || !invocation) {
    return;
  }

  base::DictValue payload = CreateDispatchPayload(
      *client, *state, std::move(invocation->listener_id),
      EventPhaseName(event_phase),
      default_prevented, propagation_stopped, immediate_propagation_stopped,
      std::nullopt, &invocation->current_target);
  SendBlinkEvidence("browser.dispatch", "listener-invoked",
                    std::move(payload));
}

void RecordBlinkDefaultAction(uintptr_t event_identity,
                              int current_document_node_id,
                              int current_target_node_id,
                              std::string current_target_tag_name,
                              std::string current_target_element_id,
                              int outcome,
                              bool default_prevented,
                              bool propagation_stopped,
                              bool immediate_propagation_stopped) {
  A11Y_RECORDER_COST("RecordBlinkDefaultAction");
  RecorderPipeClient* client = GetProcessRecorderClient();
  std::optional<EvidenceIdentityStorage::DispatchState> state =
      ObserveDispatchState(event_identity, default_prevented,
                           propagation_stopped,
                           immediate_propagation_stopped);
  if (!client || !state || current_document_node_id <= 0 ||
      current_target_node_id <= 0) {
    return;
  }

  EvidenceIdentityStorage::NodeState current_target{
      .document_node_id = current_document_node_id,
      .node_id = current_target_node_id,
      .tag_name = std::move(current_target_tag_name),
      .element_id = std::move(current_target_element_id),
  };
  base::DictValue payload = CreateDispatchPayload(
      *client, *state, std::nullopt, "none", default_prevented,
      propagation_stopped, immediate_propagation_stopped,
      DefaultActionOutcomeName(outcome), &current_target,
      std::string("blink-default-event-handler"));
  SendBlinkEvidence("browser.dispatch", "default-action", std::move(payload));
}

void RecordBlinkTimerScheduled(uintptr_t timer_identity,
                               int document_node_id,
                               int timeout_id,
                               bool repeating,
                               double requested_delay_milliseconds,
                               double effective_delay_milliseconds,
                               int nesting_level,
                               int page_lifecycle_state) {
  A11Y_RECORDER_COST("RecordBlinkTimerScheduled");
  RecorderPipeClient* client = GetProcessRecorderClient();
  if (!client || timer_identity == 0 || document_node_id <= 0 ||
      timeout_id <= 0 || requested_delay_milliseconds < 0 ||
      effective_delay_milliseconds < 0 || nesting_level < 0) {
    return;
  }

  std::optional<TimerOriginFacts> origin = TakeTimerOrigin(timer_identity);
  std::optional<ScriptFrameFacts> callback;
  if (origin && origin->has_callback) {
    callback = origin->callback;
  }
  EvidenceIdentityStorage::TimerState state = RegisterTimerIdentity(
      timer_identity, document_node_id, repeating ? "interval" : "timeout",
      requested_delay_milliseconds, effective_delay_milliseconds,
      nesting_level, std::move(callback));
  base::DictValue payload =
      CreateTimerPayload(*client, state, std::nullopt, std::nullopt,
                         page_lifecycle_state);
  SendBlinkEvidence("browser.timer", "timer-scheduled", std::move(payload));
  if (origin) {
    SendBlinkEvidence(
        "browser.timer", "timer-origin",
        CreateTimerOriginPayload(*client, state, std::move(*origin)));
  }
}

void RecordBlinkTimerFired(uintptr_t timer_identity,
                           bool repeating,
                           int page_lifecycle_state) {
  A11Y_RECORDER_COST("RecordBlinkTimerFired");
  RecorderPipeClient* client = GetProcessRecorderClient();
  if (!client || timer_identity == 0) {
    return;
  }

  std::optional<EvidenceIdentityStorage::TimerState> state =
      repeating ? FindTimerIdentity(timer_identity)
                : TakeTimerIdentity(timer_identity);
  if (!state) {
    return;
  }
  base::DictValue payload =
      CreateTimerPayload(*client, *state, std::nullopt, std::nullopt,
                         page_lifecycle_state);
  SendBlinkEvidence("browser.timer", "timer-fired", std::move(payload));
}

void RecordBlinkTimerCancelled(uintptr_t timer_identity,
                               int page_lifecycle_state) {
  A11Y_RECORDER_COST("RecordBlinkTimerCancelled");
  RecorderPipeClient* client = GetProcessRecorderClient();
  if (!client || timer_identity == 0) {
    return;
  }

  std::optional<EvidenceIdentityStorage::TimerState> state =
      TakeTimerIdentity(timer_identity);
  if (!state) {
    return;
  }
  base::DictValue payload =
      CreateTimerPayload(*client, *state, std::string("explicit-clear"),
                         std::nullopt, page_lifecycle_state);
  SendBlinkEvidence("browser.timer", "timer-cancelled", std::move(payload));
}

void RecordBlinkAnimationFrameScheduled(uintptr_t callback_identity,
                                        int document_node_id,
                                        int callback_id,
                                        int page_lifecycle_state) {
  A11Y_RECORDER_COST("RecordBlinkAnimationFrameScheduled");
  RecorderPipeClient* client = GetProcessRecorderClient();
  if (!client || callback_identity == 0 || document_node_id <= 0 ||
      callback_id <= 0) {
    return;
  }

  EvidenceIdentityStorage::TimerState state = RegisterTimerIdentity(
      callback_identity, document_node_id, "animation-frame", std::nullopt,
      std::nullopt, 0);
  base::DictValue payload =
      CreateTimerPayload(*client, state, std::nullopt, std::nullopt,
                         page_lifecycle_state);
  SendBlinkEvidence("browser.timer", "timer-scheduled", std::move(payload));
}

void RecordBlinkAnimationFrameFired(uintptr_t callback_identity,
                                    int page_lifecycle_state) {
  A11Y_RECORDER_COST("RecordBlinkAnimationFrameFired");
  RecorderPipeClient* client = GetProcessRecorderClient();
  if (!client || callback_identity == 0) {
    return;
  }

  std::optional<EvidenceIdentityStorage::TimerState> state =
      TakeTimerIdentity(callback_identity);
  if (!state || state->timer_kind != "animation-frame") {
    return;
  }
  base::DictValue payload =
      CreateTimerPayload(*client, *state, std::nullopt, std::nullopt,
                         page_lifecycle_state);
  SendBlinkEvidence("browser.timer", "timer-fired", std::move(payload));
}

void RecordBlinkAnimationFrameCancelled(uintptr_t callback_identity,
                                        int page_lifecycle_state) {
  A11Y_RECORDER_COST("RecordBlinkAnimationFrameCancelled");
  RecorderPipeClient* client = GetProcessRecorderClient();
  if (!client || callback_identity == 0) {
    return;
  }

  std::optional<EvidenceIdentityStorage::TimerState> state =
      TakeTimerIdentity(callback_identity);
  if (!state || state->timer_kind != "animation-frame") {
    return;
  }
  base::DictValue payload = CreateTimerPayload(
      *client, *state, std::string("explicit-cancel-animation-frame"),
      std::nullopt, page_lifecycle_state);
  SendBlinkEvidence("browser.timer", "timer-cancelled", std::move(payload));
}

void RecordBlinkIdleCallbackScheduled(uintptr_t callback_identity,
                                      int document_node_id,
                                      int callback_id,
                                      bool has_timeout,
                                      double timeout_milliseconds,
                                      int page_lifecycle_state) {
  A11Y_RECORDER_COST("RecordBlinkIdleCallbackScheduled");
  RecorderPipeClient* client = GetProcessRecorderClient();
  if (!client || callback_identity == 0 || document_node_id <= 0 ||
      callback_id <= 0 || timeout_milliseconds < 0) {
    return;
  }

  EvidenceIdentityStorage::TimerState state = RegisterTimerIdentity(
      callback_identity, document_node_id, "idle-callback",
      has_timeout ? std::optional<double>(timeout_milliseconds) : std::nullopt,
      has_timeout ? std::optional<double>(timeout_milliseconds) : std::nullopt,
      0);
  base::DictValue payload =
      CreateTimerPayload(*client, state, std::nullopt, std::nullopt,
                         page_lifecycle_state);
  SendBlinkEvidence("browser.timer", "timer-scheduled", std::move(payload));
}

void RecordBlinkIdleCallbackFired(uintptr_t callback_identity,
                                  bool did_timeout,
                                  int page_lifecycle_state) {
  A11Y_RECORDER_COST("RecordBlinkIdleCallbackFired");
  RecorderPipeClient* client = GetProcessRecorderClient();
  if (!client || callback_identity == 0) {
    return;
  }

  std::optional<EvidenceIdentityStorage::TimerState> state =
      TakeTimerIdentity(callback_identity);
  if (!state || state->timer_kind != "idle-callback") {
    return;
  }
  base::DictValue payload =
      CreateTimerPayload(*client, *state, std::nullopt, did_timeout,
                         page_lifecycle_state);
  SendBlinkEvidence("browser.timer", "timer-fired", std::move(payload));
}

void RecordBlinkIdleCallbackCancelled(uintptr_t callback_identity,
                                      int page_lifecycle_state) {
  A11Y_RECORDER_COST("RecordBlinkIdleCallbackCancelled");
  RecorderPipeClient* client = GetProcessRecorderClient();
  if (!client || callback_identity == 0) {
    return;
  }

  std::optional<EvidenceIdentityStorage::TimerState> state =
      TakeTimerIdentity(callback_identity);
  if (!state || state->timer_kind != "idle-callback") {
    return;
  }
  base::DictValue payload = CreateTimerPayload(
      *client, *state, std::string("explicit-cancel-idle-callback"),
      std::nullopt, page_lifecycle_state);
  SendBlinkEvidence("browser.timer", "timer-cancelled", std::move(payload));
}

uint64_t BeginBlinkDomCheckpoint(int document_node_id,
                                 std::string document_token,
                                 std::string reason,
                                 int maximum_nodes) {
  static const int recorder_span_slot = CostSpanSlot("span:dom-checkpoint");
  BeginCostSpan(recorder_span_slot);
  A11Y_RECORDER_COST("BeginBlinkDomCheckpoint");
  RecorderPipeClient* client = GetProcessRecorderClient();
  if (!client || document_node_id <= 0 || document_token.empty() ||
      reason.empty() ||
      maximum_nodes <= 0) {
    return 0;
  }
  std::string walk_reason = DomWalkReason(document_node_id, reason);
  if (walk_reason.empty()) {
    return 0;
  }
  const uint64_t checkpoint_sequence = AssignDomCheckpointIdentity();
  base::DictValue payload = CreateDomCheckpointBasePayload(
      *client, checkpoint_sequence, document_node_id,
      std::move(document_token));
  payload.Set("reason", std::move(reason));
  payload.Set("walkReason", std::move(walk_reason));
  payload.Set("maximumNodes", maximum_nodes);
  SendBlinkEvidence("browser.dom", "dom-checkpoint-started",
                    std::move(payload));
  return checkpoint_sequence;
}

void RecordBlinkDomCheckpointNode(uint64_t checkpoint_sequence,
                                  int document_node_id,
                                  std::string document_token,
                                  int node_index,
                                  int node_id,
                                  int parent_node_id,
                                  int node_type,
                                  std::string node_name) {
  A11Y_RECORDER_COST("RecordBlinkDomCheckpointNode");
  RecorderPipeClient* client = GetProcessRecorderClient();
  if (!client || checkpoint_sequence == 0 || document_node_id <= 0 ||
      document_token.empty() ||
      node_index < 0 || node_id <= 0 || node_name.empty()) {
    return;
  }
  base::DictValue payload = CreateDomCheckpointBasePayload(
      *client, checkpoint_sequence, document_node_id,
      std::move(document_token));
  payload.Set("nodeIndex", node_index);
  payload.Set("nodeId", node_id);
  if (parent_node_id > 0) {
    payload.Set("parentNodeId", parent_node_id);
  } else {
    payload.Set("parentNodeId", base::Value());
  }
  payload.Set("nodeType", DomNodeTypeName(node_type));
  payload.Set("nodeName", std::move(node_name));
  SendBlinkEvidence("browser.dom", "dom-checkpoint-node",
                    std::move(payload));
}

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
                                           int maximum_value_length) {
  A11Y_RECORDER_COST("RecordBlinkDomCheckpointNodeAttribute");
  RecorderPipeClient* client = GetProcessRecorderClient();
  if (!client || checkpoint_sequence == 0 || document_node_id <= 0 ||
      document_token.empty() || node_id <= 0 || attribute_index < 0 ||
      attribute_name.empty() || attribute_value_length < 0 ||
      maximum_value_length <= 0) {
    return;
  }
  base::DictValue payload = CreateDomCheckpointBasePayload(
      *client, checkpoint_sequence, document_node_id,
      std::move(document_token));
  payload.Set("nodeId", node_id);
  payload.Set("attributeIndex", attribute_index);
  if (attribute_namespace.empty()) {
    payload.Set("attributeNamespace", base::Value());
  } else {
    payload.Set("attributeNamespace", std::move(attribute_namespace));
  }
  payload.Set("attributeName", std::move(attribute_name));
  SetTruncatedTextProperties(payload, "attributeValue",
                             "attributeValueLength",
                             "attributeValueTruncated",
                             std::move(attribute_value),
                             attribute_value_length,
                             attribute_value_truncated);
  payload.Set("maximumValueLength", maximum_value_length);
  SendBlinkEvidence("browser.dom", "dom-checkpoint-node-attribute",
                    std::move(payload));
}

void RecordBlinkDomCheckpointNodeCharacterData(uint64_t checkpoint_sequence,
                                               int document_node_id,
                                               std::string document_token,
                                               int node_id,
                                               std::string data,
                                               int data_length,
                                               bool data_truncated,
                                               int maximum_value_length) {
  A11Y_RECORDER_COST("RecordBlinkDomCheckpointNodeCharacterData");
  RecorderPipeClient* client = GetProcessRecorderClient();
  if (!client || checkpoint_sequence == 0 || document_node_id <= 0 ||
      document_token.empty() || node_id <= 0 || data_length < 0 ||
      maximum_value_length <= 0) {
    return;
  }
  base::DictValue payload = CreateDomCheckpointBasePayload(
      *client, checkpoint_sequence, document_node_id,
      std::move(document_token));
  payload.Set("nodeId", node_id);
  SetTruncatedTextProperties(payload, "data", "dataLength", "dataTruncated",
                             std::move(data), data_length, data_truncated);
  payload.Set("maximumValueLength", maximum_value_length);
  SendBlinkEvidence("browser.dom", "dom-checkpoint-node-character-data",
                    std::move(payload));
}

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
                                        std::string reference_target) {
  A11Y_RECORDER_COST("RecordBlinkDomCheckpointShadowRoot");
  RecorderPipeClient* client = GetProcessRecorderClient();
  if (!client || checkpoint_sequence == 0 || document_node_id <= 0 ||
      document_token.empty() || node_id <= 0 || host_node_id <= 0 ||
      (mode != "open" && mode != "closed" && mode != "user-agent") ||
      (slot_assignment != "named" && slot_assignment != "manual")) {
    return;
  }
  base::DictValue payload = CreateDomCheckpointBasePayload(
      *client, checkpoint_sequence, document_node_id,
      std::move(document_token));
  payload.Set("nodeId", node_id);
  payload.Set("hostNodeId", host_node_id);
  payload.Set("mode", std::move(mode));
  payload.Set("delegatesFocus", delegates_focus);
  payload.Set("slotAssignment", std::move(slot_assignment));
  payload.Set("clonable", clonable);
  payload.Set("serializable", serializable);
  payload.Set("declarative", declarative);
  payload.Set("availableToElementInternals", available_to_element_internals);
  payload.Set("referenceTarget", reference_target_present
                                     ? base::Value(std::move(reference_target))
                                     : base::Value());
  SendBlinkEvidence("browser.dom", "dom-checkpoint-shadow-root",
                    std::move(payload));
}

void RecordBlinkDomCheckpointSlotAssignment(uint64_t checkpoint_sequence,
                                            int document_node_id,
                                            std::string document_token,
                                            int node_id,
                                            std::vector<int> assigned_node_ids,
                                            int assigned_node_count,
                                            int maximum_assigned_nodes,
                                            bool assignment_current) {
  A11Y_RECORDER_COST("RecordBlinkDomCheckpointSlotAssignment");
  RecorderPipeClient* client = GetProcessRecorderClient();
  if (!client || checkpoint_sequence == 0 || document_node_id <= 0 ||
      document_token.empty() || node_id <= 0 || assigned_node_count < 0 ||
      maximum_assigned_nodes <= 0 ||
      static_cast<int>(assigned_node_ids.size()) > assigned_node_count ||
      static_cast<int>(assigned_node_ids.size()) > maximum_assigned_nodes) {
    return;
  }
  base::DictValue payload = CreateDomCheckpointBasePayload(
      *client, checkpoint_sequence, document_node_id,
      std::move(document_token));
  payload.Set("nodeId", node_id);
  base::ListValue assigned;
  for (int assigned_node_id : assigned_node_ids) {
    if (assigned_node_id > 0) {
      assigned.Append(assigned_node_id);
    } else {
      assigned.Append(base::Value());
    }
  }
  payload.Set("assignedNodeIds", std::move(assigned));
  payload.Set("assignedNodeCount", assigned_node_count);
  payload.Set("assignedNodesTruncated",
              static_cast<int>(assigned_node_ids.size()) <
                  assigned_node_count);
  payload.Set("maximumAssignedNodes", maximum_assigned_nodes);
  payload.Set("assignmentCurrent", assignment_current);
  SendBlinkEvidence("browser.dom", "dom-checkpoint-slot-assignment",
                    std::move(payload));
}

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
                                int character_data_count) {
  static const int recorder_span_slot = CostSpanSlot("span:dom-checkpoint");
  // The span ends when this function returns, so it includes the completion.
  struct SpanEnd {
    ~SpanEnd() { EndCostSpan(recorder_span_slot); }
  } recorder_span_end;
  A11Y_RECORDER_COST("CompleteBlinkDomCheckpoint");
  RecorderPipeClient* client = GetProcessRecorderClient();
  if (!client || checkpoint_sequence == 0 || document_node_id <= 0 ||
      document_token.empty() ||
      reason.empty() || node_count < 0 || maximum_nodes <= 0 ||
      attribute_count < 0 || maximum_attributes_per_node <= 0 ||
      maximum_value_length <= 0 || shadow_root_count < 0 ||
      slot_count < 0 || character_data_count < 0) {
    return;
  }
  base::DictValue payload = CreateDomCheckpointBasePayload(
      *client, checkpoint_sequence, document_node_id,
      std::move(document_token));
  payload.Set("reason", std::move(reason));
  payload.Set("nodeCount", node_count);
  payload.Set("truncated", truncated);
  payload.Set("maximumNodes", maximum_nodes);
  payload.Set("attributeCount", attribute_count);
  payload.Set("attributesTruncated", attributes_truncated);
  payload.Set("maximumAttributesPerNode", maximum_attributes_per_node);
  payload.Set("maximumValueLength", maximum_value_length);
  payload.Set("shadowRootCount", shadow_root_count);
  payload.Set("slotCount", slot_count);
  payload.Set("characterDataCount", character_data_count);
  const EvidenceIdentityStorage::DomTransitionCoverage coverage =
      TakeDomTransitionCoverage(document_node_id);
  payload.Set("coveredTransitionCount", coverage.transition_count);
  if (coverage.transition_count == 0) {
    payload.Set("coveredTransitionFirstId", base::Value());
    payload.Set("coveredTransitionLastId", base::Value());
  } else {
    payload.Set("coveredTransitionFirstId",
                DomTransitionId(coverage.first_transition_id));
    payload.Set("coveredTransitionLastId",
                DomTransitionId(coverage.last_transition_id));
  }
  SendBlinkEvidence("browser.dom", "dom-checkpoint-completed",
                    std::move(payload));
}

uint64_t BeginRendererAccessibilityCheckpoint(
    std::string document_token,
    std::string reason,
    int maximum_nodes,
    int update_count,
    int event_count) {
  static const int recorder_span_slot = CostSpanSlot("span:accessibility-checkpoint");
  BeginCostSpan(recorder_span_slot);
  A11Y_RECORDER_COST("BeginRendererAccessibilityCheckpoint");
  RecorderPipeClient* client = GetProcessRecorderClient();
  if (!client || document_token.empty() || reason.empty() ||
      maximum_nodes <= 0 || update_count < 0 || event_count < 0) {
    return 0;
  }
  const uint64_t checkpoint_sequence =
      AssignAccessibilityCheckpointIdentity();
  base::DictValue payload = CreateAccessibilityCheckpointBasePayload(
      *client, checkpoint_sequence, std::move(document_token));
  payload.Set("reason", std::move(reason));
  payload.Set("maximumNodes", maximum_nodes);
  payload.Set("updateCount", update_count);
  payload.Set("eventCount", event_count);
  SendBlinkEvidence("browser.accessibility",
                    "accessibility-checkpoint-started",
                    std::move(payload));
  return checkpoint_sequence;
}

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
    bool focused) {
  A11Y_RECORDER_COST("RecordRendererAccessibilityCheckpointNode");
  RecorderPipeClient* client = GetProcessRecorderClient();
  if (!client || checkpoint_sequence == 0 || document_token.empty() ||
      node_index < 0 || accessibility_node_id == 0 || role < 0 ||
      role_name.empty()) {
    return;
  }
  base::DictValue payload = CreateAccessibilityCheckpointBasePayload(
      *client, checkpoint_sequence, std::move(document_token));
  payload.Set("nodeIndex", node_index);
  payload.Set("accessibilityNodeId", accessibility_node_id);
  if (parent_accessibility_node_id != 0) {
    payload.Set("parentAccessibilityNodeId",
                parent_accessibility_node_id);
  } else {
    payload.Set("parentAccessibilityNodeId", base::Value());
  }
  if (dom_node_id > 0) {
    payload.Set("domNodeId", dom_node_id);
  } else {
    payload.Set("domNodeId", base::Value());
  }
  payload.Set("role", role);
  payload.Set("roleName", std::move(role_name));
  payload.Set("name", std::move(name));
  payload.Set("description", std::move(description));
  payload.Set("serializedProperties", std::move(serialized_properties));
  payload.Set("focused", focused);
  SendBlinkEvidence("browser.accessibility",
                    "accessibility-checkpoint-node",
                    std::move(payload));
}

void CompleteRendererAccessibilityCheckpoint(
    uint64_t checkpoint_sequence,
    std::string document_token,
    std::string reason,
    int node_count,
    bool truncated,
    int maximum_nodes,
    int update_count,
    int event_count) {
  static const int recorder_span_slot = CostSpanSlot("span:accessibility-checkpoint");
  // The span ends when this function returns, so it includes the completion.
  struct SpanEnd {
    ~SpanEnd() { EndCostSpan(recorder_span_slot); }
  } recorder_span_end;
  A11Y_RECORDER_COST("CompleteRendererAccessibilityCheckpoint");
  RecorderPipeClient* client = GetProcessRecorderClient();
  if (!client || checkpoint_sequence == 0 || document_token.empty() ||
      reason.empty() || node_count < 0 || maximum_nodes <= 0 ||
      update_count < 0 || event_count < 0) {
    return;
  }
  base::DictValue payload = CreateAccessibilityCheckpointBasePayload(
      *client, checkpoint_sequence, std::move(document_token));
  payload.Set("reason", std::move(reason));
  payload.Set("nodeCount", node_count);
  payload.Set("truncated", truncated);
  payload.Set("maximumNodes", maximum_nodes);
  payload.Set("updateCount", update_count);
  payload.Set("eventCount", event_count);
  SendBlinkEvidence("browser.accessibility",
                    "accessibility-checkpoint-completed",
                    std::move(payload));
}

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
                                    int maximum_value_length) {
  A11Y_RECORDER_COST("RecordBlinkDomAttributeChanged");
  RecorderPipeClient* client = GetProcessRecorderClient();
  if (!client || document_node_id <= 0 || document_token.empty() ||
      node_id <= 0 || node_name.empty() || attribute_name.empty() ||
      change_type < 0 || change_type > 2 || attribute_value_length < 0 ||
      previous_attribute_value_length < 0 || maximum_value_length <= 0) {
    return;
  }
  base::DictValue payload = CreateDomStateChangeBasePayload(
      *client, document_node_id, std::move(document_token));
  payload.Set("nodeId", node_id);
  payload.Set("nodeName", std::move(node_name));
  if (attribute_namespace.empty()) {
    payload.Set("attributeNamespace", base::Value());
  } else {
    payload.Set("attributeNamespace", std::move(attribute_namespace));
  }
  payload.Set("attributeName", std::move(attribute_name));
  payload.Set("changeType", DomAttributeChangeTypeName(change_type));
  // A removed attribute has no current value and an added attribute has no
  // previous value. The change type decides which side is absent, so the
  // record cannot contradict itself.
  const bool has_value = change_type != 1;
  const bool has_previous_value = change_type != 0;
  if (has_value) {
    SetTruncatedTextProperties(payload, "attributeValue",
                               "attributeValueLength",
                               "attributeValueTruncated",
                               std::move(attribute_value),
                               attribute_value_length,
                               attribute_value_truncated);
  } else {
    payload.Set("attributeValue", base::Value());
    payload.Set("attributeValueLength", base::Value());
    payload.Set("attributeValueTruncated", false);
  }
  if (has_previous_value) {
    SetTruncatedTextProperties(payload, "previousAttributeValue",
                               "previousAttributeValueLength",
                               "previousAttributeValueTruncated",
                               std::move(previous_attribute_value),
                               previous_attribute_value_length,
                               previous_attribute_value_truncated);
  } else {
    payload.Set("previousAttributeValue", base::Value());
    payload.Set("previousAttributeValueLength", base::Value());
    payload.Set("previousAttributeValueTruncated", false);
  }
  payload.Set("maximumValueLength", maximum_value_length);
  SendBlinkEvidence("browser.dom", "dom-attribute-changed",
                    std::move(payload));
}

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
                                        int maximum_value_length) {
  A11Y_RECORDER_COST("RecordBlinkDomCharacterDataChanged");
  RecorderPipeClient* client = GetProcessRecorderClient();
  if (!client || document_node_id <= 0 || document_token.empty() ||
      node_id <= 0 || text_length < 0 || previous_text_length < 0 ||
      maximum_value_length <= 0) {
    return;
  }
  base::DictValue payload = CreateDomStateChangeBasePayload(
      *client, document_node_id, std::move(document_token));
  payload.Set("nodeId", node_id);
  if (parent_node_id > 0) {
    payload.Set("parentNodeId", parent_node_id);
  } else {
    payload.Set("parentNodeId", base::Value());
  }
  payload.Set("nodeType", DomNodeTypeName(node_type));
  SetTruncatedTextProperties(payload, "text", "textLength", "textTruncated",
                             std::move(text), text_length, text_truncated);
  SetTruncatedTextProperties(payload, "previousText", "previousTextLength",
                             "previousTextTruncated",
                             std::move(previous_text), previous_text_length,
                             previous_text_truncated);
  payload.Set("maximumValueLength", maximum_value_length);
  SendBlinkEvidence("browser.dom", "dom-character-data-changed",
                    std::move(payload));
}

namespace {

// The base of a record of an inserted subtree: its context and the
// transition identity of the insertion it belongs to.
base::DictValue CreateDomInsertionBasePayload(const RecorderPipeClient& client,
                                              uint64_t insertion_sequence,
                                              int document_node_id,
                                              std::string document_token) {
  base::DictValue payload;
  payload.Set("context",
              CreateContext(client, document_node_id,
                            std::move(document_token)));
  payload.Set("insertionId", DomTransitionId(insertion_sequence));
  return payload;
}

bool IsValidShadowRootFields(const std::string& mode,
                             const std::string& slot_assignment) {
  return (mode == "open" || mode == "closed" || mode == "user-agent") &&
         (slot_assignment == "named" || slot_assignment == "manual");
}

void SetShadowRootFields(base::DictValue& payload,
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
                         std::string reference_target) {
  payload.Set("nodeId", node_id);
  payload.Set("hostNodeId", host_node_id);
  payload.Set("mode", std::move(mode));
  payload.Set("delegatesFocus", delegates_focus);
  payload.Set("slotAssignment", std::move(slot_assignment));
  payload.Set("clonable", clonable);
  payload.Set("serializable", serializable);
  payload.Set("declarative", declarative);
  payload.Set("availableToElementInternals", available_to_element_internals);
  payload.Set("referenceTarget", reference_target_present
                                     ? base::Value(std::move(reference_target))
                                     : base::Value());
}

base::ListValue AssignedNodeIdList(const std::vector<int>& assigned_node_ids) {
  base::ListValue assigned;
  for (int assigned_node_id : assigned_node_ids) {
    if (assigned_node_id > 0) {
      assigned.Append(assigned_node_id);
    } else {
      assigned.Append(base::Value());
    }
  }
  return assigned;
}

}  // namespace

uint64_t RecordBlinkDomNodeInserted(int document_node_id,
                                    std::string document_token,
                                    std::string insertion_kind,
                                    int container_node_id,
                                    int node_id,
                                    int previous_sibling_node_id) {
  A11Y_RECORDER_COST("RecordBlinkDomNodeInserted");
  RecorderPipeClient* client = GetProcessRecorderClient();
  if (!client || document_node_id <= 0 || document_token.empty() ||
      (insertion_kind != "child" && insertion_kind != "shadow-root") ||
      container_node_id <= 0 || node_id <= 0 ||
      previous_sibling_node_id < 0 ||
      (insertion_kind == "shadow-root" && previous_sibling_node_id != 0)) {
    return 0;
  }
  base::DictValue payload;
  payload.Set("context",
              CreateContext(*client, document_node_id,
                            std::move(document_token)));
  const uint64_t insertion_sequence =
      AssignDomTransitionIdentity(document_node_id);
  payload.Set("transitionId", DomTransitionId(insertion_sequence));
  payload.Set("insertionKind", std::move(insertion_kind));
  payload.Set("containerNodeId", container_node_id);
  payload.Set("nodeId", node_id);
  payload.Set("previousSiblingNodeId", previous_sibling_node_id > 0
                                           ? base::Value(previous_sibling_node_id)
                                           : base::Value());
  SendBlinkEvidence("browser.dom", "dom-node-inserted", std::move(payload));
  return insertion_sequence;
}

void RecordBlinkDomInsertedNode(uint64_t insertion_sequence,
                                int document_node_id,
                                std::string document_token,
                                int node_index,
                                int node_id,
                                int parent_node_id,
                                int node_type,
                                std::string node_name) {
  A11Y_RECORDER_COST("RecordBlinkDomInsertedNode");
  RecorderPipeClient* client = GetProcessRecorderClient();
  if (!client || insertion_sequence == 0 || document_node_id <= 0 ||
      document_token.empty() || node_index < 0 || node_id <= 0 ||
      parent_node_id <= 0 || node_name.empty()) {
    return;
  }
  base::DictValue payload = CreateDomInsertionBasePayload(
      *client, insertion_sequence, document_node_id, std::move(document_token));
  payload.Set("nodeIndex", node_index);
  payload.Set("nodeId", node_id);
  payload.Set("parentNodeId", parent_node_id);
  payload.Set("nodeType", DomNodeTypeName(node_type));
  payload.Set("nodeName", std::move(node_name));
  SendBlinkEvidence("browser.dom", "dom-inserted-node", std::move(payload));
}

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
                                         int maximum_value_length) {
  A11Y_RECORDER_COST("RecordBlinkDomInsertedNodeAttribute");
  RecorderPipeClient* client = GetProcessRecorderClient();
  if (!client || insertion_sequence == 0 || document_node_id <= 0 ||
      document_token.empty() || node_id <= 0 || attribute_index < 0 ||
      attribute_name.empty() || attribute_value_length < 0 ||
      maximum_value_length <= 0) {
    return;
  }
  base::DictValue payload = CreateDomInsertionBasePayload(
      *client, insertion_sequence, document_node_id, std::move(document_token));
  payload.Set("nodeId", node_id);
  payload.Set("attributeIndex", attribute_index);
  if (attribute_namespace.empty()) {
    payload.Set("attributeNamespace", base::Value());
  } else {
    payload.Set("attributeNamespace", std::move(attribute_namespace));
  }
  payload.Set("attributeName", std::move(attribute_name));
  SetTruncatedTextProperties(payload, "attributeValue", "attributeValueLength",
                             "attributeValueTruncated",
                             std::move(attribute_value),
                             attribute_value_length,
                             attribute_value_truncated);
  payload.Set("maximumValueLength", maximum_value_length);
  SendBlinkEvidence("browser.dom", "dom-inserted-node-attribute",
                    std::move(payload));
}

void RecordBlinkDomInsertedNodeCharacterData(uint64_t insertion_sequence,
                                             int document_node_id,
                                             std::string document_token,
                                             int node_id,
                                             std::string data,
                                             int data_length,
                                             bool data_truncated,
                                             int maximum_value_length) {
  A11Y_RECORDER_COST("RecordBlinkDomInsertedNodeCharacterData");
  RecorderPipeClient* client = GetProcessRecorderClient();
  if (!client || insertion_sequence == 0 || document_node_id <= 0 ||
      document_token.empty() || node_id <= 0 || data_length < 0 ||
      maximum_value_length <= 0) {
    return;
  }
  base::DictValue payload = CreateDomInsertionBasePayload(
      *client, insertion_sequence, document_node_id, std::move(document_token));
  payload.Set("nodeId", node_id);
  SetTruncatedTextProperties(payload, "data", "dataLength", "dataTruncated",
                             std::move(data), data_length, data_truncated);
  payload.Set("maximumValueLength", maximum_value_length);
  SendBlinkEvidence("browser.dom", "dom-inserted-node-character-data",
                    std::move(payload));
}

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
                                      std::string reference_target) {
  A11Y_RECORDER_COST("RecordBlinkDomInsertedShadowRoot");
  RecorderPipeClient* client = GetProcessRecorderClient();
  if (!client || insertion_sequence == 0 || document_node_id <= 0 ||
      document_token.empty() || node_id <= 0 || host_node_id <= 0 ||
      !IsValidShadowRootFields(mode, slot_assignment)) {
    return;
  }
  base::DictValue payload = CreateDomInsertionBasePayload(
      *client, insertion_sequence, document_node_id, std::move(document_token));
  SetShadowRootFields(payload, node_id, host_node_id, std::move(mode),
                      delegates_focus, std::move(slot_assignment), clonable,
                      serializable, declarative,
                      available_to_element_internals, reference_target_present,
                      std::move(reference_target));
  SendBlinkEvidence("browser.dom", "dom-inserted-shadow-root",
                    std::move(payload));
}

void RecordBlinkDomInsertedSlotAssignment(uint64_t insertion_sequence,
                                          int document_node_id,
                                          std::string document_token,
                                          int node_id,
                                          std::vector<int> assigned_node_ids,
                                          int assigned_node_count,
                                          int maximum_assigned_nodes,
                                          bool assignment_current) {
  A11Y_RECORDER_COST("RecordBlinkDomInsertedSlotAssignment");
  RecorderPipeClient* client = GetProcessRecorderClient();
  if (!client || insertion_sequence == 0 || document_node_id <= 0 ||
      document_token.empty() || node_id <= 0 || assigned_node_count < 0 ||
      maximum_assigned_nodes <= 0 ||
      static_cast<int>(assigned_node_ids.size()) > assigned_node_count ||
      static_cast<int>(assigned_node_ids.size()) > maximum_assigned_nodes) {
    return;
  }
  base::DictValue payload = CreateDomInsertionBasePayload(
      *client, insertion_sequence, document_node_id, std::move(document_token));
  payload.Set("nodeId", node_id);
  const bool truncated =
      static_cast<int>(assigned_node_ids.size()) < assigned_node_count;
  payload.Set("assignedNodeIds", AssignedNodeIdList(assigned_node_ids));
  payload.Set("assignedNodeCount", assigned_node_count);
  payload.Set("assignedNodesTruncated", truncated);
  payload.Set("maximumAssignedNodes", maximum_assigned_nodes);
  payload.Set("assignmentCurrent", assignment_current);
  SendBlinkEvidence("browser.dom", "dom-inserted-slot-assignment",
                    std::move(payload));
}

void CompleteBlinkDomInsertion(uint64_t insertion_sequence,
                               int document_node_id,
                               std::string document_token,
                               int node_count,
                               int attribute_count,
                               int character_data_count,
                               int shadow_root_count,
                               int slot_count) {
  A11Y_RECORDER_COST("CompleteBlinkDomInsertion");
  RecorderPipeClient* client = GetProcessRecorderClient();
  if (!client || insertion_sequence == 0 || document_node_id <= 0 ||
      document_token.empty() || node_count <= 0 || attribute_count < 0 ||
      character_data_count < 0 || shadow_root_count < 0 || slot_count < 0) {
    return;
  }
  base::DictValue payload = CreateDomInsertionBasePayload(
      *client, insertion_sequence, document_node_id, std::move(document_token));
  payload.Set("nodeCount", node_count);
  payload.Set("attributeCount", attribute_count);
  payload.Set("characterDataCount", character_data_count);
  payload.Set("shadowRootCount", shadow_root_count);
  payload.Set("slotCount", slot_count);
  SendBlinkEvidence("browser.dom", "dom-insertion-completed",
                    std::move(payload));
}

void RecordBlinkDomNodeRemoved(int document_node_id,
                               std::string document_token,
                               int container_node_id,
                               int node_id) {
  A11Y_RECORDER_COST("RecordBlinkDomNodeRemoved");
  RecorderPipeClient* client = GetProcessRecorderClient();
  if (!client || document_node_id <= 0 || document_token.empty() ||
      container_node_id <= 0 || node_id <= 0) {
    return;
  }
  base::DictValue payload = CreateDomStateChangeBasePayload(
      *client, document_node_id, std::move(document_token));
  payload.Set("containerNodeId", container_node_id);
  payload.Set("nodeId", node_id);
  SendBlinkEvidence("browser.dom", "dom-node-removed", std::move(payload));
}

void RecordBlinkDomChildrenRemoved(int document_node_id,
                                   std::string document_token,
                                   int container_node_id) {
  A11Y_RECORDER_COST("RecordBlinkDomChildrenRemoved");
  RecorderPipeClient* client = GetProcessRecorderClient();
  if (!client || document_node_id <= 0 || document_token.empty() ||
      container_node_id <= 0) {
    return;
  }
  base::DictValue payload = CreateDomStateChangeBasePayload(
      *client, document_node_id, std::move(document_token));
  payload.Set("containerNodeId", container_node_id);
  SendBlinkEvidence("browser.dom", "dom-children-removed", std::move(payload));
}

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
                                     std::string reference_target) {
  A11Y_RECORDER_COST("RecordBlinkDomShadowRootChanged");
  RecorderPipeClient* client = GetProcessRecorderClient();
  if (!client || document_node_id <= 0 || document_token.empty() ||
      node_id <= 0 || host_node_id <= 0 ||
      !IsValidShadowRootFields(mode, slot_assignment)) {
    return;
  }
  base::DictValue payload = CreateDomStateChangeBasePayload(
      *client, document_node_id, std::move(document_token));
  SetShadowRootFields(payload, node_id, host_node_id, std::move(mode),
                      delegates_focus, std::move(slot_assignment), clonable,
                      serializable, declarative,
                      available_to_element_internals, reference_target_present,
                      std::move(reference_target));
  SendBlinkEvidence("browser.dom", "dom-shadow-root-changed",
                    std::move(payload));
}

void RecordBlinkDomSlotAssignmentChanged(int document_node_id,
                                         std::string document_token,
                                         int node_id,
                                         std::vector<int> assigned_node_ids,
                                         int assigned_node_count,
                                         int maximum_assigned_nodes) {
  A11Y_RECORDER_COST("RecordBlinkDomSlotAssignmentChanged");
  RecorderPipeClient* client = GetProcessRecorderClient();
  if (!client || document_node_id <= 0 || document_token.empty() ||
      node_id <= 0 || assigned_node_count < 0 || maximum_assigned_nodes <= 0 ||
      static_cast<int>(assigned_node_ids.size()) > assigned_node_count ||
      static_cast<int>(assigned_node_ids.size()) > maximum_assigned_nodes) {
    return;
  }
  base::DictValue payload = CreateDomStateChangeBasePayload(
      *client, document_node_id, std::move(document_token));
  payload.Set("nodeId", node_id);
  const bool truncated =
      static_cast<int>(assigned_node_ids.size()) < assigned_node_count;
  payload.Set("assignedNodeIds", AssignedNodeIdList(assigned_node_ids));
  payload.Set("assignedNodeCount", assigned_node_count);
  payload.Set("assignedNodesTruncated", truncated);
  payload.Set("maximumAssignedNodes", maximum_assigned_nodes);
  SendBlinkEvidence("browser.dom", "dom-slot-assignment-changed",
                    std::move(payload));
}

void RecordBlinkSchedulerWakeUpDeferred(
    int queue_type,
    int throttling_type,
    int64_t desired_wake_up_microseconds,
    int64_t allowed_wake_up_microseconds,
    bool has_ready_task,
    int block_type) {
  A11Y_RECORDER_COST("RecordBlinkSchedulerWakeUpDeferred");
  RecorderPipeClient* client = GetProcessRecorderClient();
  if (!client || desired_wake_up_microseconds < 0 ||
      allowed_wake_up_microseconds <= desired_wake_up_microseconds ||
      throttling_type < 1 || throttling_type > 3 ||
      block_type < 0 || block_type > 1) {
    return;
  }

  base::DictValue payload;
  payload.Set("context", CreateContext(*client, 0));
  payload.Set("queueName", SchedulerQueueName(queue_type));
  payload.Set("queueType", queue_type);
  payload.Set("throttlingType",
              SchedulerThrottlingTypeName(throttling_type));
  payload.Set("desiredWakeUpTicks",
              base::NumberToString(desired_wake_up_microseconds));
  payload.Set("allowedWakeUpTicks",
              base::NumberToString(allowed_wake_up_microseconds));
  payload.Set(
      "deferralMilliseconds",
      static_cast<double>(allowed_wake_up_microseconds -
                          desired_wake_up_microseconds) /
          1000.0);
  payload.Set("hasReadyTask", has_ready_task);
  payload.Set("blockType", SchedulerBlockTypeName(block_type));
  payload.Set("decisionBoundary", "task-queue-throttler");
  SendBlinkEvidence("browser.scheduler", "wake-up-deferred",
                    std::move(payload));
}

void RecordBrowserNavigationStarted(int64_t navigation_id,
                                    int page_frame_tree_node_id,
                                    int frame_tree_node_id,
                                    int parent_frame_tree_node_id,
                                    int parent_or_outer_document_frame_tree_node_id,
                                    std::string frame_type,
                                    bool primary_page,
                                    std::string url,
                                    bool renderer_initiated,
                                    bool same_document) {
  A11Y_RECORDER_COST("RecordBrowserNavigationStarted");
  RecorderPipeClient* client = GetProcessRecorderClient();
  if (!client || navigation_id <= 0 || page_frame_tree_node_id < 0 ||
      frame_tree_node_id < 0 || url.empty()) {
    return;
  }

  base::DictValue payload = CreateNavigationPayload(
      *client, navigation_id, page_frame_tree_node_id, frame_tree_node_id,
      parent_frame_tree_node_id, parent_or_outer_document_frame_tree_node_id,
      std::move(frame_type), primary_page, 0, std::string(), 0,
      std::move(url),
      renderer_initiated, same_document);
  payload.Set("committed", base::Value());
  payload.Set("errorPage", base::Value());
  payload.Set("netErrorCode", base::Value());
  payload.Set("outcome", base::Value());
  SendBlinkEvidence("browser.navigation", "navigation-started",
                    std::move(payload));
}

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
                                      int net_error_code) {
  A11Y_RECORDER_COST("RecordBrowserNavigationCompleted");
  RecorderPipeClient* client = GetProcessRecorderClient();
  if (!client || navigation_id <= 0 || page_frame_tree_node_id < 0 ||
      frame_tree_node_id < 0 || url.empty()) {
    return;
  }

  base::DictValue payload = CreateNavigationPayload(
      *client, navigation_id, page_frame_tree_node_id, frame_tree_node_id,
      parent_frame_tree_node_id, parent_or_outer_document_frame_tree_node_id,
      std::move(frame_type), primary_page,
      committed ? document_navigation_id : 0,
      committed ? std::move(document_token) : std::string(),
      committed ? renderer_process_id : 0, std::move(url),
      renderer_initiated, same_document);
  payload.Set("committed", committed);
  payload.Set("errorPage", error_page);
  payload.Set("netErrorCode", net_error_code);
  payload.Set("outcome", !committed
                             ? "not-committed"
                             : error_page ? "committed-error-page"
                                          : "committed");
  SendBlinkEvidence("browser.navigation", "navigation-completed",
                    std::move(payload));
}

void RecordBlinkDispatchCompleted(uintptr_t event_identity,
                                  int dispatch_result,
                                  bool default_prevented,
                                  bool propagation_stopped,
                                  bool immediate_propagation_stopped) {
  A11Y_RECORDER_COST("RecordBlinkDispatchCompleted");
  RecorderPipeClient* client = GetProcessRecorderClient();
  std::optional<EvidenceIdentityStorage::DispatchState> state =
      TakeDispatchIdentity(event_identity);
  if (!client || !state) {
    return;
  }

  base::DictValue payload = CreateDispatchPayload(
      *client, *state, std::nullopt, "none",
      default_prevented || state->observed_default_prevented,
      propagation_stopped || state->observed_propagation_stopped,
      immediate_propagation_stopped ||
          state->observed_immediate_propagation_stopped,
      DispatchOutcomeName(dispatch_result));
  SendBlinkEvidence("browser.dispatch", "dispatch-completed",
                    std::move(payload));
}

std::vector<std::string> ReadCookieNamesFromCookieString(
    std::string_view cookie_string) {
  return cookie_text::NamesFromCookieString(cookie_string);
}

CookieWriteRequest ReadCookieWriteRequest(std::string_view cookie_line) {
  cookie_text::CookieWriteText text = cookie_text::ParseCookieWrite(cookie_line);
  CookieWriteRequest request;
  request.name = std::move(text.name);
  request.domain_present = text.domain.has_value();
  request.domain = text.domain.value_or(std::string());
  request.path_present = text.path.has_value();
  request.path = text.path.value_or(std::string());
  request.same_site_present = text.same_site.has_value();
  request.same_site = text.same_site.value_or(std::string());
  request.secure = text.secure;
  request.http_only = text.http_only;
  request.partitioned = text.partitioned;
  request.expires_present = text.expires_present;
  request.max_age_present = text.max_age_present;
  request.attribute_names = std::move(text.attribute_names);
  return request;
}

std::string ReadCookieNameFromSetCookieLine(std::string_view cookie_line) {
  return cookie_text::ParseCookieWrite(cookie_line).name;
}

void RecordBlinkDocumentCookieRead(int document_node_id,
                                   std::string document_token,
                                   std::string cookie_url,
                                   std::string outcome,
                                   std::string served_from,
                                   std::vector<std::string> cookie_names,
                                   CookieCallOrigin origin) {
  A11Y_RECORDER_COST("RecordBlinkDocumentCookieRead");
  RecorderPipeClient* client = GetProcessRecorderClient();
  if (!client) {
    return;
  }
  base::DictValue payload;
  payload.Set("context",
              CreateCookieRendererContext(*client, document_node_id,
                                          std::move(document_token), origin));
  payload.Set("accessId", AssignCookieAccessId());
  payload.Set("cookieUrl", NonEmptyString(std::move(cookie_url)));
  payload.Set("outcome", std::move(outcome));
  payload.Set("servedFrom", NonEmptyString(std::move(served_from)));
  SetCookieNames(payload, std::move(cookie_names));
  SetCookieCallOrigin(payload, std::move(origin));
  SendBlinkEvidence("browser.cookie", "document-cookie-read",
                    std::move(payload));
}

void RecordBlinkDocumentCookieWrite(int document_node_id,
                                    std::string document_token,
                                    std::string cookie_url,
                                    std::string outcome,
                                    CookieWriteRequest request,
                                    CookieCallOrigin origin) {
  A11Y_RECORDER_COST("RecordBlinkDocumentCookieWrite");
  RecorderPipeClient* client = GetProcessRecorderClient();
  if (!client) {
    return;
  }
  base::DictValue payload;
  payload.Set("context",
              CreateCookieRendererContext(*client, document_node_id,
                                          std::move(document_token), origin));
  payload.Set("accessId", AssignCookieAccessId());
  payload.Set("cookieUrl", NonEmptyString(std::move(cookie_url)));
  payload.Set("outcome", std::move(outcome));
  payload.Set("name", request.name);
  payload.Set("attributes",
              CreateCookieWriteAttributes(std::move(request), true));
  SetCookieCallOrigin(payload, std::move(origin));
  SendBlinkEvidence("browser.cookie", "document-cookie-write",
                    std::move(payload));
}

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
                                CookieCallOrigin origin) {
  A11Y_RECORDER_COST("RecordBlinkCookieStoreRead");
  RecorderPipeClient* client = GetProcessRecorderClient();
  if (!client) {
    return;
  }
  const bool sent = outcome == kCookieOutcomeSentToCookieManager;
  base::DictValue payload;
  payload.Set("context",
              CreateCookieRendererContext(*client, document_node_id,
                                          std::move(document_token), origin));
  payload.Set("requestId",
              AssignCookieStoreRequestId(sent ? resolver_identity : 0,
                                         method));
  payload.Set("method", std::move(method));
  payload.Set("contextKind", NormalizeCookieContextKind(std::move(context_kind)));
  payload.Set("outcome", std::move(outcome));
  payload.Set("name", OptionalString(name_present, std::move(name)));
  payload.Set("url", OptionalString(url_present, std::move(url)));
  payload.Set("attributes", base::Value());
  SetCookieCallOrigin(payload, std::move(origin));
  SendBlinkEvidence("browser.cookie", "cookie-store-request",
                    std::move(payload));
}

void NoteBlinkCookieStoreWriteResolver(uintptr_t resolver_identity) {
  A11Y_RECORDER_COST("NoteBlinkCookieStoreWriteResolver");
  g_pending_cookie_store_write_resolver = resolver_identity;
}

void RecordBlinkCookieStoreWrite(std::string method,
                                 std::string context_kind,
                                 int document_node_id,
                                 std::string document_token,
                                 bool threw,
                                 CookieWriteRequest request,
                                 CookieCallOrigin origin) {
  A11Y_RECORDER_COST("RecordBlinkCookieStoreWrite");
  const uintptr_t resolver_identity = g_pending_cookie_store_write_resolver;
  g_pending_cookie_store_write_resolver = 0;
  RecorderPipeClient* client = GetProcessRecorderClient();
  if (!client) {
    return;
  }
  // A write that threw was never sent, so any resolver noted on this thread
  // belongs to no request and is not kept.
  const bool sent = !threw && resolver_identity != 0;
  base::DictValue payload;
  payload.Set("context",
              CreateCookieRendererContext(*client, document_node_id,
                                          std::move(document_token), origin));
  payload.Set("requestId",
              AssignCookieStoreRequestId(sent ? resolver_identity : 0,
                                         method));
  payload.Set("method", std::move(method));
  payload.Set("contextKind", NormalizeCookieContextKind(std::move(context_kind)));
  payload.Set("outcome", sent ? kCookieOutcomeSentToCookieManager
                              : kCookieOutcomeThrew);
  payload.Set("name", request.name);
  payload.Set("url", base::Value());
  payload.Set("attributes",
              CreateCookieWriteAttributes(std::move(request), false));
  SetCookieCallOrigin(payload, std::move(origin));
  SendBlinkEvidence("browser.cookie", "cookie-store-request",
                    std::move(payload));
}

void RecordBlinkCookieStoreReadResult(uintptr_t resolver_identity,
                                      bool context_valid,
                                      std::vector<std::string> cookie_names) {
  A11Y_RECORDER_COST("RecordBlinkCookieStoreReadResult");
  RecorderPipeClient* client = GetProcessRecorderClient();
  std::optional<CookieStoreRequestState> request =
      TakeCookieStoreRequest(resolver_identity);
  if (!client || !request) {
    return;
  }
  base::DictValue payload;
  payload.Set("context", CreateContext(*client, 0));
  payload.Set("requestId", std::move(request->request_id));
  payload.Set("method", std::move(request->method));
  payload.Set("outcome", context_valid ? "resolved" : "context-destroyed");
  payload.Set("success", base::Value());
  SetCookieNames(payload, std::move(cookie_names));
  SendBlinkEvidence("browser.cookie", "cookie-store-result",
                    std::move(payload));
}

void RecordBlinkCookieStoreWriteResult(uintptr_t resolver_identity,
                                       bool success) {
  A11Y_RECORDER_COST("RecordBlinkCookieStoreWriteResult");
  RecorderPipeClient* client = GetProcessRecorderClient();
  std::optional<CookieStoreRequestState> request =
      TakeCookieStoreRequest(resolver_identity);
  if (!client || !request) {
    return;
  }
  base::DictValue payload;
  payload.Set("context", CreateContext(*client, 0));
  payload.Set("requestId", std::move(request->request_id));
  payload.Set("method", std::move(request->method));
  payload.Set("outcome", success ? "resolved" : "rejected");
  payload.Set("success", success);
  payload.Set("cookieCount", base::Value());
  payload.Set("cookieNames", base::Value());
  payload.Set("cookieNamesTruncated", base::Value());
  SendBlinkEvidence("browser.cookie", "cookie-store-result",
                    std::move(payload));
}

void RecordBlinkCookieStoreChange(std::string context_kind,
                                  int document_node_id,
                                  std::string document_token,
                                  std::string name,
                                  std::string domain,
                                  std::string path,
                                  std::string cause,
                                  bool dispatched) {
  A11Y_RECORDER_COST("RecordBlinkCookieStoreChange");
  RecorderPipeClient* client = GetProcessRecorderClient();
  if (!client) {
    return;
  }
  base::DictValue payload;
  payload.Set("context", CreateContext(*client, document_node_id,
                                       std::move(document_token)));
  payload.Set("contextKind", NormalizeCookieContextKind(std::move(context_kind)));
  payload.Set("name", std::move(name));
  payload.Set("domain", std::move(domain));
  payload.Set("path", std::move(path));
  payload.Set("cause", std::move(cause));
  payload.Set("dispatched", dispatched);
  SendBlinkEvidence("browser.cookie", "cookie-store-change",
                    std::move(payload));
}

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
                                    std::vector<CookieAccessEntry> cookies) {
  A11Y_RECORDER_COST("RecordBrowserFrameCookieAccess");
  RecorderPipeClient* client = GetProcessRecorderClient();
  if (!client || page_frame_tree_node_id < 0 || frame_tree_node_id < 0) {
    return;
  }
  base::DictValue payload;
  payload.Set("context",
              CreateNavigationContext(*client, page_frame_tree_node_id,
                                      frame_tree_node_id,
                                      document_navigation_id,
                                      std::move(document_token)));
  payload.Set("observer", "frame");
  payload.Set("navigationId", base::Value());
  payload.Set("rendererProcessId", renderer_process_id > 0
                                       ? base::Value(renderer_process_id)
                                       : base::Value());
  SetCookieAccess(payload, change, std::move(url), std::move(frame_origin),
                  std::move(top_frame_origin), std::move(request_id),
                  ad_tagged, std::move(cookies));
  SendBlinkEvidence("browser.cookie", "cookie-access", std::move(payload));
}

void RecordBrowserNavigationCookieAccess(
    int64_t navigation_id,
    int page_frame_tree_node_id,
    int frame_tree_node_id,
    bool change,
    std::string url,
    std::string frame_origin,
    std::string top_frame_origin,
    std::string request_id,
    bool ad_tagged,
    std::vector<CookieAccessEntry> cookies) {
  A11Y_RECORDER_COST("RecordBrowserNavigationCookieAccess");
  RecorderPipeClient* client = GetProcessRecorderClient();
  if (!client || navigation_id <= 0 || page_frame_tree_node_id < 0 ||
      frame_tree_node_id < 0) {
    return;
  }
  base::DictValue payload;
  payload.Set("context",
              CreateNavigationContext(*client, page_frame_tree_node_id,
                                      frame_tree_node_id, 0, std::string()));
  payload.Set("observer", "navigation");
  payload.Set("navigationId",
              "navigation-" + base::NumberToString(navigation_id));
  payload.Set("rendererProcessId", base::Value());
  SetCookieAccess(payload, change, std::move(url), std::move(frame_origin),
                  std::move(top_frame_origin), std::move(request_id),
                  ad_tagged, std::move(cookies));
  SendBlinkEvidence("browser.cookie", "cookie-access", std::move(payload));
}

namespace {

bool IsOneOf(const std::string& value,
             std::initializer_list<std::string_view> allowed) {
  for (std::string_view candidate : allowed) {
    if (value == candidate) {
      return true;
    }
  }
  return false;
}

base::Value OptionalNodeId(int node_id) {
  return node_id > 0 ? base::Value(node_id) : base::Value();
}

}  // namespace

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
                             CookieCallOrigin origin) {
  A11Y_RECORDER_COST("RecordBlinkFocusChanged");
  RecorderPipeClient* client = GetProcessRecorderClient();
  if (!client || document_node_id <= 0 || document_token.empty() ||
      !IsOneOf(focus_type, {"none", "script", "forward", "backward",
                            "spatial-navigation", "mouse", "access-key",
                            "page"}) ||
      !IsOneOf(focus_trigger, {"script", "user-gesture"})) {
    return;
  }
  // The outcome is derived from the three node identities rather than from
  // the return path Blink took, so it cannot disagree with them.
  const char* outcome = "focused";
  if (requested_node_id <= 0) {
    outcome = focused_node_id <= 0 ? "cleared" : "redirected";
  } else if (focused_node_id <= 0) {
    outcome = "not-focused";
  } else if (focused_node_id != requested_node_id) {
    outcome = "redirected";
  }
  base::DictValue payload;
  payload.Set("context",
              CreateCookieRendererContext(*client, document_node_id,
                                          std::move(document_token), origin));
  payload.Set("previousNodeId", OptionalNodeId(previous_node_id));
  payload.Set("requestedNodeId", OptionalNodeId(requested_node_id));
  payload.Set("focusedNodeId", OptionalNodeId(focused_node_id));
  payload.Set("outcome", outcome);
  payload.Set("activeDescendantNodeId",
              OptionalNodeId(focused_node_id > 0 ? active_descendant_node_id
                                                 : 0));
  payload.Set("focusType", std::move(focus_type));
  payload.Set("focusTrigger", std::move(focus_trigger));
  payload.Set("preventScroll", prevent_scroll);
  payload.Set("focusVisible", focus_visible_present ? base::Value(focus_visible)
                                                    : base::Value());
  SetCookieCallOrigin(payload, std::move(origin));
  SendBlinkEvidence("browser.interaction", "focus-changed",
                    std::move(payload));
}

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
                                 CookieCallOrigin origin) {
  A11Y_RECORDER_COST("RecordBlinkSelectionChanged");
  RecorderPipeClient* client = GetProcessRecorderClient();
  if (!client || document_node_id <= 0 || document_token.empty() ||
      !IsOneOf(set_by, {"user", "system"}) ||
      !IsOneOf(selection_type, {"none", "caret", "range"})) {
    return;
  }
  const bool has_positions = selection_type != "none";
  if (has_positions && (anchor_node_id <= 0 || focus_node_id <= 0 ||
                        anchor_offset < 0 || focus_offset < 0)) {
    return;
  }
  const bool has_text_control = has_positions && text_control_node_id > 0;
  if (has_text_control &&
      (text_control_selection_start < 0 ||
       text_control_selection_end < text_control_selection_start ||
       !IsOneOf(text_control_selection_direction,
                {"none", "forward", "backward"}))) {
    return;
  }
  base::DictValue payload;
  payload.Set("context",
              CreateCookieRendererContext(*client, document_node_id,
                                          std::move(document_token), origin));
  payload.Set("setBy", std::move(set_by));
  payload.Set("selectionType", selection_type);
  payload.Set("anchorNodeId", has_positions ? base::Value(anchor_node_id)
                                            : base::Value());
  payload.Set("anchorOffset", has_positions ? base::Value(anchor_offset)
                                            : base::Value());
  payload.Set("focusNodeId", has_positions ? base::Value(focus_node_id)
                                           : base::Value());
  payload.Set("focusOffset", has_positions ? base::Value(focus_offset)
                                           : base::Value());
  payload.Set("directional", directional);
  payload.Set("textControlNodeId", has_text_control
                                       ? base::Value(text_control_node_id)
                                       : base::Value());
  payload.Set("textControlSelectionStart",
              has_text_control ? base::Value(text_control_selection_start)
                               : base::Value());
  payload.Set("textControlSelectionEnd",
              has_text_control ? base::Value(text_control_selection_end)
                               : base::Value());
  payload.Set("textControlSelectionDirection",
              has_text_control
                  ? base::Value(std::move(text_control_selection_direction))
                  : base::Value());
  SetCookieCallOrigin(payload, std::move(origin));
  SendBlinkEvidence("browser.interaction", "selection-changed",
                    std::move(payload));
}

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
                                        CookieCallOrigin origin) {
  A11Y_RECORDER_COST("RecordBlinkTextControlValueChanged");
  RecorderPipeClient* client = GetProcessRecorderClient();
  if (!client || document_node_id <= 0 || document_token.empty() ||
      node_id <= 0 || control_type.empty() ||
      !IsOneOf(source, {"value-set", "user-edit"}) || value_length < 0 ||
      maximum_value_length <= 0 || selection_start < 0 ||
      selection_end < selection_start ||
      !IsOneOf(selection_direction, {"none", "forward", "backward"})) {
    return;
  }
  base::DictValue payload;
  payload.Set("context",
              CreateCookieRendererContext(*client, document_node_id,
                                          std::move(document_token), origin));
  payload.Set("nodeId", node_id);
  payload.Set("controlType", std::move(control_type));
  payload.Set("source", std::move(source));
  SetTruncatedTextProperties(payload, "value", "valueLength", "valueTruncated",
                             std::move(value), value_length, value_truncated);
  payload.Set("maximumValueLength", maximum_value_length);
  payload.Set("selectionStart", selection_start);
  payload.Set("selectionEnd", selection_end);
  payload.Set("selectionDirection", std::move(selection_direction));
  SetCookieCallOrigin(payload, std::move(origin));
  SendBlinkEvidence("browser.interaction", "text-control-value-changed",
                    std::move(payload));
}

void RecordBlinkActiveDescendantReferenceSet(int document_node_id,
                                             std::string document_token,
                                             int node_id,
                                             int referenced_node_id,
                                             CookieCallOrigin origin) {
  A11Y_RECORDER_COST("RecordBlinkActiveDescendantReferenceSet");
  RecorderPipeClient* client = GetProcessRecorderClient();
  if (!client || document_node_id <= 0 || document_token.empty() ||
      node_id <= 0 || referenced_node_id <= 0) {
    return;
  }
  base::DictValue payload;
  payload.Set("context",
              CreateCookieRendererContext(*client, document_node_id,
                                          std::move(document_token), origin));
  payload.Set("nodeId", node_id);
  payload.Set("referencedNodeId", referenced_node_id);
  SetCookieCallOrigin(payload, std::move(origin));
  SendBlinkEvidence("browser.interaction", "active-descendant-reference-set",
                    std::move(payload));
}

namespace {

base::DictValue PagePopupRectValue(const PagePopupRect& rect) {
  base::DictValue value;
  value.Set("x", rect.x);
  value.Set("y", rect.y);
  value.Set("width", rect.width);
  value.Set("height", rect.height);
  return value;
}

bool IsValidPagePopupRect(const PagePopupRect& rect) {
  return rect.width >= 0 && rect.height >= 0;
}

}  // namespace

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
                                double zoom_factor) {
  A11Y_RECORDER_COST("RecordBlinkPagePopupOpened");
  RecorderPipeClient* client = GetProcessRecorderClient();
  if (!client || document_node_id <= 0 || document_token.empty() ||
      !IsOneOf(kind, {"select-list", "date-time", "color", "other"}) ||
      owner_document_node_id <= 0 || owner_document_token.empty() ||
      owner_frame_token.empty() || owner_node_id <= 0 ||
      !IsValidPagePopupRect(owner_visible_bounds_in_local_root) ||
      !IsValidPagePopupRect(owner_local_root_rect_in_screen) ||
      !IsValidPagePopupRect(anchor_rect_in_screen) ||
      !IsValidPagePopupRect(initial_window_rect) ||
      !std::isfinite(zoom_factor) || zoom_factor <= 0) {
    return;
  }
  base::DictValue payload;
  payload.Set("context", CreateContext(*client, document_node_id,
                                       std::move(document_token)));
  payload.Set("kind", std::move(kind));
  payload.Set("ownerDocumentId", DocumentId(owner_document_node_id));
  payload.Set("ownerDocumentToken", std::move(owner_document_token));
  payload.Set("ownerFrameToken", std::move(owner_frame_token));
  payload.Set("ownerNodeId", owner_node_id);
  payload.Set("ownerVisibleBoundsInLocalRoot",
              PagePopupRectValue(owner_visible_bounds_in_local_root));
  payload.Set("ownerLocalRootRectInScreen",
              PagePopupRectValue(owner_local_root_rect_in_screen));
  payload.Set("anchorRectInScreen", PagePopupRectValue(anchor_rect_in_screen));
  payload.Set("initialWindowRect", PagePopupRectValue(initial_window_rect));
  payload.Set("zoomFactor", zoom_factor);
  SendBlinkEvidence("browser.interaction", "page-popup-opened",
                    std::move(payload));
}

void RecordBlinkPagePopupWindowRect(int document_node_id,
                                    std::string document_token,
                                    bool deferred,
                                    PagePopupRect window_rect) {
  A11Y_RECORDER_COST("RecordBlinkPagePopupWindowRect");
  RecorderPipeClient* client = GetProcessRecorderClient();
  if (!client || document_node_id <= 0 || document_token.empty() ||
      !IsValidPagePopupRect(window_rect)) {
    return;
  }
  base::DictValue payload;
  payload.Set("context", CreateContext(*client, document_node_id,
                                       std::move(document_token)));
  payload.Set("deferred", deferred);
  payload.Set("windowRect", PagePopupRectValue(window_rect));
  SendBlinkEvidence("browser.interaction", "page-popup-window-rect",
                    std::move(payload));
}

namespace {

bool IsValidPopupWidgetSink(const PopupWidgetSink& sink) {
  return sink.client_id != 0 || sink.sink_id != 0;
}

std::string PopupWidgetSinkId(const PopupWidgetSink& sink) {
  return base::NumberToString(sink.client_id) + ":" +
         base::NumberToString(sink.sink_id);
}

base::Value OptionalScreenRect(bool present, const PagePopupRect& rect) {
  return present ? base::Value(PagePopupRectValue(rect)) : base::Value();
}

}  // namespace

void RecordBrowserPopupWidgetCreated(int page_frame_tree_node_id,
                                     int frame_tree_node_id,
                                     int64_t document_navigation_id,
                                     std::string document_token,
                                     int renderer_process_id,
                                     std::string opener_frame_token,
                                     PopupWidgetSink sink) {
  A11Y_RECORDER_COST("RecordBrowserPopupWidgetCreated");
  RecorderPipeClient* client = GetProcessRecorderClient();
  if (!client || page_frame_tree_node_id < 0 || frame_tree_node_id < 0 ||
      opener_frame_token.empty() || !IsValidPopupWidgetSink(sink)) {
    return;
  }
  base::DictValue payload;
  payload.Set("context",
              CreateNavigationContext(*client, page_frame_tree_node_id,
                                      frame_tree_node_id,
                                      document_navigation_id,
                                      std::move(document_token)));
  payload.Set("rendererProcessId", renderer_process_id > 0
                                       ? base::Value(renderer_process_id)
                                       : base::Value());
  payload.Set("openerFrameToken", std::move(opener_frame_token));
  payload.Set("frameSinkId", PopupWidgetSinkId(sink));
  SendBlinkEvidence("browser.interaction", "popup-widget-created",
                    std::move(payload));
}

// The Windows animation settings now, read with SystemParametersInfo, each
// null when the call fails (protocol 0.47).
namespace {
base::DictValue WindowsAnimationSettingsValue() {
  base::DictValue settings;
  ReadWindowsAnimationSettings(
      [](WindowsAnimationSetting setting) -> std::optional<bool> {
        UINT action = 0;
        switch (setting) {
          case WindowsAnimationSetting::kClientAreaAnimation:
            action = SPI_GETCLIENTAREAANIMATION;
            break;
          case WindowsAnimationSetting::kUiEffects:
            action = SPI_GETUIEFFECTS;
            break;
          case WindowsAnimationSetting::kMenuAnimation:
            action = SPI_GETMENUANIMATION;
            break;
          case WindowsAnimationSetting::kMenuFade:
            action = SPI_GETMENUFADE;
            break;
          case WindowsAnimationSetting::kComboBoxAnimation:
            action = SPI_GETCOMBOBOXANIMATION;
            break;
        }
        BOOL value = FALSE;
        if (!::SystemParametersInfoW(action, 0, &value, 0)) {
          return std::nullopt;
        }
        return value != FALSE;
      },
      [&settings](std::string_view name, std::optional<bool> value) {
        settings.Set(name, value.has_value() ? base::Value(*value)
                                             : base::Value());
      });
  return settings;
}
}  // namespace

void RecordBrowserPopupWidgetShown(PopupWidgetShown shown) {
  A11Y_RECORDER_COST("RecordBrowserPopupWidgetShown");
  RecorderPipeClient* client = GetProcessRecorderClient();
  const bool is_shown = shown.outcome == "shown";
  if (!client || !IsValidPopupWidgetSink(shown.sink) ||
      !IsOneOf(shown.outcome, {"shown", "window-not-active", "not-visible",
                               "permission-exclusion"}) ||
      is_shown != shown.has_view_bounds ||
      (shown.has_constrained && !shown.has_transformed) ||
      !IsValidPagePopupRect(shown.received_rect) ||
      !IsValidPagePopupRect(shown.received_anchor_rect) ||
      (shown.has_transformed &&
       (!IsValidPagePopupRect(shown.transformed_rect) ||
        !IsValidPagePopupRect(shown.transformed_anchor_rect))) ||
      (shown.has_constrained && !IsValidPagePopupRect(shown.constrained_rect)) ||
      (shown.has_view_bounds && !IsValidPagePopupRect(shown.view_bounds))) {
    return;
  }
  base::DictValue payload;
  payload.Set("context", CreateContext(*client, 0));
  payload.Set("frameSinkId", PopupWidgetSinkId(shown.sink));
  payload.Set("outcome", std::move(shown.outcome));
  payload.Set("receivedRect", PagePopupRectValue(shown.received_rect));
  payload.Set("receivedAnchorRect",
              PagePopupRectValue(shown.received_anchor_rect));
  payload.Set("transformedRect",
              OptionalScreenRect(shown.has_transformed, shown.transformed_rect));
  payload.Set("transformedAnchorRect",
              OptionalScreenRect(shown.has_transformed,
                                 shown.transformed_anchor_rect));
  payload.Set("constrainedRect", OptionalScreenRect(shown.has_constrained,
                                                    shown.constrained_rect));
  payload.Set("viewBounds",
              OptionalScreenRect(shown.has_view_bounds, shown.view_bounds));
  payload.Set("windowsAnimationSettings", WindowsAnimationSettingsValue());
  SendBlinkEvidence("browser.interaction", "popup-widget-shown",
                    std::move(payload));
}

void RecordBrowserPopupWidgetBoundsRequested(PopupWidgetSink sink,
                                             PagePopupRect requested_rect,
                                             bool has_set_rect,
                                             PagePopupRect set_rect) {
  A11Y_RECORDER_COST("RecordBrowserPopupWidgetBoundsRequested");
  RecorderPipeClient* client = GetProcessRecorderClient();
  if (!client || !IsValidPopupWidgetSink(sink) ||
      !IsValidPagePopupRect(requested_rect) ||
      (has_set_rect && !IsValidPagePopupRect(set_rect))) {
    return;
  }
  base::DictValue payload;
  payload.Set("context", CreateContext(*client, 0));
  payload.Set("frameSinkId", PopupWidgetSinkId(sink));
  payload.Set("requestedRect", PagePopupRectValue(requested_rect));
  payload.Set("setRect", OptionalScreenRect(has_set_rect, set_rect));
  SendBlinkEvidence("browser.interaction", "popup-widget-bounds-requested",
                    std::move(payload));
}

void RecordBrowserPopupWidgetScreenRects(PopupWidgetSink sink,
                                         PagePopupRect view_rect,
                                         PagePopupRect window_rect,
                                         uintptr_t native_window,
                                         double device_scale_factor) {
  A11Y_RECORDER_COST("RecordBrowserPopupWidgetScreenRects");
  RecorderPipeClient* client = GetProcessRecorderClient();
  if (!client || !IsValidPopupWidgetSink(sink) ||
      !IsValidPagePopupRect(view_rect) || !IsValidPagePopupRect(window_rect) ||
      !std::isfinite(device_scale_factor) || device_scale_factor <= 0) {
    return;
  }
  // The window rectangle and the client area in screen pixels, as Windows
  // holds them now. Either is absent when Windows does not answer.
  PagePopupRect native_window_rect;
  PagePopupRect native_client_rect;
  bool has_native_rects = false;
  if (native_window != 0) {
    const HWND hwnd = reinterpret_cast<HWND>(native_window);
    RECT window = {};
    RECT client_area = {};
    POINT client_origin = {0, 0};
    if (::GetWindowRect(hwnd, &window) &&
        ::GetClientRect(hwnd, &client_area) &&
        ::ClientToScreen(hwnd, &client_origin)) {
      has_native_rects = true;
      native_window_rect = {window.left, window.top,
                            window.right - window.left,
                            window.bottom - window.top};
      native_client_rect = {client_origin.x, client_origin.y,
                            client_area.right - client_area.left,
                            client_area.bottom - client_area.top};
    }
  }
  base::DictValue payload;
  payload.Set("context", CreateContext(*client, 0));
  payload.Set("frameSinkId", PopupWidgetSinkId(sink));
  payload.Set("viewRect", PagePopupRectValue(view_rect));
  payload.Set("windowRect", PagePopupRectValue(window_rect));
  payload.Set("nativeWindowRect",
              OptionalScreenRect(has_native_rects, native_window_rect));
  payload.Set("nativeClientRect",
              OptionalScreenRect(has_native_rects, native_client_rect));
  payload.Set("deviceScaleFactor", device_scale_factor);
  SendBlinkEvidence("browser.interaction", "popup-widget-screen-rects",
                    std::move(payload));
}

void RecordBrowserPopupWidgetHidden(PopupWidgetSink sink,
                                    std::string cause,
                                    uintptr_t native_window) {
  A11Y_RECORDER_COST("RecordBrowserPopupWidgetHidden");
  RecorderPipeClient* client = GetProcessRecorderClient();
  if (!client || !IsValidPopupWidgetSink(sink) ||
      !IsOneOf(cause, {"hidden", "destroyed"})) {
    return;
  }
  // Whether Windows still shows the popup's window now, or absent when
  // there is no window or Windows does not know it.
  base::Value native_visible;
  if (native_window != 0) {
    const HWND hwnd = reinterpret_cast<HWND>(native_window);
    if (::IsWindow(hwnd)) {
      native_visible = base::Value(::IsWindowVisible(hwnd) != FALSE);
    }
  }
  base::DictValue payload;
  payload.Set("context", CreateContext(*client, 0));
  payload.Set("frameSinkId", PopupWidgetSinkId(sink));
  payload.Set("cause", std::move(cause));
  payload.Set("nativeWindowVisible", std::move(native_visible));
  SendBlinkEvidence("browser.interaction", "popup-widget-hidden",
                    std::move(payload));
}

void RecordBlinkPagePopupClosed(int document_node_id,
                                std::string document_token,
                                std::string closed_by) {
  A11Y_RECORDER_COST("RecordBlinkPagePopupClosed");
  RecorderPipeClient* client = GetProcessRecorderClient();
  if (!client || document_node_id <= 0 || document_token.empty() ||
      !IsOneOf(closed_by, {"renderer", "browser"})) {
    return;
  }
  base::DictValue payload;
  payload.Set("context", CreateContext(*client, document_node_id,
                                       std::move(document_token)));
  payload.Set("closedBy", std::move(closed_by));
  SendBlinkEvidence("browser.interaction", "page-popup-closed",
                    std::move(payload));
}

void RecordBlinkOptionSelectednessChanged(int document_node_id,
                                          std::string document_token,
                                          int node_id,
                                          int select_node_id,
                                          bool selected,
                                          CookieCallOrigin origin) {
  A11Y_RECORDER_COST("RecordBlinkOptionSelectednessChanged");
  RecorderPipeClient* client = GetProcessRecorderClient();
  if (!client || document_node_id <= 0 || document_token.empty() ||
      node_id <= 0 || select_node_id < 0) {
    return;
  }
  base::DictValue payload;
  payload.Set("context",
              CreateCookieRendererContext(*client, document_node_id,
                                          std::move(document_token), origin));
  payload.Set("nodeId", node_id);
  payload.Set("selectNodeId",
              select_node_id > 0 ? base::Value(select_node_id) : base::Value());
  payload.Set("selected", selected);
  SetCookieCallOrigin(payload, std::move(origin));
  SendBlinkEvidence("browser.interaction", "option-selectedness-changed",
                    std::move(payload));
}


namespace {

// The last counters each document reported to a layout checkpoint, keyed by
// the document's DOM node identifier, which is unique within one renderer
// process. A rendering update whose counters match is not recorded again.
struct LayoutCheckpointStorage {
  base::Lock lock;
  uint64_t next_checkpoint_id = 1;
  // What the document's last rendering update recorded, until its change
  // set reads it: nothing yet, a layout checkpoint, or a recalculation that
  // was not walked (protocol 0.35).
  enum class Update { kNone, kWalked, kNotWalked };
  struct DocumentCounters {
    unsigned style_resolution_count = 0;
    unsigned layout_count = 0;
    uint64_t checkpoint_sequence = 0;
    Update update = Update::kNone;
  };
  std::unordered_map<int, DocumentCounters> documents;
};

LayoutCheckpointStorage& LayoutCheckpoints() {
  static base::NoDestructor<LayoutCheckpointStorage> storage;
  return *storage;
}

std::string LayoutCheckpointId(uint64_t checkpoint_sequence) {
  return "layout-checkpoint-" + base::NumberToString(checkpoint_sequence);
}

bool IsFiniteNumber(double value) {
  return std::isfinite(value);
}

base::DictValue CreateLayoutCheckpointBasePayload(
    const RecorderPipeClient& client,
    uint64_t checkpoint_sequence,
    int document_node_id,
    std::string document_token) {
  base::DictValue payload;
  payload.Set("context", CreateContext(client, document_node_id,
                                       std::move(document_token)));
  payload.Set("checkpointId", LayoutCheckpointId(checkpoint_sequence));
  return payload;
}

}  // namespace

uint64_t BeginBlinkLayoutCheckpoint(
    int document_node_id,
    std::string document_token,
    unsigned style_resolution_count,
    unsigned layout_count,
    LayoutCheckpointFrame frame,
    const std::vector<std::string>& style_properties,
    int maximum_nodes) {
  static const int recorder_span_slot = CostSpanSlot("span:layout-checkpoint");
  BeginCostSpan(recorder_span_slot);
  A11Y_RECORDER_COST("BeginBlinkLayoutCheckpoint");
  RecorderPipeClient* client = GetProcessRecorderClient();
  if (!client || document_node_id <= 0 || document_token.empty() ||
      maximum_nodes <= 0 || style_properties.empty() ||
      !IsFiniteNumber(frame.viewport_width) ||
      !IsFiniteNumber(frame.viewport_height) ||
      !IsFiniteNumber(frame.scroll_x) || !IsFiniteNumber(frame.scroll_y) ||
      !IsFiniteNumber(frame.device_pixel_ratio) ||
      !IsFiniteNumber(frame.layout_zoom_factor) ||
      frame.viewport_width < 0 || frame.viewport_height < 0 ||
      frame.device_pixel_ratio <= 0 || frame.layout_zoom_factor <= 0) {
    return 0;
  }
  for (const std::string& property : style_properties) {
    if (property.empty()) {
      return 0;
    }
  }
  uint64_t checkpoint_sequence = 0;
  uint64_t previous_sequence = 0;
  std::string walk_reason;
  {
    LayoutCheckpointStorage& storage = LayoutCheckpoints();
    base::AutoLock lock(storage.lock);
    auto found = storage.documents.find(document_node_id);
    if (found != storage.documents.end()) {
      if (found->second.style_resolution_count == style_resolution_count &&
          found->second.layout_count == layout_count) {
        return 0;
      }
      previous_sequence = found->second.checkpoint_sequence;
    }
    walk_reason = LayoutWalkReason(document_node_id);
    if (walk_reason.empty() && found != storage.documents.end()) {
      // The update is recorded by its change set. The counters move on, so
      // the next update is compared with this one, as when it is walked.
      found->second.style_resolution_count = style_resolution_count;
      found->second.layout_count = layout_count;
      found->second.update = LayoutCheckpointStorage::Update::kNotWalked;
      return 0;
    }
    if (walk_reason.empty()) {
      walk_reason = "first";
    }
    checkpoint_sequence = storage.next_checkpoint_id++;
    storage.documents.insert_or_assign(
        document_node_id,
        LayoutCheckpointStorage::DocumentCounters{
            style_resolution_count, layout_count, checkpoint_sequence,
            LayoutCheckpointStorage::Update::kWalked});
  }
  base::DictValue payload = CreateLayoutCheckpointBasePayload(
      *client, checkpoint_sequence, document_node_id,
      std::move(document_token));
  payload.Set("reason", "rendering-update");
  payload.Set("walkReason", std::move(walk_reason));
  payload.Set("previousCheckpointId",
              previous_sequence == 0
                  ? base::Value()
                  : base::Value(LayoutCheckpointId(previous_sequence)));
  payload.Set("styleResolutionCount",
              base::saturated_cast<int>(style_resolution_count));
  payload.Set("layoutCount", base::saturated_cast<int>(layout_count));
  base::DictValue viewport;
  viewport.Set("width", frame.viewport_width);
  viewport.Set("height", frame.viewport_height);
  payload.Set("viewport", std::move(viewport));
  base::DictValue scroll;
  scroll.Set("x", frame.scroll_x);
  scroll.Set("y", frame.scroll_y);
  payload.Set("scrollOffset", std::move(scroll));
  payload.Set("devicePixelRatio", frame.device_pixel_ratio);
  payload.Set("layoutZoomFactor", frame.layout_zoom_factor);
  payload.Set("maximumNodes", maximum_nodes);
  payload.Set("styleProperties", StringList(style_properties));
  SendBlinkEvidence("browser.layout", "layout-checkpoint-started",
                    std::move(payload));
  return checkpoint_sequence;
}

namespace {

// A variation axis tag, four characters packed big-endian as SkFourByteTag.
// A tag that is not four printable ASCII characters, which OpenType does not
// allow, is stated as its number in decimal.
std::string FontAxisTag(uint32_t axis) {
  std::string tag(4, ' ');
  for (int index = 0; index < 4; ++index) {
    const uint32_t character = (axis >> (24 - 8 * index)) & 0xff;
    if (character < 0x20 || character > 0x7e) {
      return base::NumberToString(axis);
    }
    tag[index] = static_cast<char>(character);
  }
  return tag;
}

base::DictValue BoxFragmentValue(LayoutBoxFragment& fragment);

// One fragment item (protocol 0.39). Every member is stated, null where it
// does not apply, and a run's glyphs are base64.
base::DictValue FragmentItemValue(LayoutFragmentItem& item) {
  base::DictValue value;
  value.Set("type", std::move(item.type));
  value.Set("x", item.x);
  value.Set("y", item.y);
  value.Set("width", item.width);
  value.Set("height", item.height);
  value.Set("descendantsCount", item.descendants_count >= 0
                                    ? base::Value(item.descendants_count)
                                    : base::Value());
  value.Set("nodeId",
            item.node_id > 0 ? base::Value(item.node_id) : base::Value());
  if (item.range_present) {
    value.Set("start", base::saturated_cast<int>(item.start));
    value.Set("end", base::saturated_cast<int>(item.end));
  } else {
    value.Set("start", base::Value());
    value.Set("end", base::Value());
  }
  if (item.text) {
    value.Set("firstLineStyle", item.first_line_style);
    value.Set("direction", item.rtl ? "rtl" : "ltr");
    value.Set("hiddenForPaint", item.hidden_for_paint);
    base::ListValue runs;
    for (LayoutGlyphRun& run : item.glyph_runs) {
      base::DictValue run_value;
      base::DictValue font;
      font.Set("family", std::move(run.family));
      font.Set("postScriptName", std::move(run.post_script_name));
      font.Set("size", run.size);
      font.Set("syntheticBold", run.synthetic_bold);
      font.Set("syntheticItalic", run.synthetic_italic);
      run_value.Set("font", std::move(font));
      if (run.font_file_present) {
        base::DictValue font_file;
        font_file.Set("digest", std::move(run.font_file_digest));
        font_file.Set("index", run.font_file_index);
        base::ListValue variations;
        for (const LayoutFontVariation& variation : run.font_variations) {
          base::DictValue axis;
          axis.Set("axis", FontAxisTag(variation.axis));
          axis.Set("value", static_cast<double>(variation.value));
          variations.Append(std::move(axis));
        }
        font_file.Set("variations", std::move(variations));
        run_value.Set("fontFile", std::move(font_file));
      } else {
        run_value.Set("fontFile", base::Value());
      }
      run_value.Set("horizontal", run.horizontal);
      run_value.Set("rotation", run.rotation);
      run_value.Set("glyphs", base::Base64Encode(PackGlyphs(run.glyphs)));
      runs.Append(std::move(run_value));
    }
    value.Set("glyphRuns", std::move(runs));
  } else {
    value.Set("firstLineStyle", base::Value());
    value.Set("direction", base::Value());
    value.Set("hiddenForPaint", base::Value());
    value.Set("glyphRuns", base::Value());
  }
  value.Set("generatedText", item.generated_text_present
                                 ? base::Value(std::move(item.generated_text))
                                 : base::Value());
  return value;
}

// A child link of a box fragment (protocol 0.38).
base::DictValue FragmentChildValue(LayoutFragmentChild& child) {
  base::DictValue value;
  value.Set("kind", std::move(child.kind));
  value.Set("x", child.x);
  value.Set("y", child.y);
  if (child.node_id > 0) {
    value.Set("nodeId", child.node_id);
    value.Set("fragmentIndex", child.fragment_index >= 0
                                   ? base::Value(child.fragment_index)
                                   : base::Value());
  } else {
    value.Set("nodeId", base::Value());
    value.Set("fragmentIndex", base::Value());
  }
  if (!child.fragment.empty()) {
    value.Set("fragment", BoxFragmentValue(child.fragment.front()));
  } else {
    value.Set("fragment", base::Value());
  }
  return value;
}

// One physical fragment of a box, in Blink's layout units (protocol 0.38).
base::DictValue BoxFragmentValue(LayoutBoxFragment& fragment) {
  base::DictValue value;
  value.Set("width", fragment.width);
  value.Set("height", fragment.height);
  if (fragment.break_token_present) {
    base::DictValue token;
    token.Set("consumedBlockSize", fragment.consumed_block_size);
    token.Set("breakBefore", fragment.break_before);
    token.Set("sequenceNumber",
              fragment.break_before
                  ? base::Value()
                  : base::Value(base::saturated_cast<int>(
                        fragment.sequence_number)));
    token.Set("atBlockEnd", fragment.at_block_end);
    value.Set("breakToken", std::move(token));
  } else {
    value.Set("breakToken", base::Value());
  }
  if (fragment.scrollable_overflow_present) {
    base::DictValue overflow;
    overflow.Set("x", fragment.scrollable_overflow.x);
    overflow.Set("y", fragment.scrollable_overflow.y);
    overflow.Set("width", fragment.scrollable_overflow.width);
    overflow.Set("height", fragment.scrollable_overflow.height);
    value.Set("scrollableOverflow", std::move(overflow));
  } else {
    value.Set("scrollableOverflow", base::Value());
  }
  base::ListValue children;
  for (LayoutFragmentChild& child : fragment.children) {
    children.Append(FragmentChildValue(child));
  }
  value.Set("children", std::move(children));
  if (fragment.text_present) {
    value.Set("textContent", std::move(fragment.text_content));
    value.Set("firstLineText",
              fragment.first_line_text_present
                  ? base::Value(std::move(fragment.first_line_text))
                  : base::Value());
  } else {
    value.Set("textContent", base::Value());
    value.Set("firstLineText", base::Value());
  }
  if (fragment.items_present) {
    base::ListValue items;
    for (LayoutFragmentItem& item : fragment.items) {
      items.Append(FragmentItemValue(item));
    }
    value.Set("items", std::move(items));
  } else {
    value.Set("items", base::Value());
  }
  return value;
}

// The box fragments of a node, or null when its layout object is not a box
// (protocol 0.38).
base::Value BoxFragmentsValue(LayoutBoxFragments& fragments) {
  if (!fragments.present) {
    return base::Value();
  }
  base::DictValue value;
  value.Set("effectiveZoom", fragments.effective_zoom);
  base::ListValue list;
  for (LayoutBoxFragment& fragment : fragments.fragments) {
    list.Append(BoxFragmentValue(fragment));
  }
  value.Set("fragments", std::move(list));
  if (fragments.natural_size_present) {
    base::DictValue natural;
    natural.Set("width", fragments.natural_width);
    natural.Set("height", fragments.natural_height);
    natural.Set("hasWidth", fragments.natural_has_width);
    natural.Set("hasHeight", fragments.natural_has_height);
    natural.Set("aspectRatioWidth", fragments.natural_aspect_ratio_width);
    natural.Set("aspectRatioHeight", fragments.natural_aspect_ratio_height);
    value.Set("naturalSize", std::move(natural));
  } else {
    value.Set("naturalSize", base::Value());
  }
  if (fragments.text_present && !fragments.text_unchanged) {
    value.Set("textContent", std::move(fragments.text_content));
    value.Set("firstLineText",
              fragments.first_line_text_present
                  ? base::Value(std::move(fragments.first_line_text))
                  : base::Value());
  } else {
    value.Set("textContent", base::Value());
    value.Set("firstLineText", base::Value());
  }
  value.Set("textContentUnchanged", fragments.text_unchanged);
  return base::Value(std::move(value));
}

// Estimates a fragment's serialized size: its fixed members and, for each
// child, its members and any nested fragment.
size_t EstimateBoxFragmentBytes(const LayoutBoxFragment& fragment) {
  constexpr size_t kFragmentBytes = 240;
  constexpr size_t kChildBytes = 110;
  // An item's members, and a run's font and members, with their names.
  constexpr size_t kItemBytes = 260;
  constexpr size_t kRunBytes = 140;
  size_t bytes = kFragmentBytes + fragment.text_content.size() +
                 fragment.first_line_text.size();
  for (const LayoutFragmentItem& item : fragment.items) {
    bytes += kItemBytes + item.generated_text.size();
    for (const LayoutGlyphRun& run : item.glyph_runs) {
      // The font file's digest, index, and variation axes, with their names.
      bytes += 120 + run.font_file_digest.size() +
               run.font_variations.size() * 40;
      bytes += kRunBytes + run.family.size() + run.post_script_name.size() +
               (run.glyphs.size() * kPackedGlyphBytes + 2) / 3 * 4;
    }
  }
  for (const LayoutFragmentChild& child : fragment.children) {
    bytes += kChildBytes;
    for (const LayoutBoxFragment& nested : child.fragment) {
      bytes += EstimateBoxFragmentBytes(nested);
    }
  }
  return bytes;
}

// Sets the fields a checkpoint record and a change record of a node share:
// its identity, type, and name, its pseudo-element and shadow fields, whether
// it has a layout object and is display locked, and its computed style.
void SetLayoutNodeFields(base::DictValue& payload, LayoutCheckpointNode& node) {
  payload.Set("nodeId", node.node_id);
  payload.Set("nodeType", node.pseudo_element_present
                              ? std::string("pseudo-element")
                              : DomNodeTypeName(node.node_type));
  payload.Set("nodeName", std::move(node.node_name));
  if (node.pseudo_element_present) {
    base::DictValue pseudo;
    pseudo.Set("originatingNodeId", node.originating_node_id > 0
                                        ? base::Value(node.originating_node_id)
                                        : base::Value());
    pseudo.Set("pseudoType", std::move(node.pseudo_type));
    SetTruncatedTextProperties(pseudo, "generatedText", "generatedTextLength",
                               "generatedTextTruncated",
                               std::move(node.generated_text),
                               node.generated_text_length,
                               node.generated_text_truncated);
    payload.Set("pseudoElement", std::move(pseudo));
  } else {
    payload.Set("pseudoElement", base::Value());
  }
  if (node.shadow_host_node_id > 0 &&
      (node.shadow_root_mode == "open" || node.shadow_root_mode == "closed" ||
       node.shadow_root_mode == "user-agent")) {
    payload.Set("shadowHostNodeId", node.shadow_host_node_id);
    payload.Set("shadowRootMode", std::move(node.shadow_root_mode));
  } else {
    payload.Set("shadowHostNodeId", base::Value());
    payload.Set("shadowRootMode", base::Value());
  }
  payload.Set("layoutObjectPresent", node.layout_object_present);
  payload.Set("displayLocked", node.display_locked);
  if (node.computed_style_present) {
    base::DictValue style;
    for (LayoutCheckpointStyleValue& entry : node.computed_style) {
      style.Set(entry.property_name,
                entry.value_present ? base::Value(std::move(entry.value))
                                    : base::Value());
    }
    payload.Set("computedStyle", std::move(style));
    base::DictValue custom_properties;
    for (LayoutCheckpointStyleValue& entry : node.custom_properties) {
      custom_properties.Set(entry.property_name,
                            entry.value_present
                                ? base::Value(std::move(entry.value))
                                : base::Value());
    }
    payload.Set("customProperties", std::move(custom_properties));
  } else {
    payload.Set("computedStyle", base::Value());
    payload.Set("customProperties", base::Value());
  }
  payload.Set("boxFragments", BoxFragmentsValue(node.box_fragments));
}

// Builds a layout checkpoint node record from the values the renderer copied
// out of Blink, so the dictionary is built on the writer thread rather than
// in the rendering update that observed the node.
base::DictValue CreateLayoutCheckpointNodePayload(
    const RecorderPipeClient& client,
    uint64_t checkpoint_sequence,
    int document_node_id,
    std::string document_token,
    LayoutCheckpointNode node) {
  base::DictValue payload = CreateLayoutCheckpointBasePayload(
      client, checkpoint_sequence, document_node_id, std::move(document_token));
  payload.Set("nodeIndex", node.node_index);
  if (node.layout_object_present) {
    base::DictValue rect;
    rect.Set("x", node.x);
    rect.Set("y", node.y);
    rect.Set("width", node.width);
    rect.Set("height", node.height);
    payload.Set("boundingClientRect", std::move(rect));
  } else {
    payload.Set("boundingClientRect", base::Value());
  }
  SetLayoutNodeFields(payload, node);
  return payload;
}

struct LayoutCheckpointNodeEvidence : PendingEvidence {
  // The client that owns the writer thread, and so outlives this record.
  raw_ptr<const RecorderPipeClient> client = nullptr;
  uint64_t checkpoint_sequence = 0;
  int document_node_id = 0;
  std::string document_token;
  LayoutCheckpointNode node;

  base::DictValue TakePayload() override {
    return CreateLayoutCheckpointNodePayload(
        *client, checkpoint_sequence, document_node_id,
        std::move(document_token), std::move(node));
  }
};

// Estimates the node record's serialized size from the copied values, which
// make up nearly all of it.
size_t EstimateLayoutCheckpointNodeBytes(const LayoutCheckpointNode& node,
                                         const std::string& document_token) {
  // The context, identities, flags, and rectangle, with their member names.
  constexpr size_t kFixedBytes = 640;
  // A property's quotes, colon, and separator.
  constexpr size_t kStyleEntryBytes = 6;
  size_t bytes = kFixedBytes + document_token.size() + node.node_name.size() +
                 node.pseudo_type.size() + node.generated_text.size() +
                 node.shadow_root_mode.size();
  for (const LayoutCheckpointStyleValue& entry : node.computed_style) {
    bytes += kStyleEntryBytes + entry.property_name.size() + entry.value.size();
  }
  for (const LayoutCheckpointStyleValue& entry : node.custom_properties) {
    bytes += kStyleEntryBytes + entry.property_name.size() + entry.value.size();
  }
  if (node.box_fragments.present) {
    // The zoom and the natural size, with their member names.
    constexpr size_t kBoxFragmentsBytes = 200;
    bytes += kBoxFragmentsBytes + node.box_fragments.text_content.size() +
             node.box_fragments.first_line_text.size();
    for (const LayoutBoxFragment& fragment : node.box_fragments.fragments) {
      bytes += EstimateBoxFragmentBytes(fragment);
    }
  }
  return bytes;
}

}  // namespace

void RecordBlinkLayoutCheckpointNode(uint64_t checkpoint_sequence,
                                     int document_node_id,
                                     std::string document_token,
                                     LayoutCheckpointNode node) {
  A11Y_RECORDER_COST("RecordBlinkLayoutCheckpointNode");
  RecorderPipeClient* client = GetProcessRecorderClient();
  if (!client || checkpoint_sequence == 0 || document_node_id <= 0 ||
      document_token.empty() || node.node_index < 0 || node.node_id <= 0 ||
      node.node_name.empty() || (node.node_type != 1 && node.node_type != 3)) {
    return;
  }
  if (node.pseudo_element_present &&
      (node.node_type != 1 || node.pseudo_type.empty() ||
       node.generated_text_length < 0)) {
    return;
  }
  // Text nodes carry no style of their own, and a text node is only recorded
  // when it has a layout object.
  if (node.node_type == 3 &&
      (!node.layout_object_present || node.computed_style_present)) {
    return;
  }
  if (node.layout_object_present &&
      (!IsFiniteNumber(node.x) || !IsFiniteNumber(node.y) ||
       !IsFiniteNumber(node.width) || !IsFiniteNumber(node.height) ||
       node.width < 0 || node.height < 0)) {
    return;
  }
  if (node.computed_style_present) {
    for (const LayoutCheckpointStyleValue& entry : node.computed_style) {
      if (entry.property_name.empty()) {
        return;
      }
    }
  }
  auto evidence = std::make_unique<LayoutCheckpointNodeEvidence>();
  evidence->channel = "browser.layout";
  evidence->event_type = "layout-checkpoint-node";
  evidence->bytes = EstimateLayoutCheckpointNodeBytes(node, document_token);
  evidence->client = client;
  evidence->checkpoint_sequence = checkpoint_sequence;
  evidence->document_node_id = document_node_id;
  evidence->document_token = std::move(document_token);
  evidence->node = std::move(node);
  QueueBlinkEvidence(client, std::move(evidence));
}

void RecordBlinkLayoutCheckpointCost(const LayoutCheckpointCost& cost) {
  A11Y_RECORDER_COST("RecordBlinkLayoutCheckpointCost");
  static const int node_fields = RegisterCostKind("layout.node-fields");
  static const int geometry = RegisterCostKind("layout.geometry");
  static const int style_values = RegisterCostKind("layout.style-values");
  static const int layout_dependent =
      RegisterCostKind("layout.style-values-layout-dependent");
  static const int style_cache = RegisterCostKind("layout.style-cache");
  static const int generated_text = RegisterCostKind("layout.generated-text");
  static const int pseudo_search =
      RegisterCostKind("layout.pseudo-element-search");
  static const int record = RegisterCostKind("layout.record-node");
  static const int styled_nodes = RegisterCountKind("count:layout.styled-nodes");
  static const int value_count = RegisterCountKind("count:layout.style-values");
  static const int dependent_count =
      RegisterCountKind("count:layout.style-values-layout-dependent");
  static const int previously_styled =
      RegisterCountKind("count:layout.previously-styled-nodes");
  static const int same_objects =
      RegisterCountKind("count:layout.same-style-objects");
  static const int reused = RegisterCountKind("count:layout.reused-style-values");
  static const int verification_checkpoints =
      RegisterCountKind("count:layout.style-verification-checkpoints");
  static const int verified =
      RegisterCountKind("count:layout.verified-style-values");
  static const int verified_differed =
      RegisterCountKind("count:layout.verified-style-values-differed");
  RecordCost(node_fields, cost.node_fields_nanoseconds);
  RecordCost(geometry, cost.geometry_nanoseconds);
  RecordCost(style_values, cost.style_values_nanoseconds);
  RecordCost(layout_dependent, cost.layout_dependent_values_nanoseconds);
  RecordCost(style_cache, cost.style_cache_nanoseconds);
  RecordCost(generated_text, cost.generated_text_nanoseconds);
  RecordCost(pseudo_search, cost.pseudo_element_search_nanoseconds);
  RecordCost(record, cost.record_nanoseconds);
  RecordCost(styled_nodes, cost.styled_nodes);
  RecordCost(value_count, cost.style_values);
  RecordCost(dependent_count, cost.layout_dependent_values);
  RecordCost(previously_styled, cost.previously_styled_nodes);
  RecordCost(same_objects, cost.same_style_objects);
  RecordCost(reused, cost.reused_style_values);
  RecordCost(verification_checkpoints, cost.verification_checkpoints);
  RecordCost(verified, cost.verified_style_values);
  RecordCost(verified_differed, cost.verified_style_values_differed);
}

void RecordBlinkLayoutStyleReuseDifference(const std::string& property_name) {
  A11Y_RECORDER_COST("RecordBlinkLayoutStyleReuseDifference");
  constexpr int kMaximumStyleReuseDifferenceLines = 200;
  static std::atomic<int> written{0};
  if (written.fetch_add(1, std::memory_order_relaxed) >=
      kMaximumStyleReuseDifferenceLines) {
    return;
  }
  WriteDiagnosticLine("Recorder style reuse difference property=" +
                      property_name);
}

void CompleteBlinkLayoutCheckpoint(uint64_t checkpoint_sequence,
                                   int document_node_id,
                                   std::string document_token,
                                   int node_count,
                                   bool truncated,
                                   int maximum_nodes,
                                   int pseudo_element_count,
                                   int shadow_root_count) {
  static const int recorder_span_slot = CostSpanSlot("span:layout-checkpoint");
  // The span ends when this function returns, so it includes the completion.
  struct SpanEnd {
    ~SpanEnd() { EndCostSpan(recorder_span_slot); }
  } recorder_span_end;
  A11Y_RECORDER_COST("CompleteBlinkLayoutCheckpoint");
  RecorderPipeClient* client = GetProcessRecorderClient();
  if (!client || checkpoint_sequence == 0 || document_node_id <= 0 ||
      document_token.empty() || node_count < 0 || maximum_nodes <= 0 ||
      pseudo_element_count < 0 || pseudo_element_count > node_count ||
      shadow_root_count < 0) {
    return;
  }
  base::DictValue payload = CreateLayoutCheckpointBasePayload(
      *client, checkpoint_sequence, document_node_id,
      std::move(document_token));
  payload.Set("reason", "rendering-update");
  payload.Set("nodeCount", node_count);
  payload.Set("truncated", truncated);
  payload.Set("maximumNodes", maximum_nodes);
  payload.Set("pseudoElementCount", pseudo_element_count);
  payload.Set("shadowRootCount", shadow_root_count);
  SendBlinkEvidence("browser.layout", "layout-checkpoint-completed",
                    std::move(payload));
}

namespace {

// The layout change filter of each document, by the document's DOM node
// identifier, with the last layout checkpoint a change set of the document
// has seen. The filters of the least recently recorded documents are dropped
// beyond kMaximumLayoutChangeDocuments, so their nodes' next records are sent
// again whole.
struct LayoutChangeStorage {
  base::Lock lock;
  uint64_t next_change_set_id = 1;
  uint64_t use_clock = 0;
  struct Document {
    LayoutChangeFilter filter;
    uint64_t checkpoint_sequence_seen = 0;
    uint64_t last_use = 0;
    // The count of lost browser.layout records when the document's style
    // values were last trusted. A record lost since may have held a style
    // change, so the next record of each node holds its whole style again.
    uint64_t losses_seen = 0;
  };
  std::unordered_map<int, Document> documents;
};

constexpr size_t kMaximumLayoutChangeDocuments = 64;

LayoutChangeStorage& LayoutChanges() {
  static base::NoDestructor<LayoutChangeStorage> storage;
  return *storage;
}

std::string LayoutChangeSetId(uint64_t change_set_sequence) {
  return "layout-changes-" + base::NumberToString(change_set_sequence);
}

std::string LayoutTransformNodeId(uint64_t transform_node_id) {
  return "layout-transform-" + base::NumberToString(transform_node_id);
}

base::DictValue CreateLayoutChangesBasePayload(const RecorderPipeClient& client,
                                               uint64_t change_set_sequence,
                                               int document_node_id,
                                               std::string document_token) {
  base::DictValue payload;
  payload.Set("context", CreateContext(client, document_node_id,
                                       std::move(document_token)));
  payload.Set("changeSetId", LayoutChangeSetId(change_set_sequence));
  return payload;
}

// A checkpoint records a text node only when it has a layout object. A
// change set also records a text node whose layout object was destroyed
// (protocol 0.41), with none.
bool IsValidLayoutNode(const LayoutCheckpointNode& node,
                       bool text_without_layout_object = false) {
  if (node.node_id <= 0 || node.node_name.empty() ||
      (node.node_type != 1 && node.node_type != 3)) {
    return false;
  }
  if (node.pseudo_element_present &&
      (node.node_type != 1 || node.pseudo_type.empty() ||
       node.generated_text_length < 0)) {
    return false;
  }
  if (node.node_type == 3 &&
      ((!node.layout_object_present && !text_without_layout_object) ||
       node.computed_style_present)) {
    return false;
  }
  if (node.computed_style_present) {
    for (const LayoutCheckpointStyleValue& entry : node.computed_style) {
      if (entry.property_name.empty()) {
        return false;
      }
    }
  }
  return true;
}

bool IsValidLayoutChangedNode(const LayoutChangedNode& changed) {
  if (!IsValidLayoutNode(changed.node, /*text_without_layout_object=*/true) ||
      changed.reasons == 0 ||
      (changed.reasons & ~(kLayoutChangeStyle | kLayoutChangeLayout |
                           kLayoutChangePaintProperties)) != 0) {
    return false;
  }
  if (changed.geometry_present &&
      (!changed.node.layout_object_present || changed.transform_node_id == 0 ||
       !IsFiniteNumber(changed.client_rect_scale) ||
       changed.client_rect_scale <= 0)) {
    return false;
  }
  if (changed.client_rect_empty && changed.local_rect_mapped) {
    return false;
  }
  if (changed.geometry_present && changed.local_rect_mapped &&
      (!IsFiniteNumber(changed.local_x) || !IsFiniteNumber(changed.local_y) ||
       !IsFiniteNumber(changed.local_width) ||
       !IsFiniteNumber(changed.local_height) || changed.local_width < 0 ||
       changed.local_height < 0)) {
    return false;
  }
  if (!changed.local_quad_rects.empty() &&
      (!changed.geometry_present || !changed.local_rect_mapped ||
       changed.local_quad_rects.size() < 2)) {
    return false;
  }
  for (const LayoutLocalRect& rect : changed.local_quad_rects) {
    if (!IsFiniteNumber(rect.x) || !IsFiniteNumber(rect.y) ||
        !IsFiniteNumber(rect.width) || !IsFiniteNumber(rect.height) ||
        rect.width < 0 || rect.height < 0) {
      return false;
    }
  }
  return true;
}

bool IsValidLayoutScrollOffset(const LayoutScrollOffset& scroll) {
  return scroll.node_id > 0 && IsFiniteNumber(scroll.scroll_offset_x) &&
         IsFiniteNumber(scroll.scroll_offset_y) &&
         IsFiniteNumber(scroll.web_exposed_scroll_offset_x) &&
         IsFiniteNumber(scroll.web_exposed_scroll_offset_y) &&
         IsFiniteNumber(scroll.effective_zoom) && scroll.effective_zoom > 0;
}

bool IsValidLayoutTransformNode(const LayoutTransformNode& node) {
  if (node.id == 0 || node.parent_id == node.id) {
    return false;
  }
  for (double value : node.matrix) {
    if (!IsFiniteNumber(value)) {
      return false;
    }
  }
  return true;
}

base::ListValue LayoutChangeReasonList(unsigned reasons) {
  base::ListValue list;
  if (reasons & kLayoutChangeStyle) {
    list.Append("style");
  }
  if (reasons & kLayoutChangeLayout) {
    list.Append("layout");
  }
  if (reasons & kLayoutChangePaintProperties) {
    list.Append("paint-properties");
  }
  return list;
}

struct LayoutChangedNodeEvidence : PendingEvidence {
  // The client that owns the writer thread, and so outlives this record.
  raw_ptr<const RecorderPipeClient> client = nullptr;
  uint64_t change_set_sequence = 0;
  int document_node_id = 0;
  std::string document_token;
  LayoutChangedNode changed;

  base::DictValue TakePayload() override {
    base::DictValue payload = CreateLayoutChangesBasePayload(
        *client, change_set_sequence, document_node_id,
        std::move(document_token));
    payload.Set("reasons", LayoutChangeReasonList(changed.reasons));
    const bool style_present = changed.node.computed_style_present;
    SetLayoutNodeFields(payload, changed.node);
    // Protocol 0.37: after a node's first record, its computed style and
    // custom properties hold only the values that changed.
    payload.Set("computedStyleComplete",
                style_present ? base::Value(changed.computed_style_complete)
                              : base::Value());
    if (style_present && !changed.computed_style_complete) {
      base::ListValue removed;
      for (std::string& name : changed.removed_custom_properties) {
        removed.Append(std::move(name));
      }
      payload.Set("removedCustomProperties", std::move(removed));
    } else {
      payload.Set("removedCustomProperties", base::Value());
    }
    if (changed.geometry_present) {
      base::DictValue geometry;
      geometry.Set("transformNodeId",
                   LayoutTransformNodeId(changed.transform_node_id));
      if (changed.local_rect_mapped) {
        base::DictValue rect;
        rect.Set("x", changed.local_x);
        rect.Set("y", changed.local_y);
        rect.Set("width", changed.local_width);
        rect.Set("height", changed.local_height);
        geometry.Set("localRect", std::move(rect));
      } else {
        geometry.Set("localRect", base::Value());
      }
      if (changed.local_quad_rects.empty()) {
        geometry.Set("localQuadRects", base::Value());
      } else {
        base::ListValue quad_rects;
        for (const LayoutLocalRect& quad_rect : changed.local_quad_rects) {
          base::DictValue value;
          value.Set("x", quad_rect.x);
          value.Set("y", quad_rect.y);
          value.Set("width", quad_rect.width);
          value.Set("height", quad_rect.height);
          quad_rects.Append(std::move(value));
        }
        geometry.Set("localQuadRects", std::move(quad_rects));
      }
      geometry.Set("clientRectEmpty", changed.client_rect_empty);
      geometry.Set("localRectMapped", changed.local_rect_mapped);
      geometry.Set("clientRectScale", changed.client_rect_scale);
      payload.Set("geometry", std::move(geometry));
    } else {
      payload.Set("geometry", base::Value());
    }
    return payload;
  }
};

}  // namespace

uint64_t RecordBlinkLayoutChanges(
    int document_node_id,
    std::string document_token,
    LayoutChangesFrame frame,
    int noted_node_count,
    std::vector<LayoutTransformNode> transform_nodes,
    std::vector<LayoutChangedNode> nodes,
    std::vector<LayoutScrollOffset> scroll_offsets) {
  A11Y_RECORDER_COST("RecordBlinkLayoutChanges");
  RecorderPipeClient* client = GetProcessRecorderClient();
  if (!client || document_node_id <= 0 || document_token.empty() ||
      noted_node_count < 0 || frame.view_transform_node_id == 0 ||
      !IsFiniteNumber(frame.view_paint_offset_x) ||
      !IsFiniteNumber(frame.view_paint_offset_y) ||
      !IsFiniteNumber(frame.layout_zoom_factor) ||
      frame.layout_zoom_factor <= 0) {
    return 0;
  }
  uint64_t checkpoint_sequence = 0;
  LayoutCheckpointStorage::Update update = LayoutCheckpointStorage::Update::kNone;
  {
    LayoutCheckpointStorage& checkpoints = LayoutCheckpoints();
    base::AutoLock lock(checkpoints.lock);
    auto found = checkpoints.documents.find(document_node_id);
    if (found != checkpoints.documents.end()) {
      checkpoint_sequence = found->second.checkpoint_sequence;
      update = found->second.update;
      found->second.update = LayoutCheckpointStorage::Update::kNone;
    }
  }
  std::vector<size_t> changed_transforms;
  std::vector<size_t> changed_nodes;
  std::vector<size_t> valid_scroll_offsets;
  int unchanged_node_count = 0;
  uint64_t change_set_sequence = 0;
  uint64_t named_checkpoint_sequence = 0;
  {
    LayoutChangeStorage& storage = LayoutChanges();
    base::AutoLock lock(storage.lock);
    if (!storage.documents.contains(document_node_id) &&
        storage.documents.size() >= kMaximumLayoutChangeDocuments) {
      auto oldest = storage.documents.begin();
      for (auto entry = storage.documents.begin();
           entry != storage.documents.end(); ++entry) {
        if (entry->second.last_use < oldest->second.last_use) {
          oldest = entry;
        }
      }
      storage.documents.erase(oldest);
    }
    LayoutChangeStorage::Document& document =
        storage.documents[document_node_id];
    document.last_use = ++storage.use_clock;
    const uint64_t losses = LayoutEvidenceLosses().load();
    if (losses != document.losses_seen) {
      document.filter.ForgetStyles();
      document.losses_seen = losses;
    }
    if (checkpoint_sequence != document.checkpoint_sequence_seen) {
      named_checkpoint_sequence = checkpoint_sequence;
      document.checkpoint_sequence_seen = checkpoint_sequence;
    }
    for (size_t index = 0; index < transform_nodes.size(); ++index) {
      const LayoutTransformNode& node = transform_nodes[index];
      if (IsValidLayoutTransformNode(node) &&
          document.filter.TransformNodeChanged(
              node.id, HashLayoutTransformNode(node))) {
        changed_transforms.push_back(index);
      }
    }
    for (size_t index = 0; index < nodes.size(); ++index) {
      const LayoutChangedNode& node = nodes[index];
      if (!IsValidLayoutChangedNode(node)) {
        continue;
      }
      if (document.filter.NodeChanged(node.node.node_id,
                                      HashLayoutChangedNode(node))) {
        document.filter.ReduceToStyleChanges(nodes[index]);
        document.filter.ReduceToTextChanges(nodes[index]);
        changed_nodes.push_back(index);
      } else {
        ++unchanged_node_count;
      }
    }
    for (size_t index = 0; index < scroll_offsets.size(); ++index) {
      if (IsValidLayoutScrollOffset(scroll_offsets[index])) {
        valid_scroll_offsets.push_back(index);
      }
    }
    // A recalculation that was not walked is recorded by a change set even
    // when no record changed, so the update's presentation and interaction
    // state are recorded as they were with a checkpoint.
    if (changed_transforms.empty() && changed_nodes.empty() &&
        valid_scroll_offsets.empty() &&
        update != LayoutCheckpointStorage::Update::kNotWalked) {
      return 0;
    }
    change_set_sequence = storage.next_change_set_id++;
  }
  base::DictValue started = CreateLayoutChangesBasePayload(
      *client, change_set_sequence, document_node_id, document_token);
  started.Set("layoutCheckpointId",
              named_checkpoint_sequence == 0
                  ? base::Value()
                  : base::Value(LayoutCheckpointId(named_checkpoint_sequence)));
  started.Set("checkpointUpdate",
              IsCheckpointUpdateChangeSet(
                  update == LayoutCheckpointStorage::Update::kWalked,
                  named_checkpoint_sequence));
  started.Set("viewTransformNodeId",
              LayoutTransformNodeId(frame.view_transform_node_id));
  base::DictValue offset;
  offset.Set("x", frame.view_paint_offset_x);
  offset.Set("y", frame.view_paint_offset_y);
  started.Set("viewPaintOffset", std::move(offset));
  started.Set("layoutZoomFactor", frame.layout_zoom_factor);
  SendBlinkEvidence("browser.layout", "layout-changes-started",
                    std::move(started));
  for (size_t index : changed_transforms) {
    const LayoutTransformNode& node = transform_nodes[index];
    base::DictValue payload = CreateLayoutChangesBasePayload(
        *client, change_set_sequence, document_node_id, document_token);
    payload.Set("transformNodeId", LayoutTransformNodeId(node.id));
    payload.Set("parentTransformNodeId",
                node.parent_id == 0
                    ? base::Value()
                    : base::Value(LayoutTransformNodeId(node.parent_id)));
    base::ListValue matrix;
    for (double value : node.matrix) {
      matrix.Append(value);
    }
    payload.Set("matrix", std::move(matrix));
    payload.Set("flattensInheritedTransform",
                node.flattens_inherited_transform);
    payload.Set("scrollTranslation", node.scroll_translation);
    payload.Set("sticky", node.sticky);
    SendBlinkEvidence("browser.layout", "layout-transform-node",
                      std::move(payload));
  }
  for (size_t index : changed_nodes) {
    auto evidence = std::make_unique<LayoutChangedNodeEvidence>();
    evidence->channel = "browser.layout";
    evidence->event_type = "layout-node-changed";
    // The geometry and reasons add a fixed amount to the checkpoint form.
    evidence->bytes =
        EstimateLayoutCheckpointNodeBytes(nodes[index].node, document_token) +
        256;
    evidence->client = client;
    evidence->change_set_sequence = change_set_sequence;
    evidence->document_node_id = document_node_id;
    evidence->document_token = document_token;
    evidence->changed = std::move(nodes[index]);
    QueueBlinkEvidence(client, std::move(evidence));
  }
  for (size_t index : valid_scroll_offsets) {
    const LayoutScrollOffset& scroll = scroll_offsets[index];
    base::DictValue payload = CreateLayoutChangesBasePayload(
        *client, change_set_sequence, document_node_id, document_token);
    payload.Set("nodeId", scroll.node_id);
    base::DictValue scroll_offset;
    scroll_offset.Set("x", scroll.scroll_offset_x);
    scroll_offset.Set("y", scroll.scroll_offset_y);
    payload.Set("scrollOffset", std::move(scroll_offset));
    base::DictValue exposed;
    exposed.Set("x", scroll.web_exposed_scroll_offset_x);
    exposed.Set("y", scroll.web_exposed_scroll_offset_y);
    payload.Set("webExposedScrollOffset", std::move(exposed));
    base::DictValue origin;
    origin.Set("x", scroll.scroll_origin_x);
    origin.Set("y", scroll.scroll_origin_y);
    payload.Set("scrollOrigin", std::move(origin));
    payload.Set("effectiveZoom", scroll.effective_zoom);
    payload.Set("scrollTranslationNodeId",
                scroll.scroll_translation_node_id == 0
                    ? base::Value()
                    : base::Value(LayoutTransformNodeId(
                          scroll.scroll_translation_node_id)));
    // Protocol 0.49: the scroller's compositor element ID, as the decimal
    // text the compositor records name element IDs with.
    payload.Set("scrollElementId",
                scroll.scroll_element_id == 0
                    ? base::Value()
                    : base::Value(base::NumberToString(scroll.scroll_element_id)));
    SendBlinkEvidence("browser.layout", "layout-scroll-offset-changed",
                      std::move(payload));
  }
  base::DictValue completed = CreateLayoutChangesBasePayload(
      *client, change_set_sequence, document_node_id, std::move(document_token));
  completed.Set("notedNodeCount", noted_node_count);
  completed.Set("recordedNodeCount",
                base::saturated_cast<int>(changed_nodes.size()));
  completed.Set("unchangedNodeCount", unchanged_node_count);
  completed.Set("transformNodeCount",
                base::saturated_cast<int>(changed_transforms.size()));
  completed.Set("scrollOffsetCount",
                base::saturated_cast<int>(valid_scroll_offsets.size()));
  SendBlinkEvidence("browser.layout", "layout-changes-completed",
                    std::move(completed));
  return update == LayoutCheckpointStorage::Update::kWalked
             ? 0
             : LayoutChangeSetSource(change_set_sequence);
}


namespace {

// Every kept-active DidNotSwap call of a presentation request is recorded.
// The bound is the largest value the protocol's 32-bit counts hold.
constexpr int kMaximumPresentationNotSwappedRecords = 2147483647;

std::string PresentationRequestId(uint64_t request_sequence) {
  return "presentation-request-" + base::NumberToString(request_sequence);
}

// Converts a Chromium TimeTicks value to QueryPerformanceCounter ticks. On
// Windows a high-resolution TimeTicks is the counter value scaled to
// microseconds with no offset (base/time/time_win.cc), so the inverse is exact
// to within one microsecond. Any other TimeTicks source, or a null time, has
// no counter value.
base::Value PresentationCounterTicks(int64_t microseconds,
                                     bool high_resolution_ticks) {
  if (!high_resolution_ticks || microseconds <= 0) {
    return base::Value();
  }
  const int64_t frequency = QueryEvidenceFrequency();
  const int64_t whole_seconds = microseconds / 1000000;
  const int64_t remainder = microseconds % 1000000;
  return base::Value(base::NumberToString(whole_seconds * frequency +
                                          remainder * frequency / 1000000));
}

base::Value OptionalMicroseconds(int64_t microseconds) {
  return microseconds > 0 ? base::Value(base::NumberToString(microseconds))
                          : base::Value();
}

bool IsValidPresentationWidget(const PresentationWidgetIdentity& widget) {
  return widget.present ? !widget.local_root_frame_token.empty()
                        : !widget.page_popup;
}

base::DictValue CreatePresentationBasePayload(
    const RecorderPipeClient& client,
    uint64_t request_sequence,
    int document_node_id,
    std::string document_token,
    const PresentationWidgetIdentity& widget) {
  base::DictValue payload;
  payload.Set("context", CreateContext(client, document_node_id,
                                       std::move(document_token)));
  payload.Set("requestId", PresentationRequestId(request_sequence));
  if (widget.present) {
    payload.Set("widgetKind", widget.page_popup ? "page-popup" : "frame");
    payload.Set("frameSinkId",
                widget.page_popup
                    ? base::Value()
                    : base::Value(
                          base::NumberToString(widget.frame_sink_client_id) +
                          ":" + base::NumberToString(widget.frame_sink_id)));
    payload.Set("localRootFrameToken", widget.local_root_frame_token);
  } else {
    payload.Set("widgetKind", base::Value());
    payload.Set("frameSinkId", base::Value());
    payload.Set("localRootFrameToken", base::Value());
  }
  return payload;
}

base::ListValue PresentationFeedbackFlagNames(uint32_t flags) {
  // gfx::PresentationFeedback::Flags, in bit order.
  constexpr std::array<const char*, 5> kNames = {
      "vsync", "hw-clock", "hw-completion", "zero-copy", "failure"};
  base::ListValue names;
  for (size_t bit = 0; bit < kNames.size(); ++bit) {
    if (flags & (1u << bit)) {
      names.Append(kNames[bit]);
    }
  }
  return names;
}

}  // namespace

uint64_t BeginBlinkPresentationRequest(int document_node_id,
                                       std::string document_token,
                                       uint64_t layout_checkpoint_sequence,
                                       PresentationWidgetIdentity widget,
                                       std::string not_queued_reason,
                                       int source_frame_number,
                                       bool is_main_frame_widget,
                                       bool high_resolution_ticks) {
  A11Y_RECORDER_COST("BeginBlinkPresentationRequest");
  RecorderPipeClient* client = GetProcessRecorderClient();
  if (!client || document_node_id <= 0 || document_token.empty() ||
      layout_checkpoint_sequence == 0 ||
      layout_checkpoint_sequence == kLayoutChangeSetSourceBit ||
      !IsValidPresentationWidget(widget) || source_frame_number < -1) {
    return 0;
  }
  // From protocol 0.35 the source is a layout checkpoint or a layout change
  // set, marked by kLayoutChangeSetSourceBit; the record names one of them.
  const bool change_set_source =
      (layout_checkpoint_sequence & kLayoutChangeSetSourceBit) != 0;
  const uint64_t source_sequence =
      layout_checkpoint_sequence & ~kLayoutChangeSetSourceBit;
  // A request is queued only on a widget with a layer tree host; a request
  // without a widget names no widget and no frame number.
  const bool queued = not_queued_reason.empty();
  if ((queued && (!widget.present || source_frame_number < 0)) ||
      (!queued &&
       !IsOneOf(not_queued_reason, {"no-widget", "not-compositing"})) ||
      (not_queued_reason == "no-widget" &&
       (widget.present || source_frame_number != -1))) {
    return 0;
  }
  const uint64_t request_sequence = AssignPresentationRequestIdentity();
  base::DictValue payload = CreatePresentationBasePayload(
      *client, request_sequence, document_node_id, std::move(document_token),
      widget);
  payload.Set("layoutCheckpointId",
              change_set_source
                  ? base::Value()
                  : base::Value(LayoutCheckpointId(source_sequence)));
  payload.Set("layoutChangeSetId",
              change_set_source
                  ? base::Value(LayoutChangeSetId(source_sequence))
                  : base::Value());
  payload.Set("queued", queued);
  payload.Set("notQueuedReason",
              queued ? base::Value() : base::Value(not_queued_reason));
  payload.Set("sourceFrameNumber", source_frame_number >= 0
                                       ? base::Value(source_frame_number)
                                       : base::Value());
  payload.Set("isMainFrameWidget",
              widget.present ? base::Value(is_main_frame_widget)
                             : base::Value());
  payload.Set("highResolutionTicks", high_resolution_ticks);
  payload.Set("maximumNotSwappedRecords",
              kMaximumPresentationNotSwappedRecords);
  SendBlinkEvidence("browser.presentation", "presentation-requested",
                    std::move(payload));
  return queued ? request_sequence : 0;
}

void RecordBlinkPresentationNotSwapped(uint64_t request_sequence,
                                       int document_node_id,
                                       std::string document_token,
                                       PresentationWidgetIdentity widget,
                                       std::string reason,
                                       bool kept_active,
                                       int not_swapped_index,
                                       int64_t timestamp_microseconds,
                                       bool high_resolution_ticks) {
  A11Y_RECORDER_COST("RecordBlinkPresentationNotSwapped");
  RecorderPipeClient* client = GetProcessRecorderClient();
  if (!client || request_sequence == 0 || document_node_id <= 0 ||
      document_token.empty() || !widget.present ||
      !IsValidPresentationWidget(widget) || not_swapped_index < 0 ||
      !IsOneOf(reason, {"swap-fails", "commit-fails", "commit-no-update",
                        "activation-fails"})) {
    return;
  }
  // A broken promise ends the request, so its record is always sent; a
  // kept-active one is sent while its index can be stated.
  if (kept_active &&
      not_swapped_index >= kMaximumPresentationNotSwappedRecords) {
    return;
  }
  base::DictValue payload = CreatePresentationBasePayload(
      *client, request_sequence, document_node_id, std::move(document_token),
      widget);
  payload.Set("reason", std::move(reason));
  payload.Set("action", kept_active ? "kept-active" : "broken");
  payload.Set("notSwappedIndex", not_swapped_index);
  payload.Set("notSwappedCount", not_swapped_index + 1);
  payload.Set("timestampTicks", PresentationCounterTicks(
                                    timestamp_microseconds,
                                    high_resolution_ticks));
  payload.Set("timestampTimeTicksMicroseconds",
              OptionalMicroseconds(timestamp_microseconds));
  SendBlinkEvidence("browser.presentation", "presentation-not-swapped",
                    std::move(payload));
}

void RecordBlinkPresentationSwapped(uint64_t request_sequence,
                                    int document_node_id,
                                    std::string document_token,
                                    PresentationWidgetIdentity widget,
                                    uint32_t frame_token,
                                    int not_swapped_count) {
  A11Y_RECORDER_COST("RecordBlinkPresentationSwapped");
  RecorderPipeClient* client = GetProcessRecorderClient();
  if (!client || request_sequence == 0 || document_node_id <= 0 ||
      document_token.empty() || !widget.present ||
      !IsValidPresentationWidget(widget) || frame_token == 0 ||
      not_swapped_count < 0) {
    return;
  }
  base::DictValue payload = CreatePresentationBasePayload(
      *client, request_sequence, document_node_id, std::move(document_token),
      widget);
  payload.Set("frameToken", base::NumberToString(frame_token));
  payload.Set("notSwappedCount", not_swapped_count);
  SendBlinkEvidence("browser.presentation", "presentation-swapped",
                    std::move(payload));
}

void RecordBlinkPresentationFeedback(uint64_t request_sequence,
                                     int document_node_id,
                                     std::string document_token,
                                     PresentationWidgetIdentity widget,
                                     uint32_t frame_token,
                                     PresentationFeedbackTiming timing,
                                     int not_swapped_count,
                                     bool high_resolution_ticks) {
  A11Y_RECORDER_COST("RecordBlinkPresentationFeedback");
  RecorderPipeClient* client = GetProcessRecorderClient();
  if (!client || request_sequence == 0 || document_node_id <= 0 ||
      document_token.empty() || !widget.present ||
      !IsValidPresentationWidget(widget) || frame_token == 0 ||
      not_swapped_count < 0 || timing.interval_microseconds < 0) {
    return;
  }
  base::DictValue payload = CreatePresentationBasePayload(
      *client, request_sequence, document_node_id, std::move(document_token),
      widget);
  payload.Set("frameToken", base::NumberToString(frame_token));
  payload.Set("presentedTicks",
              PresentationCounterTicks(timing.presented_microseconds,
                                       high_resolution_ticks));
  payload.Set("presentedTimeTicksMicroseconds",
              OptionalMicroseconds(timing.presented_microseconds));
  payload.Set("intervalMicroseconds",
              base::NumberToString(timing.interval_microseconds));
  payload.Set("flags", PresentationFeedbackFlagNames(timing.flags));
  payload.Set("receivedCompositorFrameTicks",
              PresentationCounterTicks(
                  timing.received_compositor_frame_microseconds,
                  high_resolution_ticks));
  payload.Set("drawStartTicks",
              PresentationCounterTicks(timing.draw_start_microseconds,
                                       high_resolution_ticks));
  payload.Set("swapStartTicks",
              PresentationCounterTicks(timing.swap_start_microseconds,
                                       high_resolution_ticks));
  payload.Set("swapEndTicks",
              PresentationCounterTicks(timing.swap_end_microseconds,
                                       high_resolution_ticks));
  payload.Set("highResolutionTicks", high_resolution_ticks);
  payload.Set("notSwappedCount", not_swapped_count);
  SendBlinkEvidence("browser.presentation", "presentation-feedback",
                    std::move(payload));
}


namespace {

// The compositor's state the bridge keeps between frames, per compositor:
// the widget its presentation requests name, the values last recorded, and
// the recorded frames whose presentation has not been reported.
struct CompositorRecordState {
  PresentationWidgetIdentity widget;
  std::map<std::pair<uint64_t, std::string>, CompositorDrawnValue> recorded;
  std::set<uint32_t> awaiting_presentation;
};

base::Lock& CompositorRecordLock() {
  static base::NoDestructor<base::Lock> lock;
  return *lock;
}

std::map<int, CompositorRecordState>& CompositorRecordStates() {
  static base::NoDestructor<std::map<int, CompositorRecordState>> states;
  return *states;
}

bool SameCompositorValue(const CompositorDrawnValue& left,
                         const CompositorDrawnValue& right) {
  if (left.present != right.present || left.numbers != right.numbers ||
      left.filters.size() != right.filters.size()) {
    return false;
  }
  for (size_t index = 0; index < left.filters.size(); ++index) {
    if (left.filters[index].type != right.filters[index].type ||
        left.filters[index].numbers != right.filters[index].numbers) {
      return false;
    }
  }
  return true;
}

base::ListValue CompositorNumbers(const std::vector<double>& numbers) {
  base::ListValue list;
  for (const double number : numbers) {
    list.Append(number);
  }
  return list;
}

base::Value CompositorValueJson(const CompositorDrawnValue& value) {
  if (!value.present) {
    return base::Value();
  }
  if (value.property == "opacity" && value.numbers.size() == 1) {
    return base::Value(value.numbers[0]);
  }
  if (value.property == "image-frame" && value.numbers.size() == 1) {
    return base::Value(static_cast<int>(value.numbers[0]));
  }
  if (value.property == "scroll-offset" &&
      (value.numbers.size() == 2 || value.numbers.size() == 4)) {
    base::DictValue offset;
    offset.Set("x", value.numbers[0]);
    offset.Set("y", value.numbers[1]);
    if (value.numbers.size() == 4) {
      // Protocol 0.50: whether the compositor scrolls the node, and the
      // reasons Chromium gives for repainting it on the main thread
      // instead (cc::MainThreadRepaintReason), one bit each in its order.
      offset.Set("isComposited", value.numbers[2] != 0);
      const auto bits = static_cast<unsigned>(value.numbers[3]);
      base::ListValue reasons;
      static constexpr std::array<const char*, 4> kReasonNames = {
          "has-background-attachment-fixed-objects",
          "not-opaque-for-text-and-lcd-text",
          "prefer-non-composited-scrolling",
          "background-needs-repaint-on-scroll",
      };
      for (size_t bit = 0; bit < kReasonNames.size(); ++bit) {
        if (bits & (1u << bit)) {
          reasons.Append(kReasonNames[bit]);
        }
      }
      offset.Set("mainThreadRepaintReasons", std::move(reasons));
    }
    return base::Value(std::move(offset));
  }
  if (value.property == "background-color-progress" ||
      value.property == "clip-path-progress") {
    base::DictValue progress;
    progress.Set("progress", value.numbers.size() == 1
                                 ? base::Value(value.numbers[0])
                                 : base::Value());
    return base::Value(std::move(progress));
  }
  if (value.property == "filter" || value.property == "backdrop-filter") {
    base::ListValue operations;
    for (const CompositorFilterOperation& operation : value.filters) {
      base::DictValue entry;
      entry.Set("type", operation.type);
      entry.Set("numbers", CompositorNumbers(operation.numbers));
      operations.Append(std::move(entry));
    }
    return base::Value(std::move(operations));
  }
  return base::Value(CompositorNumbers(value.numbers));
}

base::Value CompositorWidgetJson(const PresentationWidgetIdentity& widget) {
  if (!widget.present) {
    return base::Value();
  }
  base::DictValue entry;
  entry.Set("widgetKind", widget.page_popup ? "page-popup" : "frame");
  entry.Set("frameSinkId",
            widget.page_popup
                ? base::Value()
                : base::Value(
                      base::NumberToString(widget.frame_sink_client_id) + ":" +
                      base::NumberToString(widget.frame_sink_id)));
  entry.Set("localRootFrameToken", widget.local_root_frame_token);
  return base::Value(std::move(entry));
}

bool IsCompositorProperty(const std::string& property) {
  return IsOneOf(property,
                 {"transform", "opacity", "filter", "backdrop-filter",
                  "scroll-offset", "background-color-progress",
                  "clip-path-progress", "image-frame"});
}

}  // namespace

void RegisterCompositorWidget(int layer_tree_host_id,
                              PresentationWidgetIdentity widget) {
  if (!GetProcessRecorderClient() || layer_tree_host_id <= 0 ||
      !widget.present || !IsValidPresentationWidget(widget)) {
    return;
  }
  base::AutoLock lock(CompositorRecordLock());
  CompositorRecordStates()[layer_tree_host_id].widget = std::move(widget);
}

void RecordCompositorAnimationStarted(
    int document_node_id,
    std::string document_token,
    int node_id,
    int compositor_animation_id,
    std::vector<CompositorKeyframeModelFacts> keyframe_models) {
  A11Y_RECORDER_COST("RecordCompositorAnimationStarted");
  RecorderPipeClient* client = GetProcessRecorderClient();
  if (!client || document_node_id <= 0 || document_token.empty() ||
      node_id <= 0 || keyframe_models.empty()) {
    return;
  }
  base::DictValue payload;
  payload.Set("context", CreateContext(*client, document_node_id,
                                       std::move(document_token)));
  payload.Set("nodeId", node_id);
  payload.Set("compositorAnimationId",
              compositor_animation_id > 0 ? base::Value(compositor_animation_id)
                                          : base::Value());
  base::ListValue models;
  for (CompositorKeyframeModelFacts& model : keyframe_models) {
    base::DictValue entry;
    entry.Set("keyframeModelId", model.keyframe_model_id);
    entry.Set("targetProperty", std::move(model.target_property));
    entry.Set("elementId", base::NumberToString(model.element_id));
    entry.Set("elementIdNamespace", std::move(model.element_id_namespace));
    models.Append(std::move(entry));
  }
  payload.Set("keyframeModels", std::move(models));
  SendBlinkEvidence("browser.compositor", "compositor-animation-started",
                    std::move(payload));
}

void RecordCompositorAnimationEnded(int document_node_id,
                                    std::string document_token,
                                    int node_id,
                                    int compositor_animation_id,
                                    std::vector<int> keyframe_model_ids) {
  A11Y_RECORDER_COST("RecordCompositorAnimationEnded");
  RecorderPipeClient* client = GetProcessRecorderClient();
  if (!client || keyframe_model_ids.empty()) {
    return;
  }
  base::DictValue payload;
  payload.Set("context", CreateContext(*client, document_node_id,
                                       std::move(document_token)));
  payload.Set("nodeId", node_id > 0 ? base::Value(node_id) : base::Value());
  payload.Set("compositorAnimationId",
              compositor_animation_id > 0 ? base::Value(compositor_animation_id)
                                          : base::Value());
  base::ListValue ids;
  for (const int id : keyframe_model_ids) {
    ids.Append(id);
  }
  payload.Set("keyframeModelIds", std::move(ids));
  SendBlinkEvidence("browser.compositor", "compositor-animation-ended",
                    std::move(payload));
}

// Protocol 0.53 (slice 4g): the last recorded description of each
// animation of this renderer, by sequence number, so that an animation is
// recorded again only when something other than its current time,
// progress, and current iteration changed.
namespace {

struct AnimationRecordStorage {
  base::Lock lock;
  std::unordered_map<unsigned, AnimationFacts> last;
};

AnimationRecordStorage& GetAnimationRecordStorage() {
  static base::NoDestructor<AnimationRecordStorage> storage;
  return *storage;
}

bool SameAnimationDescription(const AnimationFacts& a,
                              const AnimationFacts& b) {
  return a.document_node_id == b.document_node_id &&
         a.document_token == b.document_token && a.kind == b.kind &&
         a.name == b.name && a.id == b.id &&
         a.target_node_id == b.target_node_id &&
         a.pseudo_element == b.pseudo_element &&
         a.play_state == b.play_state && a.pending == b.pending &&
         a.playback_rate == b.playback_rate &&
         a.start_time_milliseconds == b.start_time_milliseconds &&
         a.timeline_kind == b.timeline_kind &&
         a.timeline_zero_microseconds == b.timeline_zero_microseconds &&
         a.timeline_playback_rate == b.timeline_playback_rate &&
         a.timeline_source_node_id == b.timeline_source_node_id &&
         a.timeline_subject_node_id == b.timeline_subject_node_id &&
         a.timeline_axis == b.timeline_axis &&
         a.has_effect == b.has_effect &&
         a.delay_milliseconds == b.delay_milliseconds &&
         a.end_delay_milliseconds == b.end_delay_milliseconds &&
         a.iteration_start == b.iteration_start &&
         a.iterations == b.iterations &&
         a.duration_milliseconds == b.duration_milliseconds &&
         a.direction == b.direction && a.fill == b.fill &&
         a.easing == b.easing &&
         a.compositor_animation_id == b.compositor_animation_id;
}

base::Value FiniteOrNull(std::optional<double> value) {
  return value && std::isfinite(*value) ? base::Value(*value) : base::Value();
}

base::Value NodeIdOrNull(int node_id) {
  return node_id > 0 ? base::Value(node_id) : base::Value();
}

base::Value TextOrNull(const std::string& text) {
  return text.empty() ? base::Value() : base::Value(text);
}

}  // namespace

void RecordAnimationUpdated(AnimationFacts facts) {
  A11Y_RECORDER_COST("RecordAnimationUpdated");
  RecorderPipeClient* client = GetProcessRecorderClient();
  if (!client || facts.document_node_id <= 0 ||
      facts.document_token.empty() || facts.sequence_number == 0) {
    return;
  }
  {
    AnimationRecordStorage& storage = GetAnimationRecordStorage();
    base::AutoLock locked(storage.lock);
    auto found = storage.last.find(facts.sequence_number);
    if (found != storage.last.end() &&
        SameAnimationDescription(found->second, facts)) {
      return;
    }
    storage.last[facts.sequence_number] = facts;
  }
  base::DictValue payload;
  payload.Set("context", CreateContext(*client, facts.document_node_id,
                                       facts.document_token));
  payload.Set("sequenceNumber", base::NumberToString(facts.sequence_number));
  payload.Set("kind", facts.kind);
  payload.Set("name", TextOrNull(facts.name));
  payload.Set("id", TextOrNull(facts.id));
  payload.Set("targetNodeId", NodeIdOrNull(facts.target_node_id));
  payload.Set("pseudoElement", TextOrNull(facts.pseudo_element));
  payload.Set("playState", facts.play_state);
  payload.Set("pending", facts.pending);
  payload.Set("playbackRate", FiniteOrNull(facts.playback_rate));
  payload.Set("startTimeMilliseconds",
              FiniteOrNull(facts.start_time_milliseconds));
  payload.Set("currentTimeMilliseconds",
              FiniteOrNull(facts.current_time_milliseconds));
  base::DictValue timeline;
  timeline.Set("kind", facts.timeline_kind);
  timeline.Set("zeroTicks",
               facts.timeline_kind == "document"
                   ? PresentationCounterTicks(facts.timeline_zero_microseconds,
                                              facts.high_resolution_ticks)
                   : base::Value());
  timeline.Set("zeroTimeTicksMicroseconds",
               facts.timeline_kind == "document"
                   ? OptionalMicroseconds(facts.timeline_zero_microseconds)
                   : base::Value());
  timeline.Set("playbackRate", FiniteOrNull(facts.timeline_playback_rate));
  timeline.Set("sourceNodeId", NodeIdOrNull(facts.timeline_source_node_id));
  timeline.Set("subjectNodeId", NodeIdOrNull(facts.timeline_subject_node_id));
  timeline.Set("axis", TextOrNull(facts.timeline_axis));
  payload.Set("timeline", std::move(timeline));
  if (facts.has_effect) {
    base::DictValue effect;
    effect.Set("delayMilliseconds", FiniteOrNull(facts.delay_milliseconds));
    effect.Set("endDelayMilliseconds",
               FiniteOrNull(facts.end_delay_milliseconds));
    effect.Set("iterationStart", FiniteOrNull(facts.iteration_start));
    effect.Set("iterations", FiniteOrNull(facts.iterations));
    effect.Set("durationMilliseconds",
               FiniteOrNull(facts.duration_milliseconds));
    effect.Set("direction", facts.direction);
    effect.Set("fill", facts.fill);
    effect.Set("easing", facts.easing);
    effect.Set("progress", FiniteOrNull(facts.progress));
    effect.Set("currentIteration", FiniteOrNull(facts.current_iteration));
    payload.Set("effect", std::move(effect));
  } else {
    payload.Set("effect", base::Value());
  }
  payload.Set("compositorAnimationId",
              NodeIdOrNull(facts.compositor_animation_id));
  SendBlinkEvidence("browser.animation", "animation-updated",
                    std::move(payload));
}

void RecordAnimationRemoved(unsigned sequence_number) {
  A11Y_RECORDER_COST("RecordAnimationRemoved");
  RecorderPipeClient* client = GetProcessRecorderClient();
  if (!client || sequence_number == 0) {
    return;
  }
  AnimationFacts last;
  {
    AnimationRecordStorage& storage = GetAnimationRecordStorage();
    base::AutoLock locked(storage.lock);
    auto found = storage.last.find(sequence_number);
    if (found == storage.last.end()) {
      return;
    }
    last = std::move(found->second);
    storage.last.erase(found);
  }
  base::DictValue payload;
  payload.Set("context", CreateContext(*client, last.document_node_id,
                                       std::move(last.document_token)));
  payload.Set("sequenceNumber", base::NumberToString(sequence_number));
  SendBlinkEvidence("browser.animation", "animation-removed",
                    std::move(payload));
}

void RecordCompositorFrame(int layer_tree_host_id,
                           uint32_t frame_token,
                           int64_t begin_frame_microseconds,
                           int source_frame_number,
                           bool high_resolution_ticks,
                           std::vector<CompositorDrawnValue> values) {
  A11Y_RECORDER_COST("RecordCompositorFrame");
  RecorderPipeClient* client = GetProcessRecorderClient();
  if (!client || layer_tree_host_id <= 0 || frame_token == 0) {
    return;
  }
  base::ListValue changes;
  PresentationWidgetIdentity widget;
  {
    base::AutoLock lock(CompositorRecordLock());
    CompositorRecordState& state = CompositorRecordStates()[layer_tree_host_id];
    for (CompositorDrawnValue& value : values) {
      if (!IsCompositorProperty(value.property)) {
        continue;
      }
      const auto key = std::make_pair(value.element_id, value.property);
      auto recorded = state.recorded.find(key);
      if (!value.present) {
        // An element never recorded, or already recorded as gone, is not a
        // change.
        if (recorded == state.recorded.end()) {
          continue;
        }
        state.recorded.erase(recorded);
      } else if (recorded != state.recorded.end() &&
                 SameCompositorValue(recorded->second, value)) {
        continue;
      }
      base::DictValue change;
      // An animated image's frame is the paint image's, which no compositor
      // element names.
      change.Set(value.property == "image-frame" ? "paintImageId" : "elementId",
                 base::NumberToString(value.element_id));
      change.Set("property", value.property);
      change.Set("value", CompositorValueJson(value));
      changes.Append(std::move(change));
      if (value.present) {
        state.recorded[key] = std::move(value);
      }
    }
    if (changes.empty()) {
      return;
    }
    // Viz reports each submitted frame once, presented or failed
    // (CompositorFrameSinkSupport::DidPresentCompositorFrame). A frame whose
    // report never came, as when its renderer closed, is not awaited for
    // ever: the oldest is let go past this bound, which no report is so far
    // behind.
    constexpr size_t kMaximumAwaitedCompositorFrames = 1024;
    state.awaiting_presentation.insert(frame_token);
    if (state.awaiting_presentation.size() > kMaximumAwaitedCompositorFrames) {
      state.awaiting_presentation.erase(state.awaiting_presentation.begin());
    }
    widget = state.widget;
  }
  base::DictValue payload;
  payload.Set("context", CreateContext(*client, 0));
  payload.Set("layerTreeHostId", layer_tree_host_id);
  payload.Set("widget", CompositorWidgetJson(widget));
  payload.Set("frameToken", base::NumberToString(frame_token));
  payload.Set("sourceFrameNumber", source_frame_number);
  payload.Set("beginFrameTicks",
              PresentationCounterTicks(begin_frame_microseconds,
                                       high_resolution_ticks));
  payload.Set("beginFrameTimeTicksMicroseconds",
              OptionalMicroseconds(begin_frame_microseconds));
  payload.Set("highResolutionTicks", high_resolution_ticks);
  payload.Set("changes", std::move(changes));
  SendBlinkEvidence("browser.compositor", "compositor-frame",
                    std::move(payload));
}

void RecordCompositorFramePresented(int layer_tree_host_id,
                                    uint32_t frame_token,
                                    int64_t presented_microseconds,
                                    bool failed,
                                    bool high_resolution_ticks) {
  A11Y_RECORDER_COST("RecordCompositorFramePresented");
  RecorderPipeClient* client = GetProcessRecorderClient();
  if (!client || layer_tree_host_id <= 0 || frame_token == 0) {
    return;
  }
  PresentationWidgetIdentity widget;
  {
    base::AutoLock lock(CompositorRecordLock());
    auto state = CompositorRecordStates().find(layer_tree_host_id);
    if (state == CompositorRecordStates().end() ||
        !state->second.awaiting_presentation.erase(frame_token)) {
      return;
    }
    widget = state->second.widget;
  }
  base::DictValue payload;
  payload.Set("context", CreateContext(*client, 0));
  payload.Set("layerTreeHostId", layer_tree_host_id);
  payload.Set("widget", CompositorWidgetJson(widget));
  payload.Set("frameToken", base::NumberToString(frame_token));
  payload.Set("failed", failed);
  payload.Set("presentedTicks",
              failed ? base::Value()
                     : PresentationCounterTicks(presented_microseconds,
                                                high_resolution_ticks));
  payload.Set("presentedTimeTicksMicroseconds",
              failed ? base::Value()
                     : OptionalMicroseconds(presented_microseconds));
  payload.Set("highResolutionTicks", high_resolution_ticks);
  SendBlinkEvidence("browser.compositor", "compositor-frame-presented",
                    std::move(payload));
}

void RecordPaintWorkletPainted(PaintWorkletPaintedFacts facts) {
  A11Y_RECORDER_COST("RecordPaintWorkletPainted");
  RecorderPipeClient* client = GetProcessRecorderClient();
  if (!client || facts.element_id == 0 ||
      (facts.property != "background-color" &&
       facts.property != "clip-path")) {
    return;
  }
  base::DictValue payload;
  payload.Set("context", CreateContext(*client, 0));
  payload.Set("elementId", base::NumberToString(facts.element_id));
  payload.Set("property", facts.property);
  payload.Set("progress", facts.progress ? base::Value(*facts.progress)
                                         : base::Value());
  base::DictValue value;
  if (facts.property == "background-color") {
    value.Set("color", CompositorNumbers(facts.color));
  } else {
    value.Set("fillType", std::move(facts.fill_type));
    base::ListValue verbs;
    for (std::string& verb : facts.verbs) {
      verbs.Append(std::move(verb));
    }
    value.Set("verbs", std::move(verbs));
    value.Set("points", CompositorNumbers(facts.points));
    value.Set("conicWeights", CompositorNumbers(facts.conic_weights));
    base::DictValue translation;
    translation.Set("x", facts.translate_x);
    translation.Set("y", facts.translate_y);
    value.Set("translation", std::move(translation));
    value.Set("drawnAsRoundedRect", facts.drawn_as_rounded_rect);
  }
  payload.Set("value", std::move(value));
  SendBlinkEvidence("browser.compositor", "paint-worklet-painted",
                    std::move(payload));
}

namespace {

std::string InteractionCheckpointId(uint64_t checkpoint_sequence) {
  return "interaction-checkpoint-" + base::NumberToString(checkpoint_sequence);
}

base::DictValue CreateInteractionCheckpointBasePayload(
    const RecorderPipeClient& client,
    uint64_t checkpoint_sequence,
    int document_node_id,
    std::string document_token) {
  base::DictValue payload;
  payload.Set("context", CreateContext(client, document_node_id,
                                       std::move(document_token)));
  payload.Set("checkpointId", InteractionCheckpointId(checkpoint_sequence));
  return payload;
}

}  // namespace

uint64_t BeginBlinkInteractionCheckpoint(int document_node_id,
                                         std::string document_token,
                                         std::string source_channel,
                                         uint64_t source_checkpoint_sequence,
                                         std::string reason,
                                         InteractionCheckpointState state,
                                         int maximum_text_controls,
                                         int maximum_value_length) {
  static const int recorder_span_slot = CostSpanSlot("span:interaction-checkpoint");
  BeginCostSpan(recorder_span_slot);
  A11Y_RECORDER_COST("BeginBlinkInteractionCheckpoint");
  RecorderPipeClient* client = GetProcessRecorderClient();
  if (!client || document_node_id <= 0 || document_token.empty() ||
      maximum_text_controls <= 0 || maximum_value_length <= 0) {
    return 0;
  }
  // The reason must be one the source's channel uses, so the snapshot cannot
  // name a reason its source did not record. From protocol 0.35 the source
  // is a DOM checkpoint, a mutation delivery that was not walked (sequence
  // zero), a layout checkpoint, or a layout change set, marked by
  // kLayoutChangeSetSourceBit.
  const bool change_set_source =
      (source_checkpoint_sequence & kLayoutChangeSetSourceBit) != 0;
  const uint64_t source_sequence =
      source_checkpoint_sequence & ~kLayoutChangeSetSourceBit;
  base::Value source_checkpoint_id;
  base::Value source_change_set_id;
  if (source_channel == "browser.dom" &&
      IsOneOf(reason,
              {"started-parsing", "finished-parsing", "post-mutation"}) &&
      !change_set_source) {
    if (source_sequence != 0) {
      source_checkpoint_id = base::Value(DomCheckpointId(source_sequence));
    } else if (reason != "post-mutation") {
      return 0;
    }
  } else if (source_channel == "browser.layout" &&
             reason == "rendering-update" && source_sequence != 0) {
    if (change_set_source) {
      source_change_set_id = base::Value(LayoutChangeSetId(source_sequence));
    } else {
      source_checkpoint_id = base::Value(LayoutCheckpointId(source_sequence));
    }
  } else {
    return 0;
  }
  if (!IsOneOf(state.last_focus_type,
               {"none", "script", "forward", "backward", "spatial-navigation",
                "mouse", "access-key", "page"}) ||
      !IsOneOf(state.selection_type, {"none", "caret", "range"})) {
    return 0;
  }
  const bool has_positions = state.selection_type != "none";
  if (has_positions &&
      (state.anchor_node_id <= 0 || state.focus_node_id <= 0 ||
       state.anchor_offset < 0 || state.focus_offset < 0)) {
    return 0;
  }
  const bool has_focus = state.focused_node_id > 0;
  const uint64_t checkpoint_sequence = AssignInteractionCheckpointIdentity();
  base::DictValue payload = CreateInteractionCheckpointBasePayload(
      *client, checkpoint_sequence, document_node_id,
      std::move(document_token));
  payload.Set("sourceCheckpointId", std::move(source_checkpoint_id));
  payload.Set("sourceChangeSetId", std::move(source_change_set_id));
  payload.Set("sourceChannel", std::move(source_channel));
  payload.Set("reason", std::move(reason));
  payload.Set("documentHasFocus", state.document_has_focus);
  payload.Set("focusedNodeId", OptionalNodeId(state.focused_node_id));
  payload.Set("focusVisible", has_focus && state.focus_visible);
  payload.Set("activeDescendantNodeId",
              OptionalNodeId(has_focus ? state.active_descendant_node_id : 0));
  payload.Set("lastFocusType", std::move(state.last_focus_type));
  payload.Set("selectionType", state.selection_type);
  payload.Set("anchorNodeId", has_positions ? base::Value(state.anchor_node_id)
                                            : base::Value());
  payload.Set("anchorOffset", has_positions ? base::Value(state.anchor_offset)
                                            : base::Value());
  payload.Set("focusNodeId", has_positions ? base::Value(state.focus_node_id)
                                           : base::Value());
  payload.Set("focusOffset", has_positions ? base::Value(state.focus_offset)
                                           : base::Value());
  payload.Set("directional", state.directional);
  payload.Set("maximumTextControls", maximum_text_controls);
  payload.Set("maximumValueLength", maximum_value_length);
  SendBlinkEvidence("browser.interaction", "interaction-checkpoint-started",
                    std::move(payload));
  return checkpoint_sequence;
}

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
    std::string selection_direction) {
  A11Y_RECORDER_COST("RecordBlinkInteractionCheckpointTextControl");
  RecorderPipeClient* client = GetProcessRecorderClient();
  if (!client || checkpoint_sequence == 0 || document_node_id <= 0 ||
      document_token.empty() || text_control_index < 0 || node_id <= 0 ||
      control_type.empty() || value_length < 0 || selection_start < 0 ||
      selection_end < selection_start ||
      !IsOneOf(selection_direction, {"none", "forward", "backward"})) {
    return;
  }
  base::DictValue payload = CreateInteractionCheckpointBasePayload(
      *client, checkpoint_sequence, document_node_id,
      std::move(document_token));
  payload.Set("textControlIndex", text_control_index);
  payload.Set("nodeId", node_id);
  payload.Set("controlType", std::move(control_type));
  SetTruncatedTextProperties(payload, "value", "valueLength", "valueTruncated",
                             std::move(value), value_length, value_truncated);
  payload.Set("selectionStart", selection_start);
  payload.Set("selectionEnd", selection_end);
  payload.Set("selectionDirection", std::move(selection_direction));
  SendBlinkEvidence("browser.interaction",
                    "interaction-checkpoint-text-control", std::move(payload));
}

void CompleteBlinkInteractionCheckpoint(uint64_t checkpoint_sequence,
                                        int document_node_id,
                                        std::string document_token,
                                        int text_control_count,
                                        bool truncated,
                                        int maximum_text_controls) {
  static const int recorder_span_slot = CostSpanSlot("span:interaction-checkpoint");
  // The span ends when this function returns, so it includes the completion.
  struct SpanEnd {
    ~SpanEnd() { EndCostSpan(recorder_span_slot); }
  } recorder_span_end;
  A11Y_RECORDER_COST("CompleteBlinkInteractionCheckpoint");
  RecorderPipeClient* client = GetProcessRecorderClient();
  if (!client || checkpoint_sequence == 0 || document_node_id <= 0 ||
      document_token.empty() || text_control_count < 0 ||
      maximum_text_controls <= 0 ||
      text_control_count > maximum_text_controls) {
    return;
  }
  base::DictValue payload = CreateInteractionCheckpointBasePayload(
      *client, checkpoint_sequence, document_node_id,
      std::move(document_token));
  payload.Set("textControlCount", text_control_count);
  payload.Set("truncated", truncated);
  payload.Set("maximumTextControls", maximum_text_controls);
  SendBlinkEvidence("browser.interaction", "interaction-checkpoint-completed",
                    std::move(payload));
}


namespace {

// Network records name the Blink loader's per-process request counter as a
// decimal string, since it is a 64-bit value.
std::string InspectorId(uint64_t inspector_id) {
  return base::NumberToString(inspector_id);
}

// A byte count or other 64-bit quantity as a JSON number. Values up to 2^53
// are exact.
base::Value NetworkQuantity(int64_t value) {
  return base::Value(static_cast<double>(value));
}

// A transferred byte count, or null when Chromium reports none. The network
// service reports -1 when no data crossed the network, as for a chrome:// or
// otherwise locally served response.
base::Value NetworkTransferredLength(int64_t value) {
  if (value < 0) {
    return base::Value();
  }
  return NetworkQuantity(value);
}

// A microsecond offset as milliseconds, or null for an unobserved time.
base::Value NetworkMilliseconds(int64_t microseconds) {
  if (microseconds == kNetworkTimeUnobserved) {
    return base::Value();
  }
  return base::Value(static_cast<double>(microseconds) / 1000.0);
}

// Sets a header list, its full length, and whether it was cut. A value the
// classifier withholds is dropped here, before it reaches any record.
void SetNetworkHeaders(base::DictValue& payload,
                       const char* list_key,
                       const char* count_key,
                       const char* truncated_key,
                       std::vector<NetworkHeader> headers) {
  const size_t count = headers.size();
  const bool truncated = count > kMaximumNetworkHeadersPerRecord;
  if (truncated) {
    headers.resize(kMaximumNetworkHeadersPerRecord);
  }
  base::ListValue list;
  for (NetworkHeader& header : headers) {
    const network_text::HeaderRedaction redaction =
        network_text::ClassifyHeader(header.name, header.value);
    base::DictValue entry;
    entry.Set("name", std::move(header.name));
    if (redaction == network_text::HeaderRedaction::kNone) {
      entry.Set("value", std::move(header.value));
      entry.Set("valueRedacted", false);
      entry.Set("redactionReason", base::Value());
    } else {
      entry.Set("value", base::Value());
      entry.Set("valueRedacted", true);
      entry.Set("redactionReason",
                std::string(network_text::HeaderRedactionName(redaction)));
    }
    list.Append(std::move(entry));
  }
  payload.Set(count_key, base::checked_cast<int>(count));
  payload.Set(list_key, std::move(list));
  payload.Set(truncated_key, truncated);
}

void SetNetworkCookies(base::DictValue& payload,
                       std::vector<CookieAccessEntry> cookies) {
  const size_t count = cookies.size();
  const bool truncated = count > kMaximumCookiesPerRecord;
  if (truncated) {
    cookies.resize(kMaximumCookiesPerRecord);
  }
  base::ListValue entries;
  for (CookieAccessEntry& entry : cookies) {
    entries.Append(CreateCookieAccessEntry(std::move(entry)));
  }
  payload.Set("cookieCount", base::checked_cast<int>(count));
  payload.Set("cookies", std::move(entries));
  payload.Set("cookiesTruncated", truncated);
}

std::string NormalizeNetworkContextKind(std::string context_kind) {
  if (IsOneOf(context_kind, {"window", "dedicated-worker", "shared-worker",
                             "service-worker", "worklet"})) {
    return context_kind;
  }
  return "other";
}

base::DictValue CreateNetworkRendererContext(const RecorderPipeClient& client,
                                             const NetworkScope& scope,
                                             const CookieCallOrigin* origin) {
  base::DictValue context =
      CreateContext(client, scope.document_node_id, scope.document_token);
  if (origin && !NormalizeExecutionWorldKind(origin->world_kind).empty()) {
    context.Set("executionWorldId", ExecutionWorldId(origin->world_id));
  }
  return context;
}

base::DictValue CreateNetworkScope(NetworkScope scope) {
  base::DictValue value;
  value.Set("contextKind",
            NormalizeNetworkContextKind(std::move(scope.context_kind)));
  value.Set("workerToken", NonEmptyString(std::move(scope.worker_token)));
  value.Set("globalObjectUrl",
            NonEmptyString(std::move(scope.global_object_url)));
  return value;
}

base::Value CreateNetworkLoadTiming(const NetworkLoadTiming& timing) {
  if (!timing.present) {
    return base::Value();
  }
  base::DictValue value;
  value.Set("requestStartBeforeRecordMilliseconds",
            NetworkMilliseconds(timing.request_start_before_record));
  value.Set("proxyStart", NetworkMilliseconds(timing.proxy_start));
  value.Set("proxyEnd", NetworkMilliseconds(timing.proxy_end));
  value.Set("domainLookupStart",
            NetworkMilliseconds(timing.domain_lookup_start));
  value.Set("domainLookupEnd", NetworkMilliseconds(timing.domain_lookup_end));
  value.Set("connectStart", NetworkMilliseconds(timing.connect_start));
  value.Set("connectEnd", NetworkMilliseconds(timing.connect_end));
  value.Set("sslStart", NetworkMilliseconds(timing.ssl_start));
  value.Set("sslEnd", NetworkMilliseconds(timing.ssl_end));
  value.Set("workerStart", NetworkMilliseconds(timing.worker_start));
  value.Set("workerReady", NetworkMilliseconds(timing.worker_ready));
  value.Set("workerFetchStart",
            NetworkMilliseconds(timing.worker_fetch_start));
  value.Set("workerRespondWithSettled",
            NetworkMilliseconds(timing.worker_respond_with_settled));
  value.Set("workerRouterEvaluationStart",
            NetworkMilliseconds(timing.worker_router_evaluation_start));
  value.Set("workerCacheLookupStart",
            NetworkMilliseconds(timing.worker_cache_lookup_start));
  value.Set("sendStart", NetworkMilliseconds(timing.send_start));
  value.Set("sendEnd", NetworkMilliseconds(timing.send_end));
  value.Set("receiveHeadersStart",
            NetworkMilliseconds(timing.receive_headers_start));
  value.Set("receiveHeadersEnd",
            NetworkMilliseconds(timing.receive_headers_end));
  value.Set("receiveNonInformationalHeadersStart",
            NetworkMilliseconds(timing.receive_non_informational_headers_start));
  value.Set("receiveEarlyHintsStart",
            NetworkMilliseconds(timing.receive_early_hints_start));
  value.Set("pushStart", NetworkMilliseconds(timing.push_start));
  value.Set("pushEnd", NetworkMilliseconds(timing.push_end));
  value.Set("responseEnd", NetworkMilliseconds(timing.response_end));
  return base::Value(std::move(value));
}

base::Value CreateRemoteAddress(std::string ip, int port) {
  if (ip.empty()) {
    return base::Value();
  }
  base::DictValue address;
  address.Set("ip", std::move(ip));
  address.Set("port", port);
  return base::Value(std::move(address));
}

base::DictValue CreateNetworkRequest(NetworkRequestFacts request) {
  base::DictValue value;
  value.Set("inspectorId", InspectorId(request.inspector_id));
  value.Set("requestId", NonEmptyString(std::move(request.request_id)));
  value.Set("url", std::move(request.url));
  value.Set("method", std::move(request.method));
  value.Set("resourceType", std::move(request.resource_type));
  base::DictValue initiator;
  initiator.Set("type", NonEmptyString(std::move(request.initiator_type)));
  initiator.Set("url", NonEmptyString(std::move(request.initiator_url)));
  initiator.Set("line", request.initiator_line > 0
                            ? base::Value(request.initiator_line)
                            : base::Value());
  initiator.Set("column", request.initiator_column > 0
                              ? base::Value(request.initiator_column)
                              : base::Value());
  initiator.Set("linkPreload", request.link_preload);
  value.Set("initiator", std::move(initiator));
  value.Set("internal", request.internal);
  value.Set("destination", std::move(request.destination));
  value.Set("mode", std::move(request.mode));
  value.Set("credentialsMode", std::move(request.credentials_mode));
  value.Set("redirectMode", std::move(request.redirect_mode));
  value.Set("cacheMode", std::move(request.cache_mode));
  value.Set("priority", std::move(request.priority));
  value.Set("initialPriority", std::move(request.initial_priority));
  value.Set("fetchPriorityHint", std::move(request.fetch_priority_hint));
  value.Set("renderBlocking", std::move(request.render_blocking));
  value.Set("referrer", NonEmptyString(std::move(request.referrer)));
  value.Set("referrerPolicy", std::move(request.referrer_policy));
  value.Set("keepalive", request.keepalive);
  value.Set("userGesture", request.user_gesture);
  value.Set("adResource", request.ad_resource);
  value.Set("formSubmission", request.form_submission);
  SetNetworkHeaders(value, "headers", "headerCount", "headersTruncated",
                    std::move(request.headers));
  return value;
}

base::DictValue CreateNetworkResponse(NetworkResponseFacts response) {
  base::DictValue value;
  value.Set("url", std::move(response.url));
  value.Set("responseUrl", NonEmptyString(std::move(response.response_url)));
  value.Set("status", response.status_code);
  value.Set("statusText", std::move(response.status_text));
  value.Set("mimeType", std::move(response.mime_type));
  value.Set("charset", NonEmptyString(std::move(response.charset)));
  value.Set("alpnProtocol", NonEmptyString(std::move(response.alpn_protocol)));
  value.Set("connectionInfo",
            NonEmptyString(std::move(response.connection_info)));
  value.Set("remoteAddress", CreateRemoteAddress(std::move(response.remote_ip),
                                                 response.remote_port));
  value.Set("connectionId", NetworkQuantity(response.connection_id));
  value.Set("connectionReused", response.connection_reused);
  value.Set("wasCached", response.was_cached);
  value.Set("fetchedViaServiceWorker", response.fetched_via_service_worker);
  value.Set("serviceWorkerResponseSource",
            std::move(response.service_worker_response_source));
  value.Set("inPrefetchCache", response.in_prefetch_cache);
  value.Set("networkAccessed", response.network_accessed);
  value.Set("fromArchive", response.from_archive);
  value.Set("cookieInRequest", response.cookie_in_request);
  value.Set("responseType", std::move(response.response_type));
  value.Set("encodedDataLength",
            NetworkTransferredLength(response.encoded_data_length));
  value.Set("expectedContentLength",
            NetworkQuantity(response.expected_content_length));
  SetNetworkHeaders(value, "headers", "headerCount", "headersTruncated",
                    std::move(response.headers));
  value.Set("timing", CreateNetworkLoadTiming(response.timing));
  return value;
}

base::Value CreateNavigationResponseTiming(
    const NavigationResponseTiming& timing) {
  if (!timing.present) {
    return base::Value();
  }
  base::DictValue value;
  value.Set("navigationStartBeforeRecordMilliseconds",
            NetworkMilliseconds(timing.navigation_start_before_record));
  value.Set("loaderStart", NetworkMilliseconds(timing.loader_start));
  value.Set("firstRequestStart",
            NetworkMilliseconds(timing.first_request_start));
  value.Set("firstResponseStart",
            NetworkMilliseconds(timing.first_response_start));
  value.Set("firstLoaderCallback",
            NetworkMilliseconds(timing.first_loader_callback));
  value.Set("finalRequestStart",
            NetworkMilliseconds(timing.final_request_start));
  value.Set("finalResponseStart",
            NetworkMilliseconds(timing.final_response_start));
  value.Set("finalNonInformationalResponseStart",
            NetworkMilliseconds(timing.final_non_informational_response_start));
  value.Set("finalLoaderCallback",
            NetworkMilliseconds(timing.final_loader_callback));
  value.Set("requestFailed", NetworkMilliseconds(timing.request_failed));
  value.Set("commitSent", NetworkMilliseconds(timing.commit_sent));
  value.Set("commitReceived", NetworkMilliseconds(timing.commit_received));
  value.Set("commitReplySent", NetworkMilliseconds(timing.commit_reply_sent));
  value.Set("didCommit", NetworkMilliseconds(timing.did_commit));
  value.Set("finalRequestDomainLookupStart",
            NetworkMilliseconds(timing.final_request_domain_lookup_start));
  value.Set("finalRequestDomainLookupEnd",
            NetworkMilliseconds(timing.final_request_domain_lookup_end));
  value.Set("finalRequestConnectStart",
            NetworkMilliseconds(timing.final_request_connect_start));
  value.Set("finalRequestConnectEnd",
            NetworkMilliseconds(timing.final_request_connect_end));
  value.Set("finalRequestSslStart",
            NetworkMilliseconds(timing.final_request_ssl_start));
  return base::Value(std::move(value));
}

// Browser network records carry the frame the network service observer was
// made for. A worker's observer has no frame and carries its DevTools agent
// id instead.
base::DictValue CreateBrowserNetworkContext(const RecorderPipeClient& client,
                                            int page_frame_tree_node_id,
                                            int frame_tree_node_id) {
  if (page_frame_tree_node_id < 0 || frame_tree_node_id < 0) {
    return CreateContext(client, 0);
  }
  return CreateNavigationContext(client, page_frame_tree_node_id,
                                 frame_tree_node_id, 0, std::string());
}

}  // namespace

bool IsRecorderActive() {
  return GetProcessRecorderClient() != nullptr;
}

void RecordBlinkNetworkRequest(NetworkScope scope,
                               NetworkRequestFacts request,
                               bool redirect,
                               NetworkResponseFacts redirect_response,
                               CookieCallOrigin origin) {
  A11Y_RECORDER_COST("RecordBlinkNetworkRequest");
  RecorderPipeClient* client = GetProcessRecorderClient();
  if (!client) {
    return;
  }
  base::DictValue payload;
  payload.Set("context", CreateNetworkRendererContext(*client, scope, &origin));
  payload.Set("scope", CreateNetworkScope(std::move(scope)));
  payload.Set("request", CreateNetworkRequest(std::move(request)));
  payload.Set("redirect", redirect);
  payload.Set("redirectResponse",
              redirect ? base::Value(CreateNetworkResponse(
                             std::move(redirect_response)))
                       : base::Value());
  SetCookieCallOrigin(payload, std::move(origin));
  SendBlinkEvidence("browser.network", "request-will-be-sent",
                    std::move(payload));
}

void RecordBlinkNetworkResponse(NetworkScope scope,
                                uint64_t inspector_id,
                                std::string request_id,
                                bool from_memory_cache,
                                NetworkResponseFacts response) {
  A11Y_RECORDER_COST("RecordBlinkNetworkResponse");
  RecorderPipeClient* client = GetProcessRecorderClient();
  if (!client) {
    return;
  }
  base::DictValue payload;
  payload.Set("context", CreateNetworkRendererContext(*client, scope, nullptr));
  payload.Set("scope", CreateNetworkScope(std::move(scope)));
  payload.Set("inspectorId", InspectorId(inspector_id));
  payload.Set("requestId", NonEmptyString(std::move(request_id)));
  payload.Set("responseSource",
              from_memory_cache ? "memory-cache" : "loader");
  payload.Set("response", CreateNetworkResponse(std::move(response)));
  SendBlinkEvidence("browser.network", "response-received",
                    std::move(payload));
}

void RecordBlinkNetworkFinished(NetworkScope scope,
                                uint64_t inspector_id,
                                int64_t encoded_data_length,
                                int64_t decoded_body_length,
                                int64_t finish_before_record) {
  A11Y_RECORDER_COST("RecordBlinkNetworkFinished");
  RecorderPipeClient* client = GetProcessRecorderClient();
  if (!client) {
    return;
  }
  base::DictValue payload;
  payload.Set("context", CreateNetworkRendererContext(*client, scope, nullptr));
  payload.Set("scope", CreateNetworkScope(std::move(scope)));
  payload.Set("inspectorId", InspectorId(inspector_id));
  payload.Set("encodedDataLength",
              NetworkTransferredLength(encoded_data_length));
  payload.Set("decodedBodyLength", NetworkQuantity(decoded_body_length));
  payload.Set("finishBeforeRecordMilliseconds",
              NetworkMilliseconds(finish_before_record));
  SendBlinkEvidence("browser.network", "request-finished", std::move(payload));
}

void RecordBlinkNetworkFailed(NetworkScope scope,
                              uint64_t inspector_id,
                              NetworkFailureFacts failure) {
  A11Y_RECORDER_COST("RecordBlinkNetworkFailed");
  RecorderPipeClient* client = GetProcessRecorderClient();
  if (!client) {
    return;
  }
  base::DictValue payload;
  payload.Set("context", CreateNetworkRendererContext(*client, scope, nullptr));
  payload.Set("scope", CreateNetworkScope(std::move(scope)));
  payload.Set("inspectorId", InspectorId(inspector_id));
  payload.Set("url", std::move(failure.url));
  payload.Set("netError", failure.net_error);
  payload.Set("netErrorName", NonEmptyString(std::move(failure.net_error_name)));
  payload.Set("cancellation", failure.cancellation);
  payload.Set("timeout", failure.timeout);
  payload.Set("accessCheck", failure.access_check);
  payload.Set("blockedByResponse", failure.blocked_by_response);
  payload.Set("blockedByOrb", failure.blocked_by_orb);
  payload.Set("hasCopyInCache", failure.has_copy_in_cache);
  payload.Set("cancelledFromHttpError", failure.cancelled_from_http_error);
  payload.Set("internal", failure.internal);
  payload.Set("blockedReason", NonEmptyString(std::move(failure.blocked_reason)));
  if (failure.cors_error.empty()) {
    payload.Set("corsError", base::Value());
  } else {
    base::DictValue cors;
    cors.Set("error", std::move(failure.cors_error));
    cors.Set("failedParameter",
             NonEmptyString(std::move(failure.cors_failed_parameter)));
    payload.Set("corsError", std::move(cors));
  }
  SendBlinkEvidence("browser.network", "request-failed", std::move(payload));
}

void RecordBlinkMemoryCacheUse(NetworkScope scope,
                               bool static_data,
                               NetworkRequestFacts request,
                               NetworkResponseFacts response) {
  A11Y_RECORDER_COST("RecordBlinkMemoryCacheUse");
  RecorderPipeClient* client = GetProcessRecorderClient();
  if (!client) {
    return;
  }
  base::DictValue payload;
  payload.Set("context", CreateNetworkRendererContext(*client, scope, nullptr));
  payload.Set("scope", CreateNetworkScope(std::move(scope)));
  payload.Set("staticData", static_data);
  payload.Set("request", CreateNetworkRequest(std::move(request)));
  payload.Set("response", CreateNetworkResponse(std::move(response)));
  SendBlinkEvidence("browser.network", "memory-cache-hit", std::move(payload));
}

void RecordBrowserNetworkRequestHeaders(int page_frame_tree_node_id,
                                        int frame_tree_node_id,
                                        std::string devtools_agent_id,
                                        std::string request_id,
                                        int64_t sent_before_record,
                                        std::vector<NetworkHeader> headers,
                                        std::vector<CookieAccessEntry> cookies) {
  A11Y_RECORDER_COST("RecordBrowserNetworkRequestHeaders");
  RecorderPipeClient* client = GetProcessRecorderClient();
  if (!client || request_id.empty()) {
    return;
  }
  base::DictValue payload;
  payload.Set("context",
              CreateBrowserNetworkContext(*client, page_frame_tree_node_id,
                                          frame_tree_node_id));
  payload.Set("devtoolsAgentId",
              NonEmptyString(std::move(devtools_agent_id)));
  payload.Set("requestId", std::move(request_id));
  payload.Set("sentBeforeRecordMilliseconds",
              NetworkMilliseconds(sent_before_record));
  SetNetworkHeaders(payload, "headers", "headerCount", "headersTruncated",
                    std::move(headers));
  SetNetworkCookies(payload, std::move(cookies));
  SendBlinkEvidence("browser.network", "request-headers-sent",
                    std::move(payload));
}

void RecordBrowserNetworkResponseHeaders(
    int page_frame_tree_node_id,
    int frame_tree_node_id,
    std::string devtools_agent_id,
    std::string request_id,
    int status_code,
    std::vector<NetworkHeader> headers,
    std::vector<CookieAccessEntry> cookies) {
  A11Y_RECORDER_COST("RecordBrowserNetworkResponseHeaders");
  RecorderPipeClient* client = GetProcessRecorderClient();
  if (!client || request_id.empty()) {
    return;
  }
  base::DictValue payload;
  payload.Set("context",
              CreateBrowserNetworkContext(*client, page_frame_tree_node_id,
                                          frame_tree_node_id));
  payload.Set("devtoolsAgentId",
              NonEmptyString(std::move(devtools_agent_id)));
  payload.Set("requestId", std::move(request_id));
  payload.Set("status", status_code);
  SetNetworkHeaders(payload, "headers", "headerCount", "headersTruncated",
                    std::move(headers));
  SetNetworkCookies(payload, std::move(cookies));
  SendBlinkEvidence("browser.network", "response-headers-received",
                    std::move(payload));
}

void RecordBrowserNavigationResponse(int64_t navigation_id,
                                     int page_frame_tree_node_id,
                                     int frame_tree_node_id,
                                     int64_t document_navigation_id,
                                     std::string document_token,
                                     NavigationResponseFacts facts) {
  A11Y_RECORDER_COST("RecordBrowserNavigationResponse");
  RecorderPipeClient* client = GetProcessRecorderClient();
  if (!client || navigation_id <= 0 || page_frame_tree_node_id < 0 ||
      frame_tree_node_id < 0) {
    return;
  }
  base::DictValue payload;
  payload.Set("context",
              CreateNavigationContext(*client, page_frame_tree_node_id,
                                      frame_tree_node_id,
                                      document_navigation_id,
                                      std::move(document_token)));
  payload.Set("navigationId",
              "navigation-" + base::NumberToString(navigation_id));
  payload.Set("requestId", NonEmptyString(std::move(facts.request_id)));
  payload.Set("url", std::move(facts.url));
  payload.Set("method", std::move(facts.method));
  payload.Set("committed", facts.committed);
  payload.Set("errorPage", facts.error_page);
  payload.Set("sameDocument", facts.same_document);
  payload.Set("download", facts.download);
  payload.Set("backForwardCache", facts.back_forward_cache);
  payload.Set("netError", facts.net_error);
  payload.Set("netErrorName", NonEmptyString(std::move(facts.net_error_name)));
  payload.Set("redirectChain", StringList(std::move(facts.redirect_chain)));
  SetNetworkHeaders(payload, "requestHeaders", "requestHeaderCount",
                    "requestHeadersTruncated",
                    std::move(facts.request_headers));
  if (facts.response_present) {
    base::DictValue response;
    response.Set("status", facts.status_code);
    response.Set("statusText", std::move(facts.status_text));
    response.Set("mimeType", NonEmptyString(std::move(facts.mime_type)));
    response.Set("wasCached", facts.was_cached);
    response.Set("remoteAddress",
                 CreateRemoteAddress(std::move(facts.remote_ip),
                                     facts.remote_port));
    response.Set("connectionInfo",
                 NonEmptyString(std::move(facts.connection_info)));
    SetNetworkHeaders(response, "headers", "headerCount", "headersTruncated",
                      std::move(facts.response_headers));
    payload.Set("response", std::move(response));
  } else {
    payload.Set("response", base::Value());
  }
  payload.Set("timing", CreateNavigationResponseTiming(facts.timing));
  SendBlinkEvidence("browser.network", "navigation-response",
                    std::move(payload));
}


namespace {

bool EqualsIgnoringAsciiCase(std::string_view a, std::string_view b) {
  if (a.size() != b.size()) {
    return false;
  }
  for (size_t index = 0; index < a.size(); ++index) {
    const char left = a[index] >= 'A' && a[index] <= 'Z'
                          ? static_cast<char>(a[index] - 'A' + 'a')
                          : a[index];
    const char right = b[index] >= 'A' && b[index] <= 'Z'
                           ? static_cast<char>(b[index] - 'A' + 'a')
                           : b[index];
    if (left != right) {
      return false;
    }
  }
  return true;
}

// The names of the cookies a handshake's Cookie headers sent, or its
// Set-Cookie headers set. Only the names are read; the values are skipped.
base::ListValue HandshakeCookieNames(const std::vector<NetworkHeader>& headers,
                                     bool request) {
  base::ListValue names;
  for (const NetworkHeader& header : headers) {
    if (request && EqualsIgnoringAsciiCase(header.name, "cookie")) {
      for (std::string& name :
           cookie_text::NamesFromCookieString(header.value)) {
        names.Append(std::move(name));
      }
    } else if (!request && EqualsIgnoringAsciiCase(header.name, "set-cookie")) {
      names.Append(cookie_text::ParseCookieWrite(header.value).name);
    }
  }
  return names;
}

// The recordable part of a message, event, or close reason.
base::DictValue CreateMessageText(std::string_view text) {
  network_text::MessageText read = network_text::ReadMessageText(text);
  base::DictValue value;
  value.Set("text", std::move(read.text));
  value.Set("truncated", read.truncated);
  base::ListValue withheld;
  for (const network_text::WithheldText& part : read.withheld) {
    base::DictValue entry;
    entry.Set("offset", base::checked_cast<int>(part.offset));
    entry.Set("reason",
              std::string(network_text::HeaderRedactionName(part.reason)));
    withheld.Append(std::move(entry));
  }
  value.Set("withheld", std::move(withheld));
  return value;
}

base::DictValue CreateRealtimePayload(const RecorderPipeClient& client,
                                      NetworkScope scope,
                                      const CookieCallOrigin* origin) {
  base::DictValue payload;
  payload.Set("context", CreateNetworkRendererContext(client, scope, origin));
  payload.Set("scope", CreateNetworkScope(std::move(scope)));
  return payload;
}

void SetHandshakeResponse(base::DictValue& payload,
                          RealtimeHandshakeResponseFacts response) {
  payload.Set("url", NonEmptyString(std::move(response.url)));
  payload.Set("httpVersion", NonEmptyString(std::move(response.http_version)));
  payload.Set("status", response.status_code);
  payload.Set("statusText", NonEmptyString(std::move(response.status_text)));
  payload.Set("remoteAddress", CreateRemoteAddress(std::move(response.remote_ip),
                                                   response.remote_port));
  payload.Set("selectedProtocol",
              NonEmptyString(std::move(response.selected_protocol)));
  payload.Set("setCookieNames",
              HandshakeCookieNames(response.headers, /*request=*/false));
  SetNetworkHeaders(payload, "headers", "headerCount", "headersTruncated",
                    std::move(response.headers));
}

}  // namespace

void RecordBlinkWebSocketCreated(NetworkScope scope,
                                 uint64_t inspector_id,
                                 std::string url,
                                 std::string requested_protocols,
                                 CookieCallOrigin origin) {
  A11Y_RECORDER_COST("RecordBlinkWebSocketCreated");
  RecorderPipeClient* client = GetProcessRecorderClient();
  if (!client) {
    return;
  }
  base::DictValue payload =
      CreateRealtimePayload(*client, std::move(scope), &origin);
  payload.Set("inspectorId", InspectorId(inspector_id));
  payload.Set("url", std::move(url));
  payload.Set("requestedProtocols",
              NonEmptyString(std::move(requested_protocols)));
  SetCookieCallOrigin(payload, std::move(origin));
  SendBlinkEvidence("browser.network", "websocket-created", std::move(payload));
}

void RecordBlinkWebSocketHandshakeRequest(NetworkScope scope,
                                          uint64_t inspector_id,
                                          std::string url,
                                          std::vector<NetworkHeader> headers) {
  A11Y_RECORDER_COST("RecordBlinkWebSocketHandshakeRequest");
  RecorderPipeClient* client = GetProcessRecorderClient();
  if (!client) {
    return;
  }
  base::DictValue payload =
      CreateRealtimePayload(*client, std::move(scope), nullptr);
  payload.Set("inspectorId", InspectorId(inspector_id));
  payload.Set("url", std::move(url));
  payload.Set("cookieNames", HandshakeCookieNames(headers, /*request=*/true));
  SetNetworkHeaders(payload, "headers", "headerCount", "headersTruncated",
                    std::move(headers));
  SendBlinkEvidence("browser.network", "websocket-handshake-request",
                    std::move(payload));
}

void RecordBlinkWebSocketHandshakeResponse(
    NetworkScope scope,
    uint64_t inspector_id,
    RealtimeHandshakeResponseFacts response) {
  A11Y_RECORDER_COST("RecordBlinkWebSocketHandshakeResponse");
  RecorderPipeClient* client = GetProcessRecorderClient();
  if (!client) {
    return;
  }
  base::DictValue payload =
      CreateRealtimePayload(*client, std::move(scope), nullptr);
  payload.Set("inspectorId", InspectorId(inspector_id));
  payload.Set("extensions", NonEmptyString(std::move(response.extensions)));
  SetHandshakeResponse(payload, std::move(response));
  SendBlinkEvidence("browser.network", "websocket-handshake-response",
                    std::move(payload));
}

void RecordBlinkWebSocketMessage(NetworkScope scope,
                                 uint64_t inspector_id,
                                 bool sent,
                                 std::string opcode,
                                 int64_t payload_length,
                                 std::string text,
                                 CookieCallOrigin origin) {
  A11Y_RECORDER_COST("RecordBlinkWebSocketMessage");
  RecorderPipeClient* client = GetProcessRecorderClient();
  if (!client) {
    return;
  }
  const bool is_text = opcode == "text";
  base::DictValue payload =
      CreateRealtimePayload(*client, std::move(scope), sent ? &origin : nullptr);
  payload.Set("inspectorId", InspectorId(inspector_id));
  payload.Set("opcode", is_text ? "text" : "binary");
  payload.Set("payloadLength", NetworkQuantity(payload_length));
  payload.Set("payload", is_text ? base::Value(CreateMessageText(text))
                                 : base::Value());
  if (sent) {
    SetCookieCallOrigin(payload, std::move(origin));
  }
  SendBlinkEvidence("browser.network",
                    sent ? "websocket-message-sent"
                         : "websocket-message-received",
                    std::move(payload));
}

void RecordBlinkWebSocketCloseRequested(NetworkScope scope,
                                        uint64_t inspector_id,
                                        int code,
                                        std::string reason,
                                        CookieCallOrigin origin) {
  A11Y_RECORDER_COST("RecordBlinkWebSocketCloseRequested");
  RecorderPipeClient* client = GetProcessRecorderClient();
  if (!client) {
    return;
  }
  base::DictValue payload =
      CreateRealtimePayload(*client, std::move(scope), &origin);
  payload.Set("inspectorId", InspectorId(inspector_id));
  payload.Set("code", code < 0 ? base::Value() : base::Value(code));
  payload.Set("reason", CreateMessageText(reason));
  SetCookieCallOrigin(payload, std::move(origin));
  SendBlinkEvidence("browser.network", "websocket-close-requested",
                    std::move(payload));
}

void RecordBlinkWebSocketError(NetworkScope scope,
                               uint64_t inspector_id,
                               std::string message) {
  A11Y_RECORDER_COST("RecordBlinkWebSocketError");
  RecorderPipeClient* client = GetProcessRecorderClient();
  if (!client) {
    return;
  }
  base::DictValue payload =
      CreateRealtimePayload(*client, std::move(scope), nullptr);
  payload.Set("inspectorId", InspectorId(inspector_id));
  payload.Set("message", std::move(message));
  SendBlinkEvidence("browser.network", "websocket-error", std::move(payload));
}

void RecordBlinkWebSocketClosed(NetworkScope scope,
                                uint64_t inspector_id,
                                std::string cause,
                                bool was_clean,
                                int code,
                                std::string reason) {
  A11Y_RECORDER_COST("RecordBlinkWebSocketClosed");
  RecorderPipeClient* client = GetProcessRecorderClient();
  if (!client) {
    return;
  }
  const bool dropped = cause == "dropped";
  base::DictValue payload =
      CreateRealtimePayload(*client, std::move(scope), nullptr);
  payload.Set("inspectorId", InspectorId(inspector_id));
  payload.Set("cause", dropped ? "dropped" : "disconnected");
  payload.Set("wasClean", dropped ? base::Value(was_clean) : base::Value());
  payload.Set("code", dropped ? base::Value(code) : base::Value());
  payload.Set("reason", dropped ? base::Value(CreateMessageText(reason))
                                : base::Value());
  SendBlinkEvidence("browser.network", "websocket-closed", std::move(payload));
}

void RecordBlinkEventSourceMessage(NetworkScope scope,
                                   uint64_t inspector_id,
                                   std::string url,
                                   std::string event_type,
                                   std::string last_event_id,
                                   std::string data) {
  A11Y_RECORDER_COST("RecordBlinkEventSourceMessage");
  RecorderPipeClient* client = GetProcessRecorderClient();
  if (!client) {
    return;
  }
  base::DictValue payload =
      CreateRealtimePayload(*client, std::move(scope), nullptr);
  payload.Set("inspectorId", InspectorId(inspector_id));
  payload.Set("url", std::move(url));
  payload.Set("eventType", std::move(event_type));
  payload.Set("lastEventId", CreateMessageText(last_event_id));
  payload.Set("dataLength", NetworkQuantity(static_cast<int64_t>(data.size())));
  payload.Set("data", CreateMessageText(data));
  SendBlinkEvidence("browser.network", "event-source-message",
                    std::move(payload));
}

void RecordBlinkWebTransportCreated(NetworkScope scope,
                                    uint64_t transport_id,
                                    std::string url,
                                    CookieCallOrigin origin) {
  A11Y_RECORDER_COST("RecordBlinkWebTransportCreated");
  RecorderPipeClient* client = GetProcessRecorderClient();
  if (!client) {
    return;
  }
  base::DictValue payload =
      CreateRealtimePayload(*client, std::move(scope), &origin);
  payload.Set("transportId", InspectorId(transport_id));
  payload.Set("url", std::move(url));
  SetCookieCallOrigin(payload, std::move(origin));
  SendBlinkEvidence("browser.network", "web-transport-created",
                    std::move(payload));
}

void RecordBlinkWebTransportEstablished(
    NetworkScope scope,
    uint64_t transport_id,
    RealtimeHandshakeResponseFacts response) {
  A11Y_RECORDER_COST("RecordBlinkWebTransportEstablished");
  RecorderPipeClient* client = GetProcessRecorderClient();
  if (!client) {
    return;
  }
  base::DictValue payload =
      CreateRealtimePayload(*client, std::move(scope), nullptr);
  payload.Set("transportId", InspectorId(transport_id));
  payload.Set("maxDatagramSize", response.max_datagram_size < 0
                                     ? base::Value()
                                     : NetworkQuantity(
                                           response.max_datagram_size));
  SetHandshakeResponse(payload, std::move(response));
  SendBlinkEvidence("browser.network", "web-transport-established",
                    std::move(payload));
}

void RecordBlinkWebTransportCloseRequested(NetworkScope scope,
                                           uint64_t transport_id,
                                           bool close_info_present,
                                           int64_t code,
                                           std::string reason,
                                           CookieCallOrigin origin) {
  A11Y_RECORDER_COST("RecordBlinkWebTransportCloseRequested");
  RecorderPipeClient* client = GetProcessRecorderClient();
  if (!client) {
    return;
  }
  base::DictValue payload =
      CreateRealtimePayload(*client, std::move(scope), &origin);
  payload.Set("transportId", InspectorId(transport_id));
  payload.Set("code", close_info_present ? NetworkQuantity(code)
                                         : base::Value());
  payload.Set("reason", close_info_present
                            ? base::Value(CreateMessageText(reason))
                            : base::Value());
  SetCookieCallOrigin(payload, std::move(origin));
  SendBlinkEvidence("browser.network", "web-transport-close-requested",
                    std::move(payload));
}

void RecordBlinkWebTransportClosed(NetworkScope scope,
                                   uint64_t transport_id,
                                   bool abrupt,
                                   int64_t code,
                                   std::string reason) {
  A11Y_RECORDER_COST("RecordBlinkWebTransportClosed");
  RecorderPipeClient* client = GetProcessRecorderClient();
  if (!client) {
    return;
  }
  base::DictValue payload =
      CreateRealtimePayload(*client, std::move(scope), nullptr);
  payload.Set("transportId", InspectorId(transport_id));
  payload.Set("abrupt", abrupt);
  payload.Set("code", abrupt ? base::Value() : NetworkQuantity(code));
  payload.Set("reason", abrupt ? base::Value()
                               : base::Value(CreateMessageText(reason)));
  SendBlinkEvidence("browser.network", "web-transport-closed",
                    std::move(payload));
}

// Page resources (protocol 0.40).
FontFaceFacts::FontFaceFacts() = default;
FontFaceFacts::FontFaceFacts(FontFaceFacts&&) = default;
FontFaceFacts& FontFaceFacts::operator=(FontFaceFacts&&) = default;
FontFaceFacts::~FontFaceFacts() = default;

ImageResourceFacts::ImageResourceFacts() = default;
ImageResourceFacts::ImageResourceFacts(ImageResourceFacts&&) = default;
ImageResourceFacts& ImageResourceFacts::operator=(ImageResourceFacts&&) =
    default;
ImageResourceFacts::~ImageResourceFacts() = default;
StyleSheetResourceFacts::StyleSheetResourceFacts() = default;
StyleSheetResourceFacts::StyleSheetResourceFacts(StyleSheetResourceFacts&&) =
    default;
StyleSheetResourceFacts& StyleSheetResourceFacts::operator=(
    StyleSheetResourceFacts&&) = default;
StyleSheetResourceFacts::~StyleSheetResourceFacts() = default;
StyleSheetFacts::StyleSheetFacts() = default;
StyleSheetFacts::StyleSheetFacts(const StyleSheetFacts&) = default;
StyleSheetFacts::StyleSheetFacts(StyleSheetFacts&&) = default;
StyleSheetFacts& StyleSheetFacts::operator=(const StyleSheetFacts&) = default;
StyleSheetFacts& StyleSheetFacts::operator=(StyleSheetFacts&&) = default;
StyleSheetFacts::~StyleSheetFacts() = default;
StyleSheetScopeFacts::StyleSheetScopeFacts() = default;
StyleSheetScopeFacts::StyleSheetScopeFacts(StyleSheetScopeFacts&&) = default;
StyleSheetScopeFacts& StyleSheetScopeFacts::operator=(StyleSheetScopeFacts&&) =
    default;
StyleSheetScopeFacts::~StyleSheetScopeFacts() = default;

namespace {

constexpr char kResourcesChannel[] = "browser.resources";

// What the renderer has recorded of its resources: the digest of each font
// file and image whose bytes were queued, and the font file of each typeface.
struct RecordedFontFile {
  std::string digest;
  int collection_index = 0;
};

struct ResourceStorage {
  base::Lock lock;
  std::unordered_map<uint32_t, RecordedFontFile> typefaces;
  // Protocol 0.48: the paint image IDs already recorded in the renderer.
  std::unordered_set<int64_t> paint_image_ids;
  std::unordered_map<std::string, bool> font_file_digests;
  std::unordered_map<std::string, bool> image_digests;
  // Protocol 0.51: the style sheet text digests already recorded.
  std::unordered_map<std::string, bool> style_sheet_digests;
  // Protocol 0.54 (slice 4h): the digests of the script sources recorded,
  // and the script IDs of the main thread recorded.
  std::unordered_map<std::string, bool> script_digests;
  std::unordered_set<int> script_ids;
  uint64_t next_face_number = 0;
  uint64_t next_style_sheet_number = 0;
};

ResourceStorage& Resources() {
  static base::NoDestructor<ResourceStorage> storage;
  return *storage;
}

std::string Sha256Hex(const std::string& bytes) {
  return base::HexEncodeLower(crypto::hash::Sha256(std::string_view(bytes)));
}

// The bytes of a font file or an image, base64 encoded on the writer thread so
// the observing thread only copies them.
struct ResourceBytesEvidence : PendingEvidence {
  raw_ptr<const RecorderPipeClient> client = nullptr;
  std::string digest;
  std::string data;

  base::DictValue TakePayload() override {
    base::DictValue payload;
    payload.Set("context", CreateContext(*client, 0));
    payload.Set("digest", std::move(digest));
    payload.Set("size", base::NumberToString(data.size()));
    payload.Set("bytes", base::Base64Encode(data));
    return payload;
  }
};

// Queues the bytes of a digest the renderer has not met. Returns true when the
// record was queued or had been already, so the digest counts as recorded only
// when its bytes are on their way.
bool QueueResourceBytes(RecorderPipeClient* client,
                        std::unordered_map<std::string, bool>& recorded,
                        const char* event_type,
                        const std::string& digest,
                        std::string bytes,
                        const char* channel = kResourcesChannel) {
  if (recorded.contains(digest)) {
    return true;
  }
  auto evidence = std::make_unique<ResourceBytesEvidence>();
  evidence->channel = channel;
  evidence->event_type = event_type;
  // The context and members with their names, and the base64 of the bytes.
  evidence->bytes = 400 + digest.size() + (bytes.size() + 2) / 3 * 4;
  evidence->client = client;
  evidence->digest = digest;
  evidence->data = std::move(bytes);
  ReportOmittedEvidence(client, channel);
  std::string error;
  if (!client->QueueEvidence(std::move(evidence), &error)) {
    HoldOmittedEvidence(channel, 1);
    WriteDiagnosticLine("Blink evidence write failed: " + error);
    return false;
  }
  recorded.emplace(digest, true);
  return true;
}

base::DictValue CreateFontFacePayload(const RecorderPipeClient& client,
                                      int document_node_id,
                                      std::string document_token,
                                      uint64_t face_number) {
  base::DictValue payload;
  payload.Set("context",
              CreateContext(client, document_node_id, std::move(document_token)));
  payload.Set("faceNumber", base::NumberToString(face_number));
  return payload;
}

}  // namespace

bool LookUpFontFile(uint32_t typeface_id,
                    std::string* digest,
                    int* collection_index) {
  ResourceStorage& storage = Resources();
  base::AutoLock lock(storage.lock);
  auto found = storage.typefaces.find(typeface_id);
  if (found == storage.typefaces.end()) {
    return false;
  }
  *digest = found->second.digest;
  *collection_index = found->second.collection_index;
  return true;
}

std::string RecordFontFile(uint32_t typeface_id,
                           int collection_index,
                           bool readable,
                           std::string bytes) {
  A11Y_RECORDER_COST("RecordFontFile");
  RecorderPipeClient* client = GetProcessRecorderClient();
  if (!client && !IsRecreationMode()) {
    return std::string();
  }
  std::string digest = readable ? Sha256Hex(bytes) : std::string();
  ResourceStorage& storage = Resources();
  base::AutoLock lock(storage.lock);
  if (!client) {
    // The recreation mode (sub-step 3) records nothing: it keeps the digest
    // of each typeface, so that a glyph run's recorded font file can be
    // compared with the font Blink chose, and each file is read once.
    storage.typefaces[typeface_id] = {digest, collection_index};
    return digest;
  }
  if (readable &&
      !QueueResourceBytes(client, storage.font_file_digests, "font-file",
                          digest, std::move(bytes))) {
    // The typeface is left unmet, so its file is read again the next time a
    // glyph run uses it.
    return digest;
  }
  storage.typefaces[typeface_id] = {digest, collection_index};
  return digest;
}

uint64_t AssignFontFaceNumber() {
  ResourceStorage& storage = Resources();
  base::AutoLock lock(storage.lock);
  return ++storage.next_face_number;
}

void RecordBlinkFontFaceAdded(int document_node_id,
                              std::string document_token,
                              uint64_t face_number) {
  A11Y_RECORDER_COST("RecordBlinkFontFaceAdded");
  RecorderPipeClient* client = GetProcessRecorderClient();
  if (!client || document_node_id <= 0 || document_token.empty() ||
      face_number == 0) {
    return;
  }
  SendBlinkEvidence(kResourcesChannel, "font-face-added",
                    CreateFontFacePayload(*client, document_node_id,
                                          std::move(document_token),
                                          face_number));
}

void RecordBlinkFontFaceLoaded(int document_node_id,
                               std::string document_token,
                               uint64_t face_number,
                               FontFaceFacts face) {
  A11Y_RECORDER_COST("RecordBlinkFontFaceLoaded");
  RecorderPipeClient* client = GetProcessRecorderClient();
  if (!client || document_node_id <= 0 || document_token.empty() ||
      face_number == 0) {
    return;
  }
  base::DictValue payload = CreateFontFacePayload(
      *client, document_node_id, std::move(document_token), face_number);
  payload.Set("family", std::move(face.family));
  base::DictValue descriptors;
  descriptors.Set("style", std::move(face.style));
  descriptors.Set("weight", std::move(face.weight));
  descriptors.Set("stretch", std::move(face.stretch));
  descriptors.Set("unicodeRange", std::move(face.unicode_range));
  descriptors.Set("variant", std::move(face.variant));
  descriptors.Set("featureSettings", std::move(face.feature_settings));
  descriptors.Set("display", std::move(face.display));
  descriptors.Set("ascentOverride", std::move(face.ascent_override));
  descriptors.Set("descentOverride", std::move(face.descent_override));
  descriptors.Set("lineGapOverride", std::move(face.line_gap_override));
  descriptors.Set("sizeAdjust", std::move(face.size_adjust));
  payload.Set("descriptors", std::move(descriptors));
  if (face.source_kind.empty()) {
    payload.Set("source", base::Value());
  } else {
    base::DictValue source;
    source.Set("kind", std::move(face.source_kind));
    if (face.source_url.empty()) {
      source.Set("url", base::Value());
    } else {
      source.Set("url", std::move(face.source_url));
    }
    payload.Set("source", std::move(source));
  }
  if (face.font_file_digest.empty()) {
    payload.Set("fontFile", base::Value());
  } else {
    base::DictValue font_file;
    font_file.Set("digest", std::move(face.font_file_digest));
    font_file.Set("index", face.font_file_index);
    payload.Set("fontFile", std::move(font_file));
  }
  SendBlinkEvidence(kResourcesChannel, "font-face-loaded", std::move(payload));
}

void RecordBlinkFontFaceRemoved(int document_node_id,
                                std::string document_token,
                                uint64_t face_number) {
  A11Y_RECORDER_COST("RecordBlinkFontFaceRemoved");
  RecorderPipeClient* client = GetProcessRecorderClient();
  if (!client || document_node_id <= 0 || document_token.empty() ||
      face_number == 0) {
    return;
  }
  SendBlinkEvidence(kResourcesChannel, "font-face-removed",
                    CreateFontFacePayload(*client, document_node_id,
                                          std::move(document_token),
                                          face_number));
}

void RecordBlinkImageResource(ImageResourceFacts image) {
  A11Y_RECORDER_COST("RecordBlinkImageResource");
  RecorderPipeClient* client = GetProcessRecorderClient();
  if (!client || image.url.empty()) {
    return;
  }
  const size_t size = image.bytes.size();
  const std::string digest = Sha256Hex(image.bytes);
  bool recorded = false;
  {
    ResourceStorage& storage = Resources();
    base::AutoLock lock(storage.lock);
    recorded = QueueResourceBytes(client, storage.image_digests, "image-data",
                                  digest, std::move(image.bytes));
  }
  base::DictValue payload;
  payload.Set("context", CreateContext(*client, 0));
  payload.Set("url", std::move(image.url));
  if (image.response_url.empty()) {
    payload.Set("responseUrl", base::Value());
  } else {
    payload.Set("responseUrl", std::move(image.response_url));
  }
  payload.Set("status", image.status);
  payload.Set("mimeType", std::move(image.mime_type));
  payload.Set("size", base::NumberToString(size));
  payload.Set("digest", digest);
  payload.Set("dataRecorded", recorded);
  payload.Set("imageId", image.image_id && *image.image_id >= 0
                             ? base::Value(base::NumberToString(*image.image_id))
                             : base::Value());
  SendBlinkEvidence(kResourcesChannel, "image-resource", std::move(payload));
}

std::string RecordBlinkStyleSheetResource(StyleSheetResourceFacts sheet) {
  A11Y_RECORDER_COST("RecordBlinkStyleSheetResource");
  RecorderPipeClient* client = GetProcessRecorderClient();
  if (!client || sheet.url.empty()) {
    return std::string();
  }
  const size_t size = sheet.text.size();
  const std::string digest = Sha256Hex(sheet.text);
  bool recorded = false;
  {
    ResourceStorage& storage = Resources();
    base::AutoLock lock(storage.lock);
    recorded = QueueResourceBytes(client, storage.style_sheet_digests,
                                  "style-sheet-text", digest,
                                  std::move(sheet.text));
  }
  base::DictValue payload;
  payload.Set("context", CreateContext(*client, 0));
  payload.Set("url", std::move(sheet.url));
  if (sheet.response_url.empty()) {
    payload.Set("responseUrl", base::Value());
  } else {
    payload.Set("responseUrl", std::move(sheet.response_url));
  }
  payload.Set("status", sheet.status);
  payload.Set("mimeType", std::move(sheet.mime_type));
  payload.Set("size", base::NumberToString(size));
  payload.Set("digest", digest);
  payload.Set("textRecorded", recorded);
  SendBlinkEvidence(kResourcesChannel, "style-sheet-resource",
                    std::move(payload));
  return recorded ? digest : std::string();
}

std::string RecordBlinkStyleSheetText(std::string text) {
  A11Y_RECORDER_COST("RecordBlinkStyleSheetText");
  RecorderPipeClient* client = GetProcessRecorderClient();
  if (!client) {
    return std::string();
  }
  const std::string digest = Sha256Hex(text);
  ResourceStorage& storage = Resources();
  base::AutoLock lock(storage.lock);
  return QueueResourceBytes(client, storage.style_sheet_digests,
                            "style-sheet-text", digest, std::move(text))
             ? digest
             : std::string();
}

uint64_t AssignStyleSheetNumber() {
  ResourceStorage& storage = Resources();
  base::AutoLock lock(storage.lock);
  return ++storage.next_style_sheet_number;
}

namespace {

base::DictValue StyleSheetFactsValue(const StyleSheetFacts& sheet) {
  base::DictValue value;
  value.Set("sheet", base::NumberToString(sheet.sheet_number));
  if (!sheet.full) {
    return value;
  }
  value.Set("kind", sheet.kind);
  value.Set("ownerNodeId", sheet.owner_node_id > 0
                               ? base::Value(sheet.owner_node_id)
                               : base::Value());
  if (sheet.parent_sheet_number > 0) {
    value.Set("parentSheet", base::NumberToString(sheet.parent_sheet_number));
    value.Set("ruleIndex", sheet.rule_index);
  } else {
    value.Set("parentSheet", base::Value());
    value.Set("ruleIndex", base::Value());
  }
  value.Set("href", sheet.href.empty() ? base::Value() : base::Value(sheet.href));
  value.Set("media", sheet.media);
  value.Set("title", sheet.title);
  value.Set("disabled", sheet.disabled);
  value.Set("active", sheet.active);
  value.Set("textSource", sheet.text_source);
  value.Set("textDigest", sheet.text_digest.empty()
                              ? base::Value()
                              : base::Value(sheet.text_digest));
  return value;
}

}  // namespace

void RecordBlinkStyleSheetsUpdated(int document_node_id,
                                   std::string document_token,
                                   std::vector<StyleSheetScopeFacts> scopes) {
  A11Y_RECORDER_COST("RecordBlinkStyleSheetsUpdated");
  RecorderPipeClient* client = GetProcessRecorderClient();
  if (!client || document_node_id <= 0 || scopes.empty()) {
    return;
  }
  base::ListValue scope_values;
  for (const StyleSheetScopeFacts& scope : scopes) {
    base::DictValue scope_value;
    scope_value.Set("scopeNodeId", scope.scope_node_id);
    base::ListValue sheets;
    for (const StyleSheetFacts& sheet : scope.sheets) {
      sheets.Append(StyleSheetFactsValue(sheet));
    }
    base::ListValue adopted;
    for (const StyleSheetFacts& sheet : scope.adopted) {
      adopted.Append(StyleSheetFactsValue(sheet));
    }
    scope_value.Set("sheets", std::move(sheets));
    scope_value.Set("adopted", std::move(adopted));
    scope_values.Append(std::move(scope_value));
  }
  base::DictValue payload;
  payload.Set("context", CreateContext(*client, document_node_id,
                                       std::move(document_token)));
  payload.Set("scopes", std::move(scope_values));
  SendBlinkEvidence(kResourcesChannel, "style-sheets-updated",
                    std::move(payload));
}

void RecordBlinkImagePaintImage(ImagePaintImageFacts facts) {
  A11Y_RECORDER_COST("RecordBlinkImagePaintImage");
  RecorderPipeClient* client = GetProcessRecorderClient();
  if (!client || facts.image_id < 0 || facts.paint_image_id < 0) {
    return;
  }
  {
    ResourceStorage& storage = Resources();
    base::AutoLock lock(storage.lock);
    if (!storage.paint_image_ids.insert(facts.paint_image_id).second) {
      return;
    }
  }
  base::DictValue payload;
  payload.Set("context", CreateContext(*client, 0));
  payload.Set("imageId", base::NumberToString(facts.image_id));
  payload.Set("paintImageId", base::NumberToString(facts.paint_image_id));
  payload.Set("sequence", facts.own_sequence ? "own" : "shared");
  payload.Set("nodeId", facts.node_id && *facts.node_id > 0
                            ? base::Value(*facts.node_id)
                            : base::Value());
  payload.Set("syncTargetPaintImageId",
              facts.sync_target_paint_image_id &&
                      *facts.sync_target_paint_image_id >= 0
                  ? base::Value(base::NumberToString(
                        *facts.sync_target_paint_image_id))
                  : base::Value());
  SendBlinkEvidence(kResourcesChannel, "image-paint-image", std::move(payload));
}

int RegisterHookCostKind(const char* name) {
  return RegisterCostKind(name);
}

int64_t StartHookCost(int slot) {
  if (slot < 0 || !GetProcessRecorderClient()) {
    return -1;
  }
  return CostNowNanoseconds();
}

void StopHookCost(int slot, int64_t started) {
  if (slot < 0 || started < 0) {
    return;
  }
  RecordCost(slot, CostNowNanoseconds() - started);
}


ScriptFrameFacts::ScriptFrameFacts() = default;
ScriptFrameFacts::ScriptFrameFacts(const ScriptFrameFacts&) = default;
ScriptFrameFacts::ScriptFrameFacts(ScriptFrameFacts&&) = default;
ScriptFrameFacts& ScriptFrameFacts::operator=(const ScriptFrameFacts&) =
    default;
ScriptFrameFacts& ScriptFrameFacts::operator=(ScriptFrameFacts&&) = default;
ScriptFrameFacts::~ScriptFrameFacts() = default;
TimerOriginFacts::TimerOriginFacts() = default;
TimerOriginFacts::TimerOriginFacts(TimerOriginFacts&&) = default;
TimerOriginFacts& TimerOriginFacts::operator=(TimerOriginFacts&&) = default;
TimerOriginFacts::~TimerOriginFacts() = default;
ScriptSourceFacts::ScriptSourceFacts() = default;
ScriptSourceFacts::ScriptSourceFacts(const ScriptSourceFacts&) = default;
ScriptSourceFacts::ScriptSourceFacts(ScriptSourceFacts&&) = default;
ScriptSourceFacts& ScriptSourceFacts::operator=(const ScriptSourceFacts&) =
    default;
ScriptSourceFacts& ScriptSourceFacts::operator=(ScriptSourceFacts&&) = default;
ScriptSourceFacts::~ScriptSourceFacts() = default;

void NoteBlinkTimerOrigin(uintptr_t timer_identity, TimerOriginFacts facts) {
  A11Y_RECORDER_COST("NoteBlinkTimerOrigin");
  if (!GetProcessRecorderClient() || timer_identity == 0) {
    return;
  }
  EvidenceIdentityStorage& identities = EvidenceIdentities();
  base::AutoLock lock(identities.lock);
  identities.timer_origins.insert_or_assign(timer_identity, std::move(facts));
}

void RecordBlinkScriptSource(ScriptSourceFacts facts) {
  A11Y_RECORDER_COST("RecordBlinkScriptSource");
  RecorderPipeClient* client = GetProcessRecorderClient();
  if (!client || facts.document_node_id <= 0 || facts.script_id <= 0 ||
      (facts.kind != kScriptSourceKindClassic &&
       facts.kind != kScriptSourceKindModule &&
       facts.kind != kScriptSourceKindEventHandlerAttribute)) {
    return;
  }
  const bool attribute = facts.kind == kScriptSourceKindEventHandlerAttribute;
  base::DictValue payload;
  payload.Set("context", CreateContext(*client, facts.document_node_id,
                                       std::move(facts.document_token)));
  payload.Set("scriptId", base::NumberToString(facts.script_id));
  payload.Set("kind", facts.kind);
  payload.Set("elementNodeId", facts.element_node_id > 0
                                   ? base::Value(facts.element_node_id)
                                   : base::Value());
  payload.Set("attributeName",
              attribute && !facts.attribute_name.empty()
                  ? base::Value(std::move(facts.attribute_name))
                  : base::Value());
  payload.Set("url", facts.url.empty() ? base::Value()
                                       : base::Value(std::move(facts.url)));
  payload.Set("line", facts.line_number > 0 ? base::Value(facts.line_number)
                                            : base::Value());
  payload.Set("column", facts.column_number > 0
                            ? base::Value(facts.column_number)
                            : base::Value());
  SendBlinkEvidence("browser.timer", "script-compiled", std::move(payload));
}

void PushBlinkScriptElement(uintptr_t script_identity,
                            ScriptSourceFacts facts) {
  if (!GetProcessRecorderClient() || script_identity == 0) {
    return;
  }
  EvidenceIdentityStorage& identities = EvidenceIdentities();
  base::AutoLock lock(identities.lock);
  identities.running_script_elements.insert_or_assign(script_identity,
                                                      std::move(facts));
}

void PopBlinkScriptElement(uintptr_t script_identity) {
  EvidenceIdentityStorage& identities = EvidenceIdentities();
  base::AutoLock lock(identities.lock);
  identities.running_script_elements.erase(script_identity);
}

void RecordBlinkClassicScriptCompiled(uintptr_t script_identity,
                                      int script_id) {
  A11Y_RECORDER_COST("RecordBlinkClassicScriptCompiled");
  if (!GetProcessRecorderClient() || script_identity == 0 || script_id <= 0) {
    return;
  }
  ScriptSourceFacts facts;
  {
    EvidenceIdentityStorage& identities = EvidenceIdentities();
    base::AutoLock lock(identities.lock);
    auto found = identities.running_script_elements.find(script_identity);
    if (found == identities.running_script_elements.end()) {
      return;
    }
    facts = found->second;
  }
  facts.script_id = script_id;
  RecordBlinkScriptSource(std::move(facts));
}

ScriptParsedFacts::ScriptParsedFacts() = default;
ScriptParsedFacts::ScriptParsedFacts(ScriptParsedFacts&&) = default;
ScriptParsedFacts& ScriptParsedFacts::operator=(ScriptParsedFacts&&) = default;
ScriptParsedFacts::~ScriptParsedFacts() = default;

namespace {

constexpr char kScriptChannel[] = "browser.script";

std::atomic<int>& DevToolsCommandDepth() {
  static std::atomic<int> depth{0};
  return depth;
}

base::Value TextOrNullValue(std::string text) {
  return text.empty() ? base::Value() : base::Value(std::move(text));
}

}  // namespace

bool ClaimScriptParsed(int script_id) {
  if (script_id <= 0) {
    return false;
  }
  ResourceStorage& storage = Resources();
  base::AutoLock lock(storage.lock);
  return storage.script_ids.insert(script_id).second;
}

void RecordScriptParsed(ScriptParsedFacts facts) {
  A11Y_RECORDER_COST("RecordScriptParsed");
  RecorderPipeClient* client = GetProcessRecorderClient();
  if (!client || facts.document_node_id <= 0 || facts.script_id <= 0 ||
      (facts.kind != kScriptParsedKindClassic &&
       facts.kind != kScriptParsedKindModule &&
       facts.kind != kScriptParsedKindEval &&
       facts.kind != kScriptParsedKindFunction)) {
    return;
  }
  const size_t size = facts.source.size();
  const std::string digest = Sha256Hex(facts.source);
  bool recorded = false;
  {
    ResourceStorage& storage = Resources();
    base::AutoLock lock(storage.lock);
    recorded = QueueResourceBytes(client, storage.script_digests,
                                  "script-text", digest,
                                  std::move(facts.source), kScriptChannel);
  }
  const std::string world_kind =
      NormalizeExecutionWorldKind(std::move(facts.world_kind));
  base::DictValue context = CreateContext(*client, facts.document_node_id,
                                          std::move(facts.document_token));
  if (!world_kind.empty()) {
    context.Set("executionWorldId", ExecutionWorldId(facts.world_id));
  }
  base::DictValue payload;
  payload.Set("context", std::move(context));
  payload.Set("world", CreateExecutionWorld(world_kind, facts.world_id,
                                            std::move(facts.world_name),
                                            std::move(facts.world_stable_id)));
  payload.Set("scriptId", base::NumberToString(facts.script_id));
  payload.Set("kind", facts.kind);
  payload.Set("url", TextOrNullValue(std::move(facts.url)));
  payload.Set("sourceUrl", TextOrNullValue(std::move(facts.source_url)));
  payload.Set("sourceMapUrl", TextOrNullValue(std::move(facts.source_map_url)));
  payload.Set("line", facts.line_number > 0 ? base::Value(facts.line_number)
                                            : base::Value());
  payload.Set("column", facts.column_number > 0
                            ? base::Value(facts.column_number)
                            : base::Value());
  payload.Set("evalFromScriptId",
              facts.eval_from_script_id > 0
                  ? base::Value(base::NumberToString(facts.eval_from_script_id))
                  : base::Value());
  payload.Set("compileError", facts.compile_error);
  payload.Set("digest", digest);
  payload.Set("size", base::NumberToString(size));
  payload.Set("textRecorded", recorded);
  SendBlinkEvidence(kScriptChannel, "script-parsed", std::move(payload));
}

void EnterDevToolsCommand() {
  DevToolsCommandDepth().fetch_add(1, std::memory_order_relaxed);
}

void LeaveDevToolsCommand() {
  DevToolsCommandDepth().fetch_sub(1, std::memory_order_relaxed);
}

bool InDevToolsCommand() {
  return DevToolsCommandDepth().load(std::memory_order_relaxed) > 0;
}

}  // namespace a11y_recorder
