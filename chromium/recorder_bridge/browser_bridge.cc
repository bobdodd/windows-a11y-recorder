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
#include "base/process/launch.h"
#include "base/strings/string_number_conversions.h"
#include "base/synchronization/lock.h"
#include "base/win/windows_handle_util.h"
#include "chromium/recorder_bridge/recorder_protocol.h"
#include "chromium/recorder_bridge/recorder_switches.h"
#include "components/version_info/version_info.h"

namespace a11y_recorder {
namespace {

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
  std::unordered_map<uintptr_t, std::string> listener_ids;

  struct NodeState {
    int document_node_id;
    int node_id;
    std::string tag_name;
    std::string element_id;
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
  std::unordered_map<uintptr_t, DispatchState> dispatches;
  std::unordered_map<uintptr_t, InvocationState> active_invocations;
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

base::DictValue CreateContext(const RecorderPipeClient& client,
                              int document_node_id) {
  base::DictValue context;
  context.Set("browserInstanceId", client.browser_instance_id());
  context.Set("processId", static_cast<int>(::GetCurrentProcessId()));
  context.Set("processType", client.process_type());
  context.Set("profileId", base::Value());
  context.Set("browserContextId", base::Value());
  context.Set("pageId", base::Value());
  context.Set("frameId", base::Value());
  context.Set("documentId", DocumentId(document_node_id));
  context.Set("executionWorldId", base::Value());
  return context;
}

base::DictValue CreateNode(int document_node_id,
                           int target_node_id,
                           std::string target_tag_name,
                           std::string target_element_id) {
  base::DictValue target;
  target.Set("documentId", DocumentId(document_node_id));
  target.Set("nodeId", target_node_id);
  target.Set("backendNodeId", base::Value());
  target.Set("tagName", std::move(target_tag_name));
  if (target_element_id.empty()) {
    target.Set("elementId", base::Value());
  } else {
    target.Set("elementId", std::move(target_element_id));
  }
  target.Set("classes", base::ListValue());
  return target;
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

base::DictValue CreateListenerPayload(
    const RecorderPipeClient& client,
    std::string listener_id,
    int document_node_id,
    int target_node_id,
    std::string event_name,
    std::string target_tag_name,
    std::string target_element_id,
    bool capture,
    bool passive,
    bool once) {
  base::DictValue payload;
  payload.Set("context", CreateContext(client, document_node_id));
  payload.Set("listenerId", std::move(listener_id));
  payload.Set("eventName", std::move(event_name));
  payload.Set("registrationKind", "add-event-listener");
  payload.Set("target", CreateNode(document_node_id, target_node_id,
                                   std::move(target_tag_name),
                                   std::move(target_element_id)));
  payload.Set("capture", capture);
  payload.Set("passive", passive);
  payload.Set("once", once);
  payload.Set("location", base::Value());
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
              CreateNode(state.document_node_id, state.target_node_id,
                         state.target_tag_name, state.target_element_id));
  base::ListValue composed_path;
  for (const auto& node : state.composed_path) {
    composed_path.Append(CreateNode(node.document_node_id, node.node_id,
                                    node.tag_name, node.element_id));
  }
  payload.Set("composedPath", std::move(composed_path));
  if (current_target) {
    payload.Set("currentTarget",
                CreateNode(current_target->document_node_id,
                           current_target->node_id, current_target->tag_name,
                           current_target->element_id));
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
                                   int document_node_id,
                                   int target_node_id,
                                   std::string event_name,
                                   std::string target_tag_name,
                                   std::string target_element_id,
                                   bool capture,
                                   bool passive,
                                   bool once) {
  RecorderPipeClient* client = GetProcessRecorderClient();
  if (!client || document_node_id <= 0 || target_node_id <= 0) {
    return;
  }

  base::DictValue payload = CreateListenerPayload(
      *client, RegisterListenerIdentity(listener_identity), document_node_id,
      target_node_id, std::move(event_name), std::move(target_tag_name),
      std::move(target_element_id), capture, passive, once);
  SendBlinkEvidence("browser.listener", "listener-registered",
                    std::move(payload));
}

void RecordBlinkListenerRemoved(uintptr_t listener_identity,
                                int document_node_id,
                                int target_node_id,
                                std::string event_name,
                                std::string target_tag_name,
                                std::string target_element_id,
                                bool capture,
                                bool passive,
                                bool once) {
  RecorderPipeClient* client = GetProcessRecorderClient();
  std::optional<std::string> listener_id =
      TakeListenerIdentity(listener_identity);
  if (!client || !listener_id || document_node_id <= 0 ||
      target_node_id <= 0) {
    return;
  }

  base::DictValue payload = CreateListenerPayload(
      *client, std::move(*listener_id), document_node_id, target_node_id,
      std::move(event_name), std::move(target_tag_name),
      std::move(target_element_id), capture, passive, once);
  SendBlinkEvidence("browser.listener", "listener-removed",
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
       .element_id = std::move(element_id)});
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
                                  int current_document_node_id,
                                  int current_target_node_id,
                                  std::string current_target_tag_name,
                                  std::string current_target_element_id) {
  std::optional<std::string> listener_id =
      FindListenerIdentity(listener_identity);
  if (listener_id && current_document_node_id > 0 &&
      current_target_node_id > 0) {
    RegisterActiveInvocation(
        event_identity, std::move(*listener_id), current_document_node_id,
        current_target_node_id, std::move(current_target_tag_name),
        std::move(current_target_element_id));
  }
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
