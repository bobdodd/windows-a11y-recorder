# Windows Accessibility Session Recorder

A Windows-first research tool for recording accessibility and usability test sessions as synchronized raw evidence.

The project is intended to produce one standalone application that works with NVDA, JAWS, Narrator, and other Windows assistive technologies without requiring screen-reader add-ons or browser extensions. It will capture input, audio, video, Windows accessibility events, application context, and recorder diagnostics. Later analysis will infer user behavior while preserving the distinction between observation and interpretation.

## Status

This repository is at the planning and technical-prototyping stage.

The current direction is documented in the [Windows Accessibility Session Recorder prototype plan](docs/prototype-plan.md).

## Initial goals

- Capture raw keyboard and mouse evidence.
- Investigate touch, pen, touchpad, and HID capture.
- Record display or window video.
- Record participant microphone audio.
- Isolate screen-reader audio by process where Windows permits it.
- Observe Windows UI Automation events and selected accessibility snapshots.
- Store synchronized evidence in an open, versioned session archive.
- Provide an accessible timeline viewer.
- Infer screen-reader behavior only after recording, with provenance and confidence.

## Initial constraints

- Windows 11 is the prototype platform.
- The first version must not require screen-reader plugins.
- The first version must not require a browser extension or modified Chromium.
- Capture is local-first.
- Raw evidence remains immutable.
- Derived and inferred records are stored separately.
- Missing or uncertain evidence must be reported explicitly.

## Planned sequence

1. Define the event envelope, session manifest, privacy model, and archive format.
2. Prototype Raw Input, Windows Graphics Capture, WASAPI process loopback, and UI Automation observation.
3. Record representative NVDA, JAWS, and Narrator sessions.
4. Build the core recorder and accessible timeline viewer.
5. Add evidence correlation and screen-reader behavior analysis.
6. Investigate touch and gesture coverage.
7. Use real-session gaps to select the smallest necessary browser integration.

## Documentation

- [Prototype plan](docs/prototype-plan.md)

## Repository visibility

This repository is private while the architecture, privacy controls, and prototype are being developed.
