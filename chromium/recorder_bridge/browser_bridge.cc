#include "chromium/recorder_bridge/browser_bridge.h"

#include <windows.h>

#include <array>
#include <memory>
#include <optional>
#include <string>
#include <utility>

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
  if (path_length == 0 || path_length >= path.size()) {
    return;
  }

  HANDLE file =
      ::CreateFileW(path.data(), FILE_APPEND_DATA,
                    FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
                    nullptr, OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
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
  ::CloseHandle(file);
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

std::string NextListenerId() {
  EvidenceIdentityStorage& identities = EvidenceIdentities();
  base::AutoLock lock(identities.lock);
  return "listener-" + base::NumberToString(identities.next_listener_id++);
}

std::string NextDispatchId() {
  EvidenceIdentityStorage& identities = EvidenceIdentities();
  base::AutoLock lock(identities.lock);
  return "dispatch-" + base::NumberToString(identities.next_dispatch_id++);
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
  return process_type == kChromiumRendererProcess ||
         process_type == kChromiumGpuProcess ||
         process_type == kChromiumUtilityProcess;
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
  auto region = base::shared_memory::ReadOnlySharedMemoryRegionFrom(
      command_line.GetSwitchValueASCII(kChildBootstrapHandleSwitch));
  if (!region.has_value() || !region->IsValid()) {
    *error = "The inherited child recorder bootstrap was invalid.";
    return false;
  }
  base::ReadOnlySharedMemoryMapping mapping = region->Map();
  if (!mapping.IsValid()) {
    *error = "The inherited child recorder bootstrap could not be mapped.";
    return false;
  }
  const auto chars = base::as_chars(mapping.GetMemoryAsSpan<uint8_t>());
  return ParseBootstrapConfiguration(
      std::string_view(chars.data(), chars.size()), configuration, error);
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
      return true;
    }
    if (!IsSupportedChildProcess(process_type)) {
      *error =
          "Recorder bootstrap was supplied to an unsupported child process.";
      return false;
    }
    if (!ReadChildBootstrap(command_line, &configuration, error)) {
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
  }

  auto client = std::make_unique<RecorderPipeClient>(std::move(configuration));
  if (!client->ConnectAndSynchronize(
          process_type, std::string(version_info::GetVersionNumber()), error)) {
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

void RecordBlinkListenerRegistered(int document_node_id,
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

  base::DictValue payload;
  payload.Set("context", CreateContext(*client, document_node_id));
  payload.Set("listenerId", NextListenerId());
  payload.Set("eventName", std::move(event_name));
  payload.Set("registrationKind", "add-event-listener");
  payload.Set("target", CreateNode(document_node_id, target_node_id,
                                   std::move(target_tag_name),
                                   std::move(target_element_id)));
  payload.Set("capture", capture);
  payload.Set("passive", passive);
  payload.Set("once", once);
  payload.Set("location", base::Value());
  SendBlinkEvidence("browser.listener", "listener-registered",
                    std::move(payload));
}

void RecordBlinkDispatchStarted(int document_node_id,
                                int target_node_id,
                                std::string event_name,
                                std::string target_tag_name,
                                std::string target_element_id,
                                bool trusted) {
  RecorderPipeClient* client = GetProcessRecorderClient();
  if (!client || document_node_id <= 0 || target_node_id <= 0) {
    return;
  }

  base::DictValue original_target = CreateNode(
      document_node_id, target_node_id, target_tag_name, target_element_id);
  base::DictValue payload;
  payload.Set("context", CreateContext(*client, document_node_id));
  payload.Set("dispatchId", NextDispatchId());
  payload.Set("eventName", std::move(event_name));
  payload.Set("trusted", trusted);
  payload.Set("originalTarget", std::move(original_target));
  payload.Set("composedPath", base::ListValue());
  payload.Set("phase", "none");
  payload.Set("listenerId", base::Value());
  payload.Set("defaultPrevented", false);
  payload.Set("propagationStopped", false);
  payload.Set("immediatePropagationStopped", false);
  payload.Set("defaultAction", base::Value());
  payload.Set("outcome", base::Value());
  SendBlinkEvidence("browser.dispatch", "dispatch-started", std::move(payload));
}

}  // namespace a11y_recorder
