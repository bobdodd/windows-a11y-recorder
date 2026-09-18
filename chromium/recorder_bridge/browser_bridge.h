#ifndef WINDOWS_A11Y_RECORDER_CHROMIUM_RECORDER_BRIDGE_BROWSER_BRIDGE_H_
#define WINDOWS_A11Y_RECORDER_CHROMIUM_RECORDER_BRIDGE_BROWSER_BRIDGE_H_

#include <string>

namespace a11y_recorder {

class RecorderPipeClient;

// Initializes the recorder connection in the unsandboxed browser process.
// Returns true when recording was not requested or the connection is ready.
// When the bootstrap switch is present, failure is fatal to recorder-launched
// Chromium so a session cannot silently continue without browser evidence.
bool InitializeBrowserProcessBridge(std::string* error);

// Returns the connected browser-process client, or nullptr when Chromium was
// not launched by the recorder.
RecorderPipeClient* GetBrowserProcessRecorderClient();

}  // namespace a11y_recorder

#endif  // WINDOWS_A11Y_RECORDER_CHROMIUM_RECORDER_BRIDGE_BROWSER_BRIDGE_H_
