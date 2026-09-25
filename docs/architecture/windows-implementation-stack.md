# Recommended Windows Implementation Stack

## Decision status

- **Status:** Adopted for prototype
- **Related issue:** [Define prototype architecture and collector contracts](https://github.com/bobdodd/windows-a11y-recorder/issues/1)
- **Scope:** Windows 10 and Windows 11 x64 prototype
- **Browser decision:** A bundled instrumented Chromium build is required

Related specifications:

- [Prototype architecture](prototype-architecture.md)
- [Collector contracts](collector-contracts.md)
- [Reference Windows test platform](reference-platform.md)
- [Threat model](../security/threat-model.md)
- [Privacy and data-handling policy](../security/privacy-and-data-handling-policy.md)

## Recommendation

Build the prototype as a Windows x64 application using:

- C# on .NET 10 LTS.
- WPF for the session controller and timeline viewer.
- A separate, bundled capture-host process written in C#.
- A bundled Chromium build with narrow C++ instrumentation hooks and a private local evidence bridge.
- Microsoft.Windows.CsWin32 for most Win32 interop.
- C#/WinRT where Windows Runtime APIs are required.
- Windows Graphics Capture and Direct3D 11 for display and window frames.
- Media Foundation for video encoding and media container writing.
- WASAPI for microphone, system-loopback, and process-loopback audio.
- Windows UI Automation COM APIs for accessibility events and bounded snapshots.
- Raw Input as the primary keyboard and mouse source.
- Chromium source instrumentation for browser listener registration, event dispatch, default actions, timers, cookies, DOM, accessibility, network, and rendering evidence.
- Versioned NDJSON and media files in an append-only session directory.

This is one product and one installation, even though it contains internal worker and browser processes. No screen-reader add-on, browser extension, Python environment, external driver, or separately installed runtime is required.

## Why this stack

### .NET 10 LTS and C#

.NET 10 is the current long-term support release and is supported through November 2028 ([Microsoft .NET support policy](https://dotnet.microsoft.com/en-us/platform/support/policy)). Its self-contained deployment mode includes the selected .NET runtime so the target computer does not need a separately installed runtime ([Microsoft .NET deployment documentation](https://learn.microsoft.com/en-us/dotnet/core/deploying/)).

C# provides a practical balance for this prototype:

- Fast iteration on schemas, lifecycle logic, diagnostics, and the review interface.
- Strong asynchronous and concurrency primitives.
- Direct access to Win32, COM, Windows Runtime, and Direct3D APIs through interop.
- A mature Windows desktop UI framework with established keyboard and UI Automation behavior.
- A straightforward route to self-contained x64 deployment.

The application should target .NET 10 for Windows and publish initially for `win-x64`. The project must declare a Windows 10-compatible target platform version and guard newer APIs with runtime capability checks.

### WPF for the user interface

WPF is recommended over WinUI 3 for the prototype. Standard WPF controls expose most required accessibility information through automation peers, while custom controls can provide their own peers and UI Automation events ([Microsoft WPF accessibility guidance](https://learn.microsoft.com/en-us/dotnet/framework/ui-automation/accessibility-best-practices)). WPF also provides explicit keyboard-focus behavior suitable for a recorder that must be fully operable without a pointer ([Microsoft WPF focus overview](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/focus-overview)).

WPF is the lower-risk choice because:

- The recorder needs a utilitarian, highly accessible desktop interface rather than the newest visual controls.
- WPF works directly with HWND-based input, window, and message-loop integration.
- It avoids making Windows App SDK deployment another prototype variable.
- It supports custom timeline rendering while allowing the accessible representation to use ordinary controls.

The interface should prefer standard controls, logical tab order, access keys, visible focus, and a text-based event list. Any custom visualization must have an equivalent structured representation. Custom controls must raise the appropriate UI Automation events because Windows screen readers use those events to track focus and state ([Microsoft custom-control focus guidance](https://learn.microsoft.com/en-us/accessibility-tools-docs/items/wpf/customcontrol_focusevent)).

Any task the user must wait for, and that takes more than a fraction of a second, must say so in three ways: the wait cursor, a visible working indicator with text, and a UI Automation notification event that a screen reader can speak when the task starts and when it ends. Cursor shape is a visual cue only, so the notification is the signal a screen-reader user receives. WPF raises notification events through `AutomationPeer.RaiseNotificationEvent` ([Microsoft AutomationPeer.RaiseNotificationEvent reference](https://learn.microsoft.com/en-us/dotnet/api/system.windows.automation.peers.automationpeer.raisenotificationevent)), and the underlying UI Automation notification event requires Windows 10 version 1709 or later ([Microsoft UiaRaiseNotificationEvent reference](https://learn.microsoft.com/en-us/windows/win32/api/uiautomationcoreapi/nf-uiautomationcoreapi-uiaraisenotificationevent)). The indicator animates only when Windows animations are turned on. The recorder currently applies this to starting a recording, stopping and verifying a recording, and loading or validating a recording for playback. Work that blocks the UI thread cannot answer screen-reader queries while it runs, so slow synchronous work, such as a slow timeline redraw, must be made fast or moved off the UI thread rather than only marked busy.

### Bundled capture-host process

Use two executable processes from the start:

```text
Recorder.App.exe
  WPF session controller
  consent and privacy controls
  recording status
  timeline viewer
  worker supervision

Recorder.CaptureHost.exe
  session clock and sequencer
  collector lifecycle
  raw input message window
  UI Automation observer
  video and audio capture
  archive writer
  health and omission records
```

The capture host is an implementation detail launched and supervised by the application. It must not require a second installation or manual launch.

This boundary provides:

- Isolation between the accessible UI and high-risk capture APIs.
- A clear place to enforce collector lifecycle contracts.
- A recoverable session directory if the UI exits unexpectedly.
- A way for the UI to remain responsive when a cross-process accessibility query stalls.
- A future route to restart a failed capture host without redesigning every collector.

Use a versioned named-pipe protocol for commands, status, and low-volume preview data. High-volume evidence and media should be written directly by the capture host rather than forwarded through the UI process.

### Instrumented Chromium

The application distribution includes a project-maintained Chromium build for browser testing. The browser remains a normal interactive browser controlled by the participant, not by Selenium, WebDriver, or an external automation process.

Instrumentation hooks must be placed at the browser implementation points that own the relevant behavior. The initial hook groups are:

- Blink event-target listener registration and removal.
- Blink event dispatch, propagation, cancellation, and default handling.
- DOM timers, animation-frame callbacks, task scheduling, lifecycle throttling, and cancellation.
- DOM, style, layout, frame, and accessibility-tree checkpoint production.
- Cookie API, cookie store, network-cookie, and blocked-cookie operations with values removed.
- Navigation, frame, renderer, network, compositor, and browser-process correlation.

The browser evidence bridge sends versioned records over authenticated local IPC. Every browser process receives a per-session connection capability from the recorder-launched browser parent. The bridge must apply bounded queues, report omissions, remove cookie and authorization values before records cross the process boundary, and preserve Chromium monotonic timestamps for mapping to the recorder session clock.

CDP can supplement these records for stable snapshot and diagnostic operations. It is not the authoritative source for the complete listener registry, dispatch path, browser default actions, or task scheduling.

## Interop policy

### Default to managed code

Implement orchestration, schemas, archive writing, diagnostics, lifecycle management, the UI, and most Windows API calls in C#.

Use Microsoft.Windows.CsWin32 to generate type-safe P/Invoke bindings for Win32 APIs. Microsoft recommends this source generator for calling Win32 APIs from C#, and it can be used from WPF, WinUI, console applications, and class libraries ([Microsoft Win32 interop guidance](https://learn.microsoft.com/en-us/windows/apps/develop/interop/call-win32-apis)).

Use C#/WinRT for Windows Runtime APIs, including Windows Graphics Capture. Keep all platform-specific calls behind collector interfaces so the event model does not expose API-specific structures.

### Add native C++ only after a failed spike

Do not begin with a mixed C# and C++ architecture. A small native C++20 library or worker is justified only if a technical spike demonstrates one of these problems:

- Unacceptable frame-copy or encoding overhead.
- Unsafe or unstable Direct3D ownership across the managed boundary.
- Unreliable Media Foundation callback behavior.
- Process-loopback activation that cannot be made dependable in managed code.
- A required Windows API surface that has no maintainable managed projection.

If native code becomes necessary, expose a narrow C ABI or process protocol. Do not let native Windows types leak into collector contracts or archive schemas.

## Collector architecture

### Contract

Every collector should implement the same lifecycle:

```text
Initialize(context)
Start()
Pause(reason)
Resume()
Stop()
Dispose()
```

Each collector must:

- Declare its capabilities and required permissions before recording.
- Emit immutable evidence records.
- Map native timestamps to the shared session clock.
- Report startup, running, degraded, failed, and stopped states.
- Use bounded queues with an explicit overflow policy.
- Report dropped, delayed, or suppressed evidence.
- Stop independently without invalidating other channels.
- Flush a final health record before shutdown when possible.

The controller should never infer user behavior. It coordinates evidence capture and records omissions.

### Concurrency

Use dedicated execution contexts for collectors that have message-loop or COM requirements:

- WPF UI on the application STA thread.
- Raw Input on a capture-host thread with a hidden message-only window.
- UI Automation subscriptions and calls on dedicated worker threads, separate from input and UI.
- Windows Graphics Capture and Direct3D work on their required dispatcher or worker context.
- Audio callbacks that copy data quickly into bounded queues.
- One archive-writing pipeline per event stream or a small set of partitioned writers.

Use bounded `System.Threading.Channels` between producers and writers. Bounded channels provide configurable behavior when capacity is reached, which makes overflow a deliberate policy rather than an accidental memory leak ([Microsoft Channels documentation](https://learn.microsoft.com/en-us/dotnet/core/extensions/channels)).

No capture callback should perform serialization, tree traversal, media encoding, or disk I/O directly.

### Session clock

Use QueryPerformanceCounter as the authoritative monotonic session clock. It provides a high-resolution timestamp suitable for interval measurement ([Microsoft QueryPerformanceCounter documentation](https://learn.microsoft.com/en-us/windows/win32/api/profileapi/nf-profileapi-queryperformancecounter)).

At session start, record:

- QueryPerformanceCounter value.
- QueryPerformanceFrequency value.
- UTC wall-clock timestamp.
- Process and operating-system timing metadata.

Each record should retain both the source API timestamp and its mapped session timestamp. Media pipelines must expose enough timing information to measure mapping error and drift. Wall-clock time is for external correlation, not event ordering.

## Windows API mapping

| Channel | Primary API | Implementation note |
| --- | --- | --- |
| Keyboard | Raw Input | Register explicitly and receive records through a message-only window. |
| Mouse | Raw Input plus cursor and window APIs | Preserve device-relative movement and separately sample screen position and target window. |
| Optional hook evidence | `WH_KEYBOARD_LL` and `WH_MOUSE_LL` | Secondary diagnostic channel only. Callback enqueues and returns immediately. |
| Touch, pen, and touchpad research | Pointer messages and Raw HID | Treat coverage as experimental until tested on representative hardware. |
| Foreground context | User32 window events and process APIs | Record foreground window, bounds, process identity, and transitions. |
| Display or window frames | Windows Graphics Capture and Direct3D 11 | Capture frames without browser control or external automation. |
| Video encoding | Media Foundation | Encode H.264 in MP4 for broad review compatibility. |
| Microphone | WASAPI capture | Keep as a separate synchronized track. |
| System playback | WASAPI loopback | Retain as fallback when process isolation is incomplete. |
| Screen-reader playback | WASAPI process loopback | Capture the selected process tree and record the exact inclusion mode. |
| Accessibility state | UI Automation COM APIs | Subscribe to events and take bounded, cancellable snapshots. |

Windows Graphics Capture supplies Direct3D frames for a selected display or application window ([Microsoft screen-capture documentation](https://learn.microsoft.com/en-us/windows/apps/develop/media-authoring-processing/screen-capture)). Media Foundation provides capture, encoding, and file-authoring components suitable for the media pipeline ([Microsoft Media Foundation programming guide](https://learn.microsoft.com/en-us/windows/win32/medfound/media-foundation-programming-guide)).

Windows process-loopback capture can include or exclude the audio of a selected process tree ([Microsoft application-loopback sample](https://learn.microsoft.com/en-us/samples/microsoft/windows-classic-samples/applicationloopbackaudio-sample/), [Microsoft process-loopback parameters](https://learn.microsoft.com/en-us/windows/win32/api/audioclientactivationparams/ns-audioclientactivationparams-audioclient_process_loopback_params)). The implementation must still validate actual coverage for NVDA, JAWS, Narrator, and their synthesizer arrangements.

UI Automation calls that target desktop elements can be slow or unresponsive. Microsoft recommends making such calls from a separate non-UI thread, and handler registration and removal should occur on the same thread ([Microsoft UI Automation threading guidance](https://learn.microsoft.com/en-us/dotnet/framework/ui-automation/ui-automation-threading-issues)). This supports isolating accessibility observation from both the WPF UI thread and latency-sensitive input collection.

## Windows 10 compatibility

The minimum product release is Windows 10 x64. Windows 10 version 22H2, build 19045, is the general compatibility baseline. Earlier Windows 10 builds are not part of the prototype acceptance matrix.

This decision requires two explicit compatibility policies:

- Microsoft documents process-specific WASAPI loopback as requiring build 20348 or later ([Microsoft process-loopback requirements](https://learn.microsoft.com/en-us/windows/win32/api/audioclientactivationparams/ns-audioclientactivationparams-audioclient_process_loopback_params)). Windows 10 22H2 build 19045 therefore uses consented whole-system loopback as the screen-reader-audio fallback and records process isolation as unavailable.
- Microsoft lists .NET 10 support for maintained Windows 10 Enterprise LTSC variants, but not ordinary Windows 10 22H2 Home or Pro after their operating-system support ended ([official .NET 10 supported operating systems](https://github.com/dotnet/core/blob/main/release-notes/10.0/supported-os.md)). The application may still be tested and product-supported on Windows 10 22H2, but that support is owned by this project rather than inherited from Microsoft's .NET support policy.

The technical spikes must determine whether .NET 10 self-contained builds operate reliably on the required Windows 10 editions. If they do not, the runtime decision must be reopened before collector implementation. The application must not report process-specific audio as supported merely because whole-system loopback works.

## Solution structure

```text
src/
  Recorder.App/
  Recorder.CaptureHost/
  Recorder.Contracts/
  Recorder.Session/
  Recorder.Windows/
  Recorder.Collectors.Input/
  Recorder.Collectors.Windowing/
  Recorder.Collectors.Accessibility/
  Recorder.Collectors.Video/
  Recorder.Collectors.Audio/
  Recorder.Viewer/
tests/
  Recorder.Contracts.Tests/
  Recorder.Session.Tests/
  Recorder.Collectors.IntegrationTests/
  Recorder.Archive.Tests/
spikes/
  raw-input/
  windows-graphics-capture/
  process-loopback-audio/
  ui-automation-events/
```

`Recorder.Contracts` must not depend on WPF, Win32 interop packages, media packages, or collector implementations. It should contain the event envelope, collector states, health records, capability declarations, and control protocol.

`Recorder.Session` should own the session clock abstraction, sequence allocation, archive paths, append-only writers, checkpointing, and recovery.

`Recorder.Windows` should centralize generated Windows bindings and safe wrappers. Collector projects should depend on these wrappers rather than defining unrelated P/Invoke declarations.

## Archive and storage

Keep the prototype storage model simple:

- A directory is the active recording format.
- NDJSON is used for append-only structured streams.
- Media remains in ordinary media files.
- Each collector writes recoverable segments or checkpoints.
- A session is packaged into a ZIP-compatible archive only after validation.
- Raw streams are never rewritten by analysis.
- Derived and inferred streams are stored under separate paths.

Do not introduce a database in the first prototype. A viewer-side index may be generated from an archive and discarded or rebuilt. This keeps the raw format inspectable, streamable, and independent of a database engine.

## Deployment

Publish a self-contained `win-x64` build so test machines do not need a preinstalled .NET runtime ([Microsoft .NET deployment documentation](https://learn.microsoft.com/en-us/dotnet/core/deploying/)).

For the prototype:

- Ship one signed installer or signed ZIP distribution containing both executables and their dependencies.
- Present only `Recorder.App.exe` to the user.
- Keep the capture host in an internal application directory.
- Do not require administrator privileges for normal recording.
- Store archives in a user-selected location.
- Produce diagnostics that identify the exact application, runtime, schema, and collector versions.

Do not make .NET single-file publishing a first-release requirement. Single-file output is operating-system and architecture specific, and native components can require extraction or additional packaging rules ([Microsoft single-file deployment documentation](https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview)). A single installer is the user requirement; a single physical executable is not.

Self-contained deployments do not automatically acquire newer .NET runtime security fixes. Each application release must therefore be rebuilt and redistributed with an updated supported runtime when required ([Microsoft .NET deployment documentation](https://learn.microsoft.com/en-us/dotnet/core/deploying/)).

## Alternatives considered

| Option | Strengths | Prototype concerns | Decision |
| --- | --- | --- | --- |
| C#/.NET 10 with WPF | Mature desktop accessibility, rapid implementation, direct Windows interop, self-contained deployment | Some Direct3D and Media Foundation interop complexity | Recommended |
| C#/.NET with WinUI 3 | Current Windows visual framework and Windows App SDK integration | More deployment variables and no material benefit for this utility prototype | Defer |
| C++ with Win32 | Maximum native control and minimum interop | Slower development, greater memory-safety burden, and more work to build an accessible UI | Use only for proven native hotspots |
| Rust with a Windows UI layer | Strong memory safety and systems performance | Higher risk around Windows accessibility, COM, media, and UI integration for this prototype | Not recommended initially |
| Electron or another browser shell | Fast web-interface development | Large runtime footprint and substantial native bridges for every important capture channel | Not recommended |
| Instrumented Chromium | Complete browser-internal evidence and authoritative event, default-action, and timer correlation | Large maintenance and distribution burden; does not replace whole-system capture | Required alongside the Windows recorder |

WinUI 3 can be deployed self-contained, but unpackaged and single-file configurations have additional Windows App SDK rules and caveats ([Microsoft Windows App SDK deployment guidance](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/self-contained-deploy/deploy-self-contained-apps), [Microsoft unpackaged WinUI guidance](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/unpackage-winui-app)). It should be reconsidered only if a later product requirement depends on a WinUI-specific capability.

## Initial technical spikes

Build four disposable but measured spikes before committing collector implementations:

### Raw Input

- Capture keyboard and mouse records in a headless process.
- Preserve device identity, scan codes, movement, buttons, wheel data, and foreground context.
- Confirm operation with NVDA, JAWS, and Narrator active.
- Measure event loss and callback latency under CPU and disk load.

### Windows Graphics Capture

- Capture a full display and one selected application window.
- Encode a 60-minute MP4 while recording dropped frames and timestamp mapping.
- Test display scaling, resolution change, cursor inclusion, and window closure.
- Measure CPU, GPU, memory, and disk overhead.

### Process-loopback audio

- Capture microphone, system loopback, and selected process tree as separate tracks.
- Test NVDA, JAWS, and Narrator with representative synthesizers.
- Detect silence, process restart, default-device change, and isolation failure.
- Confirm that fallback status is visible in the archive.

### UI Automation

- Subscribe to focus, property, structure, selection, text, notification, and active-text-position events.
- Take small bounded snapshots around selected events.
- Enforce cancellation, timeout, rate, and queue limits.
- Test slow and unresponsive providers without affecting input or media capture.

Each spike should produce evidence, measurements, and a short decision record. Code should be retained only if it meets the eventual collector contract.

## Validation gates

Adopt this stack after the spikes demonstrate:

- A 60-minute simultaneous capture with bounded memory.
- No unreported input, media, or accessibility-record loss.
- Measurable synchronization error and drift for every channel.
- Recoverable archives after stopping the UI and terminating the capture host.
- Independent collector failure reporting.
- Keyboard and screen-reader operation of start, pause, resume, annotate, stop, and archive review.
- No required add-on or configuration change in NVDA, JAWS, or Narrator.
- Acceptable observer effect compared with a session recorded without the tool.

If one media collector fails its spike because of managed interop rather than an API limitation, isolate that collector behind a native implementation without changing the contracts. Browser evidence is provided by the required instrumented Chromium component and correlated with the platform collectors rather than inferred from platform capture alone.

## Decisions still required

- Windows 11 comparison build for process-specific audio and newer API validation.
- Initial x64-only support versus simultaneous Arm64 packaging.
- Maximum acceptable CPU, GPU, memory, disk, and timing overhead.
- Full-display or selected-window default.
- Video resolution, frame rate, codec profile, and segment duration.
- Lossless or compressed audio format for archival tracks.
- Encryption and archive-key management.
- Process-exclusion defaults for system-wide raw keyboard capture.
- Exact password and sensitive-field suppression behavior.
- Recovery behavior after capture-host failure.

These decisions should be recorded before the four spikes become production collector implementations.
