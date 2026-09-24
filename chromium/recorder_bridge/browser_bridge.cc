#include "chromium/recorder_bridge/browser_bridge.h"

#include <windows.h>

#include <array>
#include <cmath>
#include <initializer_list>
#include <map>
#include <memory>
#include <optional>
#include <string>
#include <string_view>
#include <unordered_map>
#include <utility>
#include <vector>

#include "base/check.h"
#include "base/command_line.h"
#include "base/containers/span.h"
#include "base/environment.h"
#include "base/memory/read_only_shared_memory_region.h"
#include "base/memory/shared_memory_switch.h"
#include "base/no_destructor.h"
#include "base/numerics/safe_conversions.h"
#include "base/process/launch.h"
#include "base/strings/string_number_conversions.h"
#include "base/synchronization/lock.h"
#include "base/win/windows_handle_util.h"
#include "chromium/recorder_bridge/cookie_text.h"
#include "chromium/recorder_bridge/network_text.h"
#include "chromium/recorder_bridge/recorder_protocol.h"
#include "chromium/recorder_bridge/recorder_switches.h"
#include "components/version_info/version_info.h"

namespace a11y_recorder {
namespace {

// Decides whether one reported EventTarget can be recorded without inventing
// identity. Every recordable target belongs to a document, because the record
// names that document. A Node must also report its own node identifier; a
// Window or other non-Node EventTarget has none and is identified by its kind
// and its process-local target identifier instead.
bool IsRecordableEventTarget(std::string_view kind,
                             int document_node_id,
                             int target_node_id) {
  if (document_node_id <= 0) {
    return false;
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

  struct DispatchState {
    std::string dispatch_id;
    int document_node_id;
    int target_node_id;
    std::string event_name;
    std::string target_tag_name;
    std::string target_element_id;
    bool trusted;
    std::vector<NodeState> composed_path;
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
  };
  std::unordered_map<uintptr_t, DispatchState> dispatches;
  std::unordered_map<uintptr_t, InvocationState> active_invocations;
  std::unordered_map<uintptr_t, TimerState> timers;
};

EvidenceIdentityStorage& EvidenceIdentities() {
  static base::NoDestructor<EvidenceIdentityStorage> identities;
  return *identities;
}

int64_t QueryEvidenceTicks() {
  LARGE_INTEGER value = {};
  CHECK(::QueryPerformanceCounter(&value));
  return value.QuadPart;
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
  target.Set("documentId", DocumentId(document_node_id));
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

void RegisterDispatchIdentity(
    uintptr_t event_identity,
    int document_node_id,
    int target_node_id,
    const std::string& event_name,
    const std::string& target_tag_name,
    const std::string& target_element_id,
    bool trusted) {
  EvidenceIdentityStorage& identities = EvidenceIdentities();
  base::AutoLock lock(identities.lock);
  EvidenceIdentityStorage::DispatchState state{
      .dispatch_id =
          "dispatch-" + base::NumberToString(identities.next_dispatch_id++),
      .document_node_id = document_node_id,
      .target_node_id = target_node_id,
      .event_name = event_name,
      .target_tag_name = target_tag_name,
      .target_element_id = target_element_id,
      .trusted = trusted,
  };
  identities.dispatches.insert_or_assign(event_identity, state);
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
    int nesting_level) {
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
    std::string world_stable_id) {
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
  payload.Set("originalTarget",
              CreateEventTarget(kEventTargetKindNode, std::string(),
                                std::string(), state.document_node_id,
                                state.target_node_id, state.target_tag_name,
                                state.target_element_id));
  base::ListValue composed_path;
  for (const auto& entry : state.composed_path) {
    composed_path.Append(CreateEventTarget(entry));
  }
  payload.Set("composedPath", std::move(composed_path));
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
  payload.Set("callbackLocation", base::Value());
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
base::Lock& OmittedEvidenceLock() {
  static base::NoDestructor<base::Lock> lock;
  return *lock;
}

std::map<std::string, int>& OmittedEvidenceCounts() {
  static base::NoDestructor<std::map<std::string, int>> counts;
  return *counts;
}

void HoldOmittedEvidence(const std::string& channel, int count) {
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
// write returns the original count unchanged instead of counting the omission
// as one more lost record.
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
  if (!client->SendEvidence(QueryEvidenceTicks(), channel,
                            std::string(kOmissionEventType),
                            std::move(payload), base::ListValue(), &error)) {
    HoldOmittedEvidence(channel, count);
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
  std::string error;
  if (!client->SendEvidence(QueryEvidenceTicks(), channel,
                            std::move(event_type), std::move(payload),
                            base::ListValue(), &error)) {
    HoldOmittedEvidence(channel, 1);
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


// Cookie records name at most this many cookies. Chromium allows far more
// cookies per profile than one record can hold within the message limit, so a
// record states its full count and whether its list was cut rather than
// dropping the record or the excess silently.
constexpr size_t kMaximumCookiesPerRecord = 256;

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

  auto client = std::make_unique<RecorderPipeClient>(std::move(configuration));
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

  const std::optional<std::string> metadata =
      base::Environment::Create()->GetVar(kChildBootstrapMetadataEnvironment);
  if (!metadata.has_value()) {
    WriteDiagnosticLine("Recorder child bootstrap metadata was unavailable.");
    return true;
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
                                   std::string world_stable_id) {
  RecorderPipeClient* client = GetProcessRecorderClient();
  if (!client || !IsRecordableEventTarget(target_kind, document_node_id,
                                          target_node_id)) {
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
      std::move(world_name), std::move(world_stable_id));
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
                                std::string world_stable_id) {
  RecorderPipeClient* client = GetProcessRecorderClient();
  std::optional<std::string> listener_id =
      TakeListenerIdentity(listener_identity);
  if (!client || !listener_id ||
      !IsRecordableEventTarget(target_kind, document_node_id,
                               target_node_id)) {
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
      std::move(world_name), std::move(world_stable_id));
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
                                        std::string world_stable_id) {
  RecorderPipeClient* client = GetProcessRecorderClient();
  if (!client || !IsRecordableEventTarget(target_kind, document_node_id,
                                          target_node_id)) {
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
      std::move(world_name), std::move(world_stable_id));
  SendBlinkEvidence("browser.listener", "listener-callback-replaced",
                    std::move(payload));
}

void RecordBlinkDispatchStarted(uintptr_t event_identity,
                                int document_node_id,
                                int target_node_id,
                                std::string event_name,
                                std::string target_tag_name,
                                std::string target_element_id,
                                bool trusted) {
  RecorderPipeClient* client = GetProcessRecorderClient();
  if (!client || document_node_id <= 0 || target_node_id <= 0) {
    return;
  }

  RegisterDispatchIdentity(event_identity, document_node_id, target_node_id,
                           event_name, target_tag_name, target_element_id,
                           trusted);
}

void RecordBlinkDispatchPathNode(uintptr_t event_identity,
                                 int document_node_id,
                                 int node_id,
                                 std::string tag_name,
                                 std::string element_id) {
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
}

void RecordBlinkDispatchPathWindow(uintptr_t event_identity,
                                   int document_node_id,
                                   uintptr_t target_identity,
                                   std::string interface_name) {
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
}

void CompleteBlinkDispatchStart(uintptr_t event_identity) {
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
  std::optional<std::string> listener_id =
      FindListenerIdentity(listener_identity);
  if (!listener_id ||
      !IsRecordableEventTarget(current_target_kind, current_document_node_id,
                               current_target_node_id)) {
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
  RecorderPipeClient* client = GetProcessRecorderClient();
  if (!client || timer_identity == 0 || document_node_id <= 0 ||
      timeout_id <= 0 || requested_delay_milliseconds < 0 ||
      effective_delay_milliseconds < 0 || nesting_level < 0) {
    return;
  }

  EvidenceIdentityStorage::TimerState state = RegisterTimerIdentity(
      timer_identity, document_node_id, repeating ? "interval" : "timeout",
      requested_delay_milliseconds, effective_delay_milliseconds,
      nesting_level);
  base::DictValue payload =
      CreateTimerPayload(*client, state, std::nullopt, std::nullopt,
                         page_lifecycle_state);
  SendBlinkEvidence("browser.timer", "timer-scheduled", std::move(payload));
}

void RecordBlinkTimerFired(uintptr_t timer_identity,
                           bool repeating,
                           int page_lifecycle_state) {
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
  RecorderPipeClient* client = GetProcessRecorderClient();
  if (!client || document_node_id <= 0 || document_token.empty() ||
      reason.empty() ||
      maximum_nodes <= 0) {
    return 0;
  }
  const uint64_t checkpoint_sequence = AssignDomCheckpointIdentity();
  base::DictValue payload = CreateDomCheckpointBasePayload(
      *client, checkpoint_sequence, document_node_id,
      std::move(document_token));
  payload.Set("reason", std::move(reason));
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
                                int maximum_value_length) {
  RecorderPipeClient* client = GetProcessRecorderClient();
  if (!client || checkpoint_sequence == 0 || document_node_id <= 0 ||
      document_token.empty() ||
      reason.empty() || node_count < 0 || maximum_nodes <= 0 ||
      attribute_count < 0 || maximum_attributes_per_node <= 0 ||
      maximum_value_length <= 0) {
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

void RecordBlinkSchedulerWakeUpDeferred(
    int queue_type,
    int throttling_type,
    int64_t desired_wake_up_microseconds,
    int64_t allowed_wake_up_microseconds,
    bool has_ready_task,
    int block_type) {
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
  g_pending_cookie_store_write_resolver = resolver_identity;
}

void RecordBlinkCookieStoreWrite(std::string method,
                                 std::string context_kind,
                                 int document_node_id,
                                 std::string document_token,
                                 bool threw,
                                 CookieWriteRequest request,
                                 CookieCallOrigin origin) {
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

// The last counters each document reported to a layout checkpoint, keyed by
// the document's DOM node identifier, which is unique within one renderer
// process. A rendering update whose counters match is not recorded again.
struct LayoutCheckpointStorage {
  base::Lock lock;
  uint64_t next_checkpoint_id = 1;
  struct DocumentCounters {
    unsigned style_resolution_count = 0;
    unsigned layout_count = 0;
    uint64_t checkpoint_sequence = 0;
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
    checkpoint_sequence = storage.next_checkpoint_id++;
    storage.documents.insert_or_assign(
        document_node_id,
        LayoutCheckpointStorage::DocumentCounters{
            style_resolution_count, layout_count, checkpoint_sequence});
  }
  base::DictValue payload = CreateLayoutCheckpointBasePayload(
      *client, checkpoint_sequence, document_node_id,
      std::move(document_token));
  payload.Set("reason", "rendering-update");
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

void RecordBlinkLayoutCheckpointNode(uint64_t checkpoint_sequence,
                                     int document_node_id,
                                     std::string document_token,
                                     LayoutCheckpointNode node) {
  RecorderPipeClient* client = GetProcessRecorderClient();
  if (!client || checkpoint_sequence == 0 || document_node_id <= 0 ||
      document_token.empty() || node.node_index < 0 || node.node_id <= 0 ||
      node.node_name.empty() || (node.node_type != 1 && node.node_type != 3)) {
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
  base::DictValue payload = CreateLayoutCheckpointBasePayload(
      *client, checkpoint_sequence, document_node_id,
      std::move(document_token));
  payload.Set("nodeIndex", node.node_index);
  payload.Set("nodeId", node.node_id);
  payload.Set("nodeType", DomNodeTypeName(node.node_type));
  payload.Set("nodeName", std::move(node.node_name));
  payload.Set("layoutObjectPresent", node.layout_object_present);
  payload.Set("displayLocked", node.display_locked);
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
  if (node.computed_style_present) {
    base::DictValue style;
    for (LayoutCheckpointStyleValue& entry : node.computed_style) {
      if (entry.property_name.empty()) {
        return;
      }
      style.Set(entry.property_name,
                entry.value_present ? base::Value(std::move(entry.value))
                                    : base::Value());
    }
    payload.Set("computedStyle", std::move(style));
  } else {
    payload.Set("computedStyle", base::Value());
  }
  SendBlinkEvidence("browser.layout", "layout-checkpoint-node",
                    std::move(payload));
}

void CompleteBlinkLayoutCheckpoint(uint64_t checkpoint_sequence,
                                   int document_node_id,
                                   std::string document_token,
                                   int node_count,
                                   bool truncated,
                                   int maximum_nodes) {
  RecorderPipeClient* client = GetProcessRecorderClient();
  if (!client || checkpoint_sequence == 0 || document_node_id <= 0 ||
      document_token.empty() || node_count < 0 || maximum_nodes <= 0) {
    return;
  }
  base::DictValue payload = CreateLayoutCheckpointBasePayload(
      *client, checkpoint_sequence, document_node_id,
      std::move(document_token));
  payload.Set("reason", "rendering-update");
  payload.Set("nodeCount", node_count);
  payload.Set("truncated", truncated);
  payload.Set("maximumNodes", maximum_nodes);
  SendBlinkEvidence("browser.layout", "layout-checkpoint-completed",
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
  value.Set("encodedDataLength", NetworkQuantity(response.encoded_data_length));
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
  RecorderPipeClient* client = GetProcessRecorderClient();
  if (!client) {
    return;
  }
  base::DictValue payload;
  payload.Set("context", CreateNetworkRendererContext(*client, scope, nullptr));
  payload.Set("scope", CreateNetworkScope(std::move(scope)));
  payload.Set("inspectorId", InspectorId(inspector_id));
  payload.Set("encodedDataLength", NetworkQuantity(encoded_data_length));
  payload.Set("decodedBodyLength", NetworkQuantity(decoded_body_length));
  payload.Set("finishBeforeRecordMilliseconds",
              NetworkMilliseconds(finish_before_record));
  SendBlinkEvidence("browser.network", "request-finished", std::move(payload));
}

void RecordBlinkNetworkFailed(NetworkScope scope,
                              uint64_t inspector_id,
                              NetworkFailureFacts failure) {
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

}  // namespace a11y_recorder
