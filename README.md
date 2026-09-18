# Windows Accessibility Session Recorder

A Windows-first research tool for recording accessibility and usability test sessions as synchronized raw evidence.

The project is intended to produce one standalone application that works with NVDA, JAWS, Narrator, and other Windows assistive technologies without requiring screen-reader add-ons, browser extensions, or external browser drivers. It combines platform-level Windows capture with a bundled instrumented Chromium build. Later analysis will infer user behavior while preserving the distinction between observation and interpretation.

## Status

The first development baseline is
[`v0.1.0`](https://github.com/bobdodd/windows-a11y-recorder/releases/tag/v0.1.0).
It is a source release for research and continued development, not an
end-user production release. See the [changelog](CHANGELOG.md) for the
baseline scope and known limitations.

The repository contains a working technical prototype. The managed solution
currently implements:

- A shared session clock, append-only event writer, manifest finalization, and
  archive validation.
- Raw keyboard and mouse input capture.
- Foreground-window and process observation.
- Desktop frame capture with Windows Graphics Capture and a GDI fallback.
- Microphone and system-audio capture.
- Windows UI Automation event capture.
- An accessible WPF recorder and synchronized session player.
- An authenticated local bridge that launches an instrumented Chromium build,
  synchronizes its clock, and records explicit browser connection lifecycle
  evidence.

The Chromium browser-process bridge was built and exercised end to end on the
reference Windows platform on September 18, 2026. The resulting archive was
valid, contained the expected connection and clock-synchronization records,
accepted 92 records, and dropped none. See the
[Chromium connection validation record](docs/validation/chromium-connection-2026-09-18.md).

Child-process capability distribution and Blink instrumentation remain future
work. The full direction is documented in the
[prototype plan](docs/prototype-plan.md).

## Project goals

- Capture raw keyboard and mouse evidence.
- Investigate touch, pen, touchpad, and HID capture.
- Record display or window video.
- Record participant microphone audio.
- Isolate screen-reader audio by process where Windows permits it.
- Observe Windows UI Automation events and selected accessibility snapshots.
- Record complete browser listener registration, event dispatch, browser default actions, timers, DOM state, accessibility state, cookie operations, and rendering evidence.
- Store synchronized evidence in an open, versioned session archive.
- Provide an accessible timeline viewer.
- Infer screen-reader behavior only after recording, with provenance and confidence.

## Initial constraints

- Windows 10 and Windows 11 x64 are the prototype platforms.
- The first version must not require screen-reader plugins.
- Browser testing uses the instrumented Chromium build shipped with the application.
- The application must not use Selenium, WebDriver, or another external browser-control process.
- Capture is local-first.
- Raw evidence remains immutable.
- Derived and inferred records are stored separately.
- Missing or uncertain evidence must be reported explicitly.

## Implementation sequence

Completed:

1. Define the event envelope, session manifest, privacy model, and archive
   format.
2. Implement Raw Input, display capture, audio capture, foreground-window
   observation, and UI Automation observation.
3. Build the core recorder and accessible synchronized timeline viewer.
4. Implement and validate the authenticated recorder-to-Chromium browser
   process connection and clock synchronization.
5. Implement recorder capability propagation, independent authentication, and
   clock synchronization for Chromium renderer, GPU, and utility processes.

Remaining:

1. Validate child-process capability propagation on the reference Chromium
   Windows build.
2. Add Blink and browser-process evidence hooks for listeners, dispatch,
   default actions, timers, cookies, DOM, accessibility, network, and
   rendering.
3. Record representative NVDA, JAWS, and Narrator sessions.
4. Add evidence correlation and screen-reader behavior analysis.
5. Investigate touch and gesture coverage on representative hardware.

## Build and test

The managed solution requires the .NET 10 SDK and targets Windows 10 version
2004 or later:

```powershell
dotnet restore .\windows-a11y-recorder.slnx
dotnet test .\windows-a11y-recorder.slnx --configuration Release
```

Run the Chromium integration-script test separately:

```powershell
python .\chromium\test_integrate.py
```

The Chromium build requires substantial disk space and the Visual Studio C++
toolchain. Run its setup from Windows PowerShell:

```powershell
.\chromium\setup-windows.ps1
```

The setup script checks out Chromium, applies the recorder bridge, generates
`out\A11yRecorder`, and builds the `chrome` target.

## Documentation

- [Release changelog](CHANGELOG.md)
- [Prototype plan](docs/prototype-plan.md)
- [Recommended Windows implementation stack](docs/architecture/windows-implementation-stack.md)
- [Instrumented Chromium architecture](docs/architecture/instrumented-chromium.md)
- [Prototype architecture](docs/architecture/prototype-architecture.md)
- [Collector contracts](docs/architecture/collector-contracts.md)
- [Reference Windows test platform](docs/architecture/reference-platform.md)
- [Threat model](docs/security/threat-model.md)
- [Privacy and data-handling policy](docs/security/privacy-and-data-handling-policy.md)
- [Chromium connection validation record](docs/validation/chromium-connection-2026-09-18.md)

## Repository visibility

This repository is private while the architecture, privacy controls, and prototype are being developed.
