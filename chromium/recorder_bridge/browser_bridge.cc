#include "chromium/recorder_bridge/browser_bridge.h"

#include <windows.h>

#include <array>
#include <memory>
#include <optional>
#include <string>
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
    int column_number) {
  base::DictValue payload;
  payload.Set("context", CreateContext(client, document_node_id));
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

void SendBlinkEvidence(std::string channel,
                       std::string event_type,
                       base::DictValue payload) {
  RecorderPipeClient* client = GetProcessRecorderClient();
  if (!client) {
    return;
  }
  std::string error;
  if (!client->SendEvidence(QueryEvidenceTicks(), std::move(channel),
                            std::move(event_type), std::move(payload),
                            base::ListValue(), &error)) {
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
                                   int column_number) {
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
      script_id, line_number, column_number);
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
                                int column_number) {
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
      script_id, line_number, column_number);
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
                                        int column_number) {
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
      script_id, line_number, column_number);
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

}  // namespace a11y_recorder
