# Changelog

This project uses semantic version numbers for product releases. Protocol and
archive schema versions are compatibility contracts and advance independently
from the product version.

## Unreleased

### Added

- Browser-to-child recorder capability propagation for renderer, GPU, and
  utility processes using inherited read-only shared memory.
- Independent child-process authentication and clock synchronization.
- Browser parent process and Chromium child process identifiers in lifecycle
  evidence.

### Changed

- Advanced the browser evidence protocol to version 0.2.
- Restricted accepted browser evidence connections to the browser, renderer,
  GPU, and utility process types with required process correlation metadata.
- Replaced the child-launch handoff's module-local static dependency with a
  browser-process metadata marker so `chrome.dll` and `content/browser` share
  the same read-only shared-memory capability. The marker contains no
  authentication token and is removed from child environments.
- Added opt-in Chromium file logging through
  `A11Y_RECORDER_CHROMIUM_LOG_FILE` for diagnosing early browser and child
  process startup failures.
- Added opt-in native bridge tracing through
  `A11Y_RECORDER_BRIDGE_LOG_FILE` for failures that occur before Chromium's
  normal logging system is initialized, including child-launch hook entry and
  early-return diagnostics.
- Moved Windows child bootstrap attachment to Chromium's shared child-launch
  sequence and made integration remove both historical forms of the obsolete
  platform-local hook.

## 0.1.0 - 2026-09-18

The first development baseline of the Windows Accessibility Session Recorder.
This source release establishes the tested recorder, session, playback, and
instrumented Chromium connection foundation for subsequent evidence
instrumentation.

### Included

- A shared monotonic session clock, append-only event storage, manifest
  finalization, and archive validation.
- Raw keyboard and mouse input capture.
- Foreground-window and process observation.
- Desktop frame capture using Windows Graphics Capture with a GDI fallback.
- Microphone and system-audio capture.
- Windows UI Automation event capture.
- An accessible WPF recording and synchronized playback interface.
- An authenticated local Chromium bridge with bootstrap, protocol framing,
  browser-process connection, clock synchronization, and lifecycle evidence.
- Versioned event, manifest, and browser evidence contracts.
- Automated Windows build and test coverage in GitHub Actions.

### Validation baseline

- Windows 10 22H2, build 19045, x64.
- .NET SDK 10.0.401 and runtime 10.0.12.
- Chromium 156.0.8065.0 built with the Chromium Clang toolchain.
- Instrumented Chromium connection recording completed with 92 accepted
  records and no dropped records.
- The resulting session completed archive validation with 77 artifacts and no
  validation issues.
- The focused browser receiver tests passed, 2 of 2.
- The release commit passed the repository's Windows CI workflow.

See the
[Chromium connection validation record](docs/validation/chromium-connection-2026-09-18.md)
for the detailed environment, procedure, evidence, and scope boundary.

### Known limitations

- This is a source baseline and does not include packaged application or
  Chromium binaries.
- Child-process capability distribution is not implemented.
- Renderer and Blink evidence hooks are not implemented.
- Listener registration, dispatch, browser default-action, timer, cookie,
  DOM, accessibility, network, and compositor evidence are not yet recorded.
- Representative NVDA, JAWS, and Narrator validation remains outstanding.
- Touch and gesture coverage remains under investigation.
