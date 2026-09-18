#include "chromium/recorder_bridge/browser_bridge.h"

#include <memory>
#include <utility>
#include <string>

#include "base/command_line.h"
#include "base/no_destructor.h"
#include "chromium/recorder_bridge/recorder_protocol.h"
#include "chromium/recorder_bridge/recorder_switches.h"
#include "components/version_info/version_info.h"
#include "content/public/common/content_switches.h"

namespace a11y_recorder {
namespace {

std::unique_ptr<RecorderPipeClient>& BrowserClientStorage() {
  static base::NoDestructor<std::unique_ptr<RecorderPipeClient>> client;
  return *client;
}

}  // namespace

bool InitializeBrowserProcessBridge(std::string* error) {
  if (!error) {
    return false;
  }
  error->clear();

  const base::CommandLine& command_line =
      *base::CommandLine::ForCurrentProcess();
  if (command_line.HasSwitch(switches::kProcessType)) {
    return true;
  }
  if (!command_line.HasSwitch(kBootstrapSwitch)) {
    return true;
  }
  if (command_line.GetSwitchValueASCII(kBootstrapSwitch) !=
      kBootstrapFromStandardInput) {
    *error = "The recorder bootstrap switch must have the value 'stdin'.";
    return false;
  }
  if (BrowserClientStorage()) {
    *error = "The browser recorder bridge was initialized more than once.";
    return false;
  }

  BootstrapConfiguration configuration;
  if (!ReadBootstrapFromStandardInput(&configuration, error)) {
    return false;
  }

  auto client =
      std::make_unique<RecorderPipeClient>(std::move(configuration));
  if (!client->ConnectAndSynchronize(
          "browser", std::string(version_info::GetVersionNumber()), error)) {
    return false;
  }
  BrowserClientStorage() = std::move(client);
  return true;
}

RecorderPipeClient* GetBrowserProcessRecorderClient() {
  return BrowserClientStorage().get();
}

}  // namespace a11y_recorder
