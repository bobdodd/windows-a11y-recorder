# Windows Accessibility Session Recorder: Prototype Plan

## Purpose

This plan defines a Windows-first prototype for recording accessibility and usability test sessions. The recorder will operate as one standalone application and will not require plugins or add-ons for NVDA, JAWS, Narrator, browsers, or other assistive technologies.

The application will capture raw evidence during a session. Analysis performed afterward will use that evidence to infer how the participant interacted with the screen reader, browser, and tested interface. The system will preserve the distinction between direct observations and behavioral interpretations.

This platform recorder is the foundation for later browser instrumentation. Evidence from real recordings will determine whether the browser layer should be a conventional extension, a native companion, an instrumented Chromium distribution, or a combination.

## Primary recommendation

Build a standalone Windows recorder before modifying Chromium or developing a browser extension. The first version should capture:

- Raw keyboard input.
- Raw mouse input.
- Available touch, pen, touchpad, and HID input.
- Full-display or selected-window video.
- Participant microphone audio.
- Screen-reader audio isolated by process where possible.
- Whole-system audio as a fallback.
- Foreground process and window changes.
- Cursor position and window geometry.
- Windows UI Automation events and selected accessibility-tree snapshots.
- Environment and software metadata.
- Session annotations and recorder-health information.

The recorder should produce an open, versioned session archive and a basic timeline viewer. The initial viewer should present evidence without attempting to classify every action. Behavioral analysis should be introduced only after representative raw recordings exist.

## Product principles

### One application

The participant or auditor should install and run one application. The application may use internal worker processes or modular collectors, but these implementation details must not create additional installation or configuration steps.

The first prototype must not require:

- A screen-reader add-on.
- A browser extension.
- A modified browser.
- Python or Selenium.
- A remote control service.
- Manual configuration inside NVDA, JAWS, or Narrator.
- Administrator privileges for ordinary recording.

### Screen-reader independence

The recorder must work regardless of which screen reader is running. Screen-reader-specific knowledge belongs in the analysis layer, not in capture-time plugins.

The recorder may detect:

- Screen-reader process names.
- Product names and versions.
- Relevant process trees.
- Audio sessions associated with those processes.
- Publicly readable configuration information.

It must not inject code into the screen reader or modify its behavior.

### Raw evidence first

The recording should preserve the lowest-level practical observations before normalizing them. For example, a raw keyboard record, low-level keyboard-hook record, UI Automation focus change, speech utterance, and visual frame should remain separate evidence items even when later analysis decides that they describe one user action.

### Explicit inference

The system must distinguish:

- **Observed:** Directly captured by a recorder channel.
- **Derived:** Deterministically calculated from captured evidence.
- **Inferred:** A probable behavioral interpretation.
- **Unknown:** Not observable or not supported by sufficient evidence.

Every inferred action should include confidence, supporting evidence, competing interpretations, and the analysis method or model version.

### Local-first and privacy-preserving

Recording should occur locally. Export or cloud analysis must require a separate, explicit action.

The recorder should default to:

- Visible recording status.
- Clear channel-by-channel consent.
- Easy pause and stop controls.
- Password and secure-desktop suppression.
- Configurable redaction.
- No network transmission during capture.
- Recoverable partial archives after a crash.

## Prototype scope

### Included

- Windows 11 desktop.
- NVDA, JAWS, and Narrator as target screen readers.
- Chromium-based browsers as initial test applications.
- Full-display and selected-window recording.
- Keyboard and conventional mouse capture.
- Microphone, process-specific playback, and system playback capture.
- UI Automation observation.
- An append-only event archive.
- A basic timeline viewer.

### Investigated but not promised initially

- Touchscreen contact capture.
- Precision-touchpad gestures.
- Pen and stylus input.
- Braille-display input and output.
- Multiple displays.
- Audio isolation when a screen reader uses multiple unrelated processes.
- Elevated applications.
- Remote desktop and virtual-machine sessions.

### Excluded from the first prototype

- Browser DOM or source capture.
- Browser network capture.
- Browser accessibility-tree capture through CDP.
- Browser source-code modifications.
- Automated behavioral scoring.
- Automated accessibility conformance findings.
- Cloud upload or collaborative review.
- macOS, mobile, and Linux packages.

## Capture architecture

```text
Windows Accessibility Session Recorder
├── Accessible session controller
├── Session clock and event sequencer
├── Raw keyboard and mouse collector
├── Optional low-level input observer
├── Pointer, touch, pen, and HID research collector
├── Foreground-window and process observer
├── UI Automation observer
├── Display and window video capture
├── Microphone capture
├── Process-specific audio capture
├── System-audio fallback capture
├── Screen-reader detector
├── Annotation service
├── Recorder-health monitor
├── Local archive writer
└── Timeline viewer
```

Each collector should have a narrow interface and should emit immutable timestamped records. Capture components should not classify behavior during the session.

## Capture channels

### Session clock

All channels must map their timestamps to one monotonic session clock. Wall-clock time should be stored for external correlation but must not define event ordering.

Each event should contain:

- Schema version.
- Session identifier.
- Producer and producer version.
- Native producer timestamp.
- Mapped session timestamp.
- Sequence number.
- Process and thread identifiers where available.
- Capture method.
- Data quality or uncertainty fields.
- Links to related events when established deterministically.

Clock drift and mapping error should be measured throughout the session. If a channel cannot be synchronized within its expected tolerance, the archive must report the problem.

### Keyboard

Use Windows Raw Input as the primary keyboard source. Raw Input supplies device-level keyboard data and can deliver background input after explicit device registration ([Microsoft Raw Input documentation](https://learn.microsoft.com/en-us/windows/win32/inputdev/about-raw-input)).

Record:

- Device identifier.
- Make or break state.
- Scan code.
- Virtual key.
- Extended-key flags.
- Modifier state.
- Repeat information.
- Timestamp.
- Foreground process and window.
- Keyboard layout and input locale.

Evaluate a low-level keyboard hook as a secondary evidence channel. Windows calls a `WH_KEYBOARD_LL` hook before posting a new keyboard event to a thread input queue, but Microsoft recommends Raw Input for most asynchronous monitoring and requires hook callbacks to return quickly ([Microsoft LowLevelKeyboardProc documentation](https://learn.microsoft.com/en-us/windows/win32/winmsg/lowlevelkeyboardproc)).

The hook must only enqueue a compact record and return. Raw and hook records should remain separately identifiable and should be deduplicated during analysis.

### Mouse

Use Raw Input for device-relative movement and button data. Use cursor and window APIs to add screen coordinates, target window, hit region, and display information.

Record:

- Raw relative or absolute movement.
- Cursor screen position.
- Buttons and transitions.
- Vertical and horizontal wheel movement.
- Device identifier.
- Foreground window.
- Window beneath the pointer.
- Display and scale.
- Timestamp.

A low-level mouse hook may be evaluated as a secondary channel. Its callback has the same timing and non-blocking requirements as the keyboard hook ([Microsoft LowLevelMouseProc documentation](https://learn.microsoft.com/en-us/windows/win32/winmsg/lowlevelmouseproc)).

### Touch, pen, touchpad, and gestures

Treat this as a research workstream rather than assuming keyboard and mouse techniques generalize to every pointer device.

Windows pointer messages can describe touch, pen, mouse, and touchpad interactions, including contact identity, location, pressure, geometry, and frame history. These messages are generally routed to a target window, and Windows may transform pointer sequences into recognized gestures or mouse input ([Microsoft pointer input documentation](https://learn.microsoft.com/en-us/windows/win32/api/_inputmsg/), [Microsoft pointer message documentation](https://learn.microsoft.com/en-us/windows/win32/inputmsg/messages)).

The prototype should test:

- Raw HID availability for target hardware.
- `WM_POINTER` visibility outside the recorder's own window.
- Precision-touchpad APIs and restrictions.
- Whether screen-reader touch gestures appear as ordinary input.
- What survives when the screen reader or operating system consumes a gesture.
- Whether a later browser adapter is required for complete page-directed touch.

The recorder must not claim complete global gesture capture until these tests pass on representative hardware.

### Screen and window video

Use Windows Graphics Capture to acquire frames from a display or application window. The API provides a system picker, an explicit capture indicator, and a stream of captured Direct3D frames ([Microsoft Windows Graphics Capture documentation](https://learn.microsoft.com/en-us/windows/apps/develop/media-authoring-processing/screen-capture)).

Support:

- Full-display capture.
- Selected-window capture.
- Configurable frame rate and resolution.
- Cursor inclusion.
- Display scale and geometry changes.
- Frame timestamps and dropped-frame counts.

Full-display capture should be the default for accessibility testing because it can include browser chrome, screen-reader dialogs, task switching, magnification, and interactions outside the tested tab. Selected-window capture should remain available for privacy-sensitive sessions.

### Participant microphone

Record microphone audio as an independent track. Do not mix microphone audio with screen-reader or system audio during capture.

Record:

- Device and audio format.
- Sample timestamps.
- Dropouts and device changes.
- Pause and mute states.
- Input level and clipping diagnostics.

### Screen-reader and system audio

Use Windows process-loopback capture to isolate audio rendered by the detected screen-reader process and its child processes. Windows supports process-specific loopback capture through `ActivateAudioInterfaceAsync`, including or excluding a specified process tree ([Microsoft application loopback sample](https://learn.microsoft.com/en-us/samples/microsoft/windows-classic-samples/applicationloopbackaudio-sample/), [Microsoft process-loopback parameters](https://learn.microsoft.com/en-us/windows/win32/api/audioclientactivationparams/ns-audioclientactivationparams-audioclient_process_loopback_params)).

The recorder should:

- Detect known screen-reader processes.
- Allow the tester to confirm the detected process.
- Capture the process tree as a separate audio track.
- Monitor process restarts.
- Record periods of silence rather than dropping timeline continuity.
- Fall back to system loopback when isolation fails.
- Record the capture method and affected process IDs in the manifest.

WASAPI system-loopback recording can capture the shared render mix even without hardware loopback support ([Microsoft WASAPI loopback documentation](https://learn.microsoft.com/en-us/windows/win32/coreaudio/loopback-recording)).

Process isolation must be validated independently for NVDA, JAWS, and Narrator. A product may use helper or synthesizer processes outside the main process tree, so successful API activation does not prove that all screen-reader audio was isolated.

### Foreground application and environment

Record:

- Foreground window changes.
- Active process and executable identity.
- Window title and bounds.
- Process creation and exit.
- Browser and screen-reader product versions.
- Display topology and scaling.
- Keyboard layout and locale.
- Audio devices and default-device changes.
- Lock, sleep, session switch, and secure-desktop transitions where observable.

Sensitive window titles and process arguments should be redactable.

### Windows accessibility state

Use UI Automation as an independent observer of the accessibility state exposed by applications. UI Automation includes focus, property, selection, text, structure, window, and active-text-position events ([Microsoft UI Automation event identifiers](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-event-ids), [Microsoft UI Automation events overview](https://learn.microsoft.com/en-us/dotnet/framework/ui-automation/ui-automation-events-overview)).

Record:

- Automation focus changes.
- Active text-position changes.
- Selection and text-selection changes.
- Invoked and selected controls.
- Value, name, state, bounds, and visibility changes.
- Structure changes.
- Notifications.
- Menus, dialogs, tooltips, and window transitions.
- Accessible element identity and ancestry.

Take selected accessibility snapshots:

- At recording start.
- When the foreground application changes.
- After significant focus or active-text movement.
- Before and after participant annotations.
- At periodic recovery checkpoints.

Avoid continuous full-tree snapshots. UI Automation providers may generate large trees or expensive cross-process calls. Snapshot collection must use strict timeouts, cancellation, and health reporting.

UI Automation is evidence about the interface exposed through Windows. It does not directly reveal a screen reader's private virtual buffer, command dispatch, or internal cursor.

## Session archive

Use an open, versioned directory or ZIP-compatible container:

```text
session/
  manifest.json
  timeline.ndjson
  input/
    raw-keyboard.ndjson
    keyboard-hook.ndjson
    raw-mouse.ndjson
    pointer-hid.ndjson
  video/
    display-1.mp4
  audio/
    microphone.flac
    screen-reader.flac
    system.flac
  accessibility/
    events.ndjson
    snapshots/
  environment/
    processes.ndjson
    windows.ndjson
    displays.json
    devices.json
  annotations/
    annotations.ndjson
  diagnostics/
    collector-health.ndjson
    clock-mapping.ndjson
    omissions.json
  analysis/
    derived-events.ndjson
    inferred-actions.ndjson
```

Raw evidence must remain immutable. Derived and inferred records should be stored separately so analysis can be rerun without changing the captured session.

Large artifacts should be streamable and recoverable after interruption. The archive validator should check schema versions, sequence continuity, time ranges, media readability, expected channel coverage, and declared omissions.

## Analysis model

### Evidence correlation

The first analysis pass should create candidate interaction groups by temporal proximity and deterministic relationships:

- Raw key with matching hook record.
- Key sequence with foreground application.
- Input followed by UI Automation change.
- Input followed by screen-reader audio.
- Accessibility change followed by visual change.
- Speech interruption correlated with a new input event.
- Application switch correlated with focus and audio changes.

This pass should not assign a user-intent label.

### Screen-reader profiles

Add built-in analysis profiles for NVDA, JAWS, and Narrator. Profiles may include:

- Documented default key commands.
- Desktop and laptop layouts.
- Modifier conventions.
- Common mode-toggle utterances.
- Typical command sequences.
- Known process and synthesizer arrangements.
- Version-specific differences.

Profiles are analysis resources bundled with the recorder or viewer. They do not modify the screen reader.

### Behavioral inference

An inferred action should use multiple evidence channels when possible:

```json
{
  "action": "navigate-to-next-heading",
  "screenReader": "NVDA",
  "confidence": 0.94,
  "evidence": [
    "raw-keyboard:18291",
    "foreground-window:554",
    "uia-active-text-position:2301",
    "screen-reader-utterance:881"
  ],
  "alternatives": [
    {
      "action": "typed-letter-h",
      "confidence": 0.04
    }
  ],
  "analysisMethod": "ruleset-nvda-1.0"
}
```

Confidence should not be generated from a model without calibration. Initial confidence categories may be more defensible than percentages:

- Confirmed by multiple independent observations.
- Strongly supported.
- Plausible.
- Ambiguous.
- Unknown.

### Speech analysis

Screen-reader audio should be segmented using input timing, silence, interruption, and application events. Transcription should preserve:

- Audio timestamps.
- Partial and interrupted utterances.
- Confidence.
- Unrecognized spans.
- Product-specific punctuation and earcons where possible.

The original audio remains authoritative. Transcription is a derived artifact.

## Privacy and security requirements

Raw keyboard capture creates keylogger-level risk. The recorder must make that risk visible and constrain it technically.

Required controls:

- Explicit session start and stop.
- Persistent recording indicator.
- Keyboard-accessible emergency stop.
- Pause without ending the session.
- No automatic startup with Windows.
- No recording before consent.
- No capture on the secure desktop.
- Password-field suppression where detectable.
- Configurable application and process exclusions.
- Optional keyboard capture limited to selected target processes.
- Local encryption.
- Configurable retention.
- Redaction before export.
- An audit log of pauses, exclusions, and redactions.

The application should avoid elevation. If elevated applications cannot be observed, the archive should report this limitation instead of requesting administrator access by default.

## Implementation phases

### Architecture and contracts

Deliver:

- Threat model.
- Capture-channel inventory.
- Event envelope schema.
- Monotonic clock design.
- Session manifest schema.
- Archive layout.
- Collector lifecycle contract.
- Recorder-health contract.
- Privacy and redaction policy.

Exit criteria:

- A synthetic session archive validates successfully.
- Every record identifies provenance.
- Missing channels and partial failure can be represented.
- Raw, derived, and inferred data are structurally separated.

### Core Windows recorder

Deliver:

- Accessible session controller.
- Raw keyboard collector.
- Raw mouse collector.
- Foreground-window and process observer.
- Full-display video capture.
- Microphone capture.
- Whole-system audio capture.
- Session archive writer.
- Crash-safe stop and recovery.

Exit criteria:

- A 60-minute session records without unbounded memory growth.
- Start, pause, resume, annotate, and stop are keyboard and screen-reader operable.
- Media and event timelines remain synchronized.
- Collector failure appears in diagnostics.
- Partial archives can be reopened.

### Screen-reader-independent evidence

Deliver:

- Screen-reader process detection.
- Process-specific audio capture.
- UI Automation event observer.
- Selected UI Automation snapshots.
- Separate audio tracks.
- Environment manifest.

Validate with:

- NVDA.
- JAWS.
- Narrator.
- Speech interruption.
- Browser and application switching.
- Browse and focus interactions.
- Dialogs and menus.

Exit criteria:

- The recorder requires no screen-reader configuration.
- Screen-reader audio is isolated or the fallback is declared.
- UI Automation observation does not destabilize the tested application.
- The archive identifies which screen reader and version were active.

### Timeline viewer

Deliver:

- Video and audio playback.
- Synchronized event list.
- Raw key and pointer display.
- Foreground-window track.
- UI Automation focus and state track.
- Screen-reader audio waveform.
- Annotation navigation.
- Recorder-health and omission display.

Exit criteria:

- A reviewer can move from any input event to the corresponding video frame, audio segment, active window, and nearest accessibility event.
- The viewer never presents inferred screen-reader behavior as captured fact.
- Keyboard and screen-reader access are tested across the primary workflow.

### Baseline behavioral analysis

Deliver:

- Input-event deduplication.
- Speech segmentation and transcription.
- Screen-reader profiles.
- Rule-based candidate actions.
- Confidence and provenance presentation.
- Manual confirmation and correction.

Exit criteria:

- Representative NVDA, JAWS, and Narrator sessions can be reviewed.
- Every inferred action links to supporting raw evidence.
- Ambiguous actions remain visibly ambiguous.
- Human corrections do not alter the original evidence.

### Touch and gesture investigation

Deliver:

- Hardware test matrix.
- Raw HID and pointer experiments.
- Precision-touchpad experiments.
- Screen-reader touch-interaction recordings.
- Coverage and omission report.

Decision:

- Continue with platform capture if adequate.
- Add a browser adapter for page-directed pointer sequences.
- Add a narrowly scoped native component if justified.
- Declare unsupported cases where capture would require unsafe or brittle techniques.

### Browser decision study

Use completed sessions to identify questions the platform recorder cannot answer. Classify each missing signal:

- Page-delivered DOM input.
- Exact DOM target.
- Original and live page source.
- Rendered layout and styles.
- Browser accessibility tree.
- Network resources.
- Browser pre-dispatch input.
- Browser chrome interaction.
- Compositor output.

Choose the least invasive browser approach that closes the demonstrated gaps:

| Need | Likely approach |
| --- | --- |
| DOM events, snapshots, network, and accessibility tree | Browser extension using `chrome.debugger` and CDP |
| Reliable local streaming and storage | Native host or built-in browser component |
| Browser pre-dispatch input and browser chrome | Chromium modification |
| Exact compositor and browser-process correlation | Chromium modification |
| Ordinary page behavior only | Extension or content instrumentation |

Do not begin a Chromium fork until this decision study identifies at least one required signal that cannot be collected reliably by the platform recorder and extension approach.

## Validation scenarios

Each screen reader should be tested with a shared set of tasks:

- Launch and close the screen reader.
- Open a browser and navigate to a test page.
- Navigate by heading, landmark, link, form control, and table.
- Switch between browse and focus interaction where applicable.
- Enter and edit text.
- Open a screen-reader element list or equivalent.
- Interrupt speech repeatedly.
- Change applications and return.
- Use browser chrome.
- Open a native dialog.
- Trigger a dynamic content update.
- Encounter a live notification.
- Use a user-remapped command where available.
- Run with speech disabled or muted.
- Run with a braille display where available.

Each scenario should answer:

- Which channels captured evidence?
- Which actions could be inferred?
- What confidence was justified?
- Which behavior remained unknown?
- Did recording change timing or screen-reader behavior?
- What browser information would have improved the conclusion?

## Technical risks

### Raw input privacy

Risk: The recorder can capture credentials and private communication.

Response: Constrain capture to explicit sessions, exclude secure surfaces, provide process filters and pauses, redact before export, and make raw input status continuously visible.

### Screen-reader audio isolation

Risk: Audio may be produced by a synthesizer process outside the detected screen-reader process tree.

Response: Validate each screen reader and synthesizer combination, monitor audio sessions, support manual process selection, and retain system-loopback fallback.

### Accessibility observer overhead

Risk: UI Automation queries may be slow, incomplete, or destabilizing for large application trees.

Response: Prefer events, take bounded snapshots, enforce timeouts, rate-limit work, and record omissions.

### Missing internal screen-reader state

Risk: Raw evidence cannot expose private virtual-buffer state or exact command dispatch.

Response: Treat behavior as inference, combine independent channels, preserve alternatives, and do not claim completeness.

### Touch and touchpad coverage

Risk: Windows does not provide one simple global stream equivalent to keyboard and mouse Raw Input for all gestures.

Response: Run a dedicated hardware investigation and let the findings determine whether browser or device-specific capture is required.

### Observer effect

Risk: Media encoding, UI Automation traversal, input capture, and disk writes may change the tested experience.

Response: Use bounded queues, background encoding, buffered writes, process priority controls, performance telemetry, and comparison sessions with recording disabled.

## Prototype success criteria

The platform prototype is successful when:

- One application installs and runs without assistive-technology or browser plugins.
- A participant can start, pause, annotate, and stop recording accessibly.
- Keyboard, mouse, video, microphone, system audio, process-specific audio, window state, and UI Automation events share a usable timeline.
- NVDA, JAWS, and Narrator sessions can be recorded without changing those products.
- Screen-reader speech can be reviewed separately from participant commentary in supported configurations.
- The viewer exposes evidence and omissions clearly.
- Raw evidence remains immutable.
- Inferences are labelled and linked to evidence.
- A real-session gap report identifies the next browser instrumentation requirements.

## Immediate next actions

1. Confirm the Windows and screen-reader version matrix for the prototype.
2. Define the event envelope and session manifest.
3. Build short technical spikes for Raw Input, Windows Graphics Capture, WASAPI process loopback, and UI Automation events.
4. Record one manually operated session for each of NVDA, JAWS, and Narrator.
5. Compare channel coverage and timing.
6. Finalize the archive schema from the observed data.
7. Build the core recorder and timeline viewer.
8. Run the shared validation scenarios.
9. Produce the browser gap report.
10. Select the smallest browser integration that addresses the demonstrated gaps.

## Review decisions

The following decisions should be confirmed before implementation:

- Minimum supported Windows release.
- Initial screen-reader and synthesizer version matrix.
- Full-display versus selected-window default.
- Whether raw input is system-wide or restricted to selected target processes.
- Required session duration.
- Acceptable CPU, memory, and disk overhead.
- Audio formats and retention requirements.
- Encryption and archive-key handling.
- Password and sensitive-field suppression rules.
- Whether speech transcription runs locally.
- Required support for braille-only and speech-disabled sessions.
- Touch and precision-touchpad hardware available for testing.

This plan deliberately defers the browser architecture decision. The first implementation should establish a trustworthy, screen-reader-independent Windows evidence record. Real recordings will then show which browser-level signals are necessary and whether they justify an extension, a native bridge, or a Chromium modification.
