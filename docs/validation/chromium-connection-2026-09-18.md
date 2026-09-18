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

## Scope boundary

This validation proves the browser-process bootstrap, local named-pipe
authentication, protocol framing, clock synchronization, lifecycle evidence,
session finalization, and archive validation.

It does not yet prove child-process capability distribution, renderer
connections, Blink listener instrumentation, event-dispatch instrumentation,
timer instrumentation, DOM or accessibility snapshots, cookie operations, or
network and compositor evidence. Those remain explicit future stages.
