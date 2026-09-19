# Instrumented Chromium Connection Validation

## Purpose

This record documents the first verified build and live connection between the
Windows A11y Recorder capture host and its instrumented Chromium browser
process. It records what was tested, what passed, and what remains outside the
validated scope.

## Environment

- Validation date: September 18, 2026
- Operating system: Windows 10 22H2, build 19045, x64
- .NET SDK: 10.0.401
- .NET runtime: 10.0.12
- Chromium version: 156.0.8065.0
- Chromium output: `out\A11yRecorder\chrome.exe`
- C++ compiler: Chromium Clang using the installed Visual Studio Build Tools
  toolchain
- Windows SDK: 10.0.28000.0

## Native build fixes validated

The integration and native bridge were adjusted for the checked-out Chromium
revision:

- The GN dependency is inserted into the Windows
  `shared_library("chrome_dll")` target in `chrome/BUILD.gn`.
- The bridge uses the current `base::DictValue` and `base::ListValue` types.
- JSON parsing supplies `base::JSON_PARSE_RFC`.
- Exact pipe reads and writes use spans rather than unsafe pointer arithmetic.
- The Chromium version string view is explicitly converted to
  `std::string`.
- The setup script does not invoke `gclient` without a command before
  `gclient sync`.

The final `autoninja -C out\A11yRecorder chrome` run completed successfully.
The `//chrome:chrome_dll` dependency graph included
`//chromium/recorder_bridge:recorder_bridge`.

## Managed tests

The focused browser receiver test command was:

```powershell
dotnet test `
  .\tests\Recorder.Tests\Recorder.Tests.csproj `
  --configuration Debug `
  --filter "FullyQualifiedName~BrowserEvidenceReceiverTests"
```

Result:

- Tests: 2
- Passed: 2
- Failed: 0
- Skipped: 0
- Exit code: 0

The tests cover:

- Authentication, clock synchronization, and browser evidence acceptance.
- Ordered `browser-connected` and `browser-clock-synchronized` lifecycle
  records.
- Exclusion of the authentication token from lifecycle payloads.
- Rejection of a client that supplies an invalid authentication token.

## Live connection test

The capture host launched the instrumented Chromium executable for a
15-second `about:blank` recording. The process completed with exit code 0.

The finalized session contained:

- Status: `completed`
- Accepted events: 92
- Dropped events: 0
- Browser collector capability: `Supported`
- Browser collector lifecycle: `Disposed`
- Browser collector health: `Healthy`
- Archive artifacts validated: 77
- Archive validation issues: 0

The first two browser records were:

1. Sequence 0, `browser-connected`, protocol 0.1, browser process,
   Chromium 156.0.8065.0.
2. Sequence 1, `browser-clock-synchronized`, with the same browser instance
   and process identifiers, a monotonic frequency of 10,000,000 ticks per
   second, and a measured uncertainty of 1,465,250 nanoseconds.

No `browser-connection-rejected` record occurred. The event archive contained
no `authenticationToken` field.

The generated recording and Chromium binaries are validation artifacts and are
not committed to the repository.

## Protocol 0.2 child-process validation attempt

After protocol 0.2 and child-process capability propagation were implemented,
the native Chromium build completed successfully and the focused managed
receiver suite passed three tests with no failures.

The capture host then launched the instrumented Chromium executable for a
20-second recording. The finalized session was:

`C:\Users\Public\Documents\A11yRecorderChildProcessTest\20260918-223436-3c3d88aa1c904fadb95a7a49e45754ce`

The session completed with 117 accepted events, zero dropped events, a healthy
browser collector, and no archive failure. It contained the expected protocol
0.2 `browser-connected` and `browser-clock-synchronized` records for browser OS
process 10160. It contained no renderer, GPU, or utility lifecycle records and
no `browser-connection-rejected` record. No `authenticationToken` field was
persisted.

This attempt therefore validates the protocol 0.2 browser-process path but does
not validate child-process capability propagation. Inspection identified that
the bridge is linked into both `chrome.dll` and `content/browser`, so
module-local static storage cannot be used as the handoff between browser
startup and the child-launch hook. The corrective implementation publishes
only Chromium's opaque shared-memory handle metadata through a browser-process
environment marker, explicitly inherits the handle at child launch, and
removes the marker from the child environment. The authentication token
remains exclusively inside the read-only shared-memory region. A repeated
native build and live child-process session are required before child
propagation is considered validated.

## September 19 renderer and Blink validation

The remaining renderer capability and initial Blink evidence scope was
validated on September 19, 2026, using commit `08b75fb`. The complete
validation script exited with code 0. The session was:

`C:\Users\Public\Documents\A11yRecorderBlinkValidation\20260919-143342-bd566cd94efe4ba79d20e0c67b90e5dc`

The archive validator accepted 371 events and 81 artifacts. The fixture
verifier found the expected renderer connection, `click` listener registration
for `#pointer-only`, and programmatic `click` dispatch start for the same
document and node. Chromium reported zero network-service crashes.

This final run used a renderer-only child capability boundary. Earlier
distribution to GPU and utility processes caused the network-service utility
process to restart repeatedly. Those process types remain excluded until
dedicated evidence hooks require and validate their participation.

## Scope boundary

Together, the September 18 and September 19 validations prove browser and
renderer bootstrap, local named-pipe authentication across the Chromium
sandbox boundary, protocol framing, per-process clock synchronization,
lifecycle evidence, initial Blink Node listener-registration evidence, initial
dispatch-start evidence, session finalization, and archive validation.

This connection record did not originally prove listener removal, listener
invocation, complete composed paths, or propagation behavior. Those capabilities
were subsequently validated on September 19, 2026, as documented in the
[Blink propagation validation record](blink-propagation-2026-09-19.md).
Default actions, timer instrumentation, DOM or accessibility snapshots, cookie
operations, network evidence, compositor evidence, and rendering evidence
remain explicit future stages.
