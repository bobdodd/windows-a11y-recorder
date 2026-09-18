#include "chromium/recorder_bridge/browser_bridge.h"

#include <windows.h>

#include <memory>
#include <optional>
#include <string>
#include <utility>

#include "base/command_line.h"
#include "base/containers/span.h"
#include "base/environment.h"
#include "base/memory/read_only_shared_memory_region.h"
#include "base/memory/shared_memory_switch.h"
#include "base/no_destructor.h"
#include "base/process/launch.h"
#include "base/strings/string_number_conversions.h"
#include "base/win/windows_handle_util.h"
#include "chromium/recorder_bridge/recorder_protocol.h"
#include "chromium/recorder_bridge/recorder_switches.h"
#include "components/version_info/version_info.h"
#include "content/public/common/content_switches.h"

namespace a11y_recorder {
namespace {

std::unique_ptr<RecorderPipeClient>& ProcessClientStorage() {
  static base::NoDestructor<std::unique_ptr<RecorderPipeClient>> client;
  return *client;
}

base::ReadOnlySharedMemoryRegion& ChildBootstrapStorage() {
  static base::NoDestructor<base::ReadOnlySharedMemoryRegion> region;
  return *region;
}

bool IsSupportedChildProcess(std::string_view process_type) {
  return process_type == switches::kRendererProcess ||
         process_type == switches::kGpuProcess ||
         process_type == switches::kUtilityProcess;
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

  // recorder_bridge is linked into both chrome.dll and content/browser.
  // Module-local static storage is therefore not a reliable way to hand the
  // region to the child-launch hook. Publish only Chromium's opaque serialized
  // handle metadata through the process environment. The authentication token
  // remains exclusively inside the read-only shared-memory region.
  base::CommandLine metadata_command_line(base::CommandLine::NO_PROGRAM);
  base::LaunchOptions metadata_launch_options;
  base::shared_memory::SharedMemorySwitch bootstrap_switch(
      kChildBootstrapHandleSwitch, 0, 0);
  bootstrap_switch.AddToLaunchParameters(
      ChildBootstrapStorage(), &metadata_command_line,
      &metadata_launch_options);
  const std::string metadata = metadata_command_line.GetSwitchValueASCII(
      kChildBootstrapHandleSwitch);
  if (metadata.empty() ||
      !base::Environment::Create()->SetVar(
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
      command_line.GetSwitchValueASCII(switches::kProcessType);
  if (process_type.empty()) {
    if (!command_line.HasSwitch(kBootstrapSwitch)) {
      base::Environment::Create()->UnSetVar(
          kChildBootstrapMetadataEnvironment);
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
  return true;
}

bool AppendRecorderBootstrapToChildProcess(base::CommandLine* command_line,
                                           base::LaunchOptions* launch_options,
                                           int child_process_id,
                                           std::string* error) {
  if (!command_line || !launch_options || !error || child_process_id <= 0) {
    return false;
  }
  error->clear();

  const std::optional<std::string> metadata =
      base::Environment::Create()->GetVar(
          kChildBootstrapMetadataEnvironment);
  if (!metadata.has_value()) {
    return true;
  }
  const std::string process_type =
      command_line->GetSwitchValueASCII(switches::kProcessType);
  if (!IsSupportedChildProcess(process_type)) {
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
  return true;
}

RecorderPipeClient* GetProcessRecorderClient() {
  return ProcessClientStorage().get();
}

}  // namespace a11y_recorder
