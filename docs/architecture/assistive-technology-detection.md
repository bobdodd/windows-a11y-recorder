# Detecting the Assistive Technology in a Recording

## Status

Proposed 2026-10-07, approach agreed 2026-10-07, not built. This is the first design of
the analysis and inference work that follows release 1.0.0. It records what
Windows and Chromium make observable about the assistive technology running
during a recording, what each signal establishes, what cannot be observed,
and a proposal for recording it. Nothing here has been run on the target
machine yet except the inventory in "The target machine".

## Purpose

The prototype plan, under "Screen-reader independence", allows the
recorder to detect screen-reader process names, product names and
versions, relevant process trees, audio sessions associated with those
processes, and publicly readable configuration information, and states
that "It must not inject code into the screen reader or modify its
behavior." It also excludes administrator privileges for ordinary
recording, and its exit criteria include "The archive identifies which
screen reader and version were active."
([prototype plan](../prototype-plan.md)). This design extends that to every
kind of assistive technology: screen readers, magnifiers, on-screen
keyboards, speech recognition and voice control, switch and scanning
software, eye and head tracking, braille displays, and the Windows
accessibility settings in effect.

Two questions are kept apart throughout:

- Running: whether an assistive technology was present during the
  recording, and which product and version.
- In use: whether it acted during the recording, such as speaking, typing,
  or magnifying.

The recorder records evidence for both. The inference of "in use" belongs
to the analysis layer, with its basis stated, as the project's evidence
provenance model requires.

## Signals

Each signal is listed with what it establishes, its source, and its limits.

### Running processes matched to registered assistive technology

- An assistive technology that registers with Windows creates a key under
  `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Accessibility\ATs\`.
  Its `ATExe` value names the executable, which Windows uses "to determine
  whether the accessibility application is running", and its
  `Simple Profile` value classifies it "in a word or two: Screen reader,
  Magnifier, or On-screen keyboard"
  ([Microsoft, assistive technology registration](https://learn.microsoft.com/en-us/windows/win32/winauto/ease-of-access---assistive-technology-registration)).
- The same page describes
  `HKCU\Software\Microsoft\Windows NT\CurrentVersion\Accessibility\Configuration`
  as a comma-delimited list of registered keys that Windows starts at
  sign-in and on the logon desktop. It is a start-up setting, not a list of
  running applications.
- Registration is optional and needs an elevated installer, so a portable
  copy is not registered (see "The target machine"). A list of known
  executables is therefore kept beside the registered ones, such as
  `natspeak.exe` for Dragon
  ([Nuance](https://isupportcontent.nuance.com/healthcare/documents/sales/opmanual/3800/L-3899_DMNE_2.6_CitrixAdminGuide.pdf))
  and `VoiceAccess.exe` for Voice Access
  ([Winaero](https://winaero.com/how-to-enable-voice-access-in-windows-11/)).
- Each process's executable path and file version give the product and
  version. A process's start and exit times bound when it ran.
- Establishes: running, with product, version, and process identity. A
  renamed or unknown executable is missed by the known list.

### Modules an assistive technology loads into other processes

- NVDA's `nvdaHelperRemote.dll` "injects itself into other processes on the
  system, allowing for in-process code execution by NVDA"
  ([NVDA, nvdaHelper](https://github.com/nvaccess/nvda/blob/master/nvdaHelper/readme.md)).
- Chromium names the assistive technology running from the modules loaded
  in its own process. In the target checkout (Chromium 156.0.8065.0),
  `DiscoverAssistiveTech` in
  `content/browser/accessibility/browser_accessibility_state_impl_win.cc`,
  lines 379 to 421, matches `fsdomsrv.dll` to JAWS,
  `nvdahelperremote.dll` or `vbufbackend_gecko_ia2.dll` to NVDA,
  `dolwinhk.dll` to SuperNova, `outhelper.dll` or `outhelper_x64.dll` to
  ZDSR, `zslhook.dll` or `zslhook64.dll` to ZoomText, and
  `uiautomation.dll` or `uiautomationcore.dll` to a UI Automation client,
  and reads each module's file version.
- The recorder can list the modules of another process it may query, such
  as the instrumented Chromium it starts, without loading anything into
  it. That is reading, not injection, and stays within the plan.
- Establishes: that the assistive technology had reached that process,
  which is stronger than a running process. A module that stays loaded
  after the assistive technology exits would be a false positive; whether
  that happens is to be tested.

### Running-state values of the Windows assistive technology

- In the same Chromium file, lines 357 to 377 read the `RunningState` value
  under `HKCU\Software\Microsoft\Narrator\NoRoam` and under
  `HKCU\Software\Microsoft\ScreenMagnifier`, because, as its comment says,
  Narrator "is not injected in process so it needs to be detected in a
  different way".
- Establishes: Narrator or Magnifier running, as Windows records it. The
  process signal confirms it.

### The screen reader flag

- `SPI_GETSCREENREADER` reports a flag that is "typically set by
  accessibility aids such as screen readers", but "Narrator, the screen
  reader that is included with Windows, does not set" it
  ([Microsoft, screen reader parameter](https://learn.microsoft.com/en-us/windows/win32/winauto/screen-reader-parameter)).
  Chromium's notes say that relying on it gives false positives
  ([Chromium, browser_accessibility_state_impl.h](https://chromium.googlesource.com/chromium/src/+/refs/tags/133.0.6943.9/content/browser/accessibility/browser_accessibility_state_impl.h)).
- Establishes: only that some program set the flag. It is recorded as a
  clue and never as the basis of a product.

### UI Automation clients

- On Windows build 26100 and later, `IUIAutomationClientInfoSource` lists
  the connected UI Automation clients, and `IUIAutomationClientInfo` gives
  each client's process ID and process name
  ([Microsoft, IUIAutomationClientInfo](https://learn.microsoft.com/en-us/windows/win32/api/uiautomationcore/nn-uiautomationcore-iuiautomationclientinfo)).
  A connection callback reports clients connecting and disconnecting. The
  target checkout uses it in `ui/accessibility/platform/uia_client_info_source_win.h`,
  whose comment, lines 25 to 27, states that the API "is available on
  Windows build 26100 (24H2) and later".
- `UiaClientsAreListening` only says whether any client has subscribed to
  UI Automation events, without naming it
  ([Microsoft, server-side providers](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-serversideprovider)),
  and the recorder is itself a client, so it says nothing here.
- Establishes, where available: which processes read UI Automation, and
  when. The target machine is build 19045, so it is not available there.

### The UIAccess flag

- `TokenUIAccess` returns a value "nonzero if the token has the UIAccess
  flag set"
  ([Microsoft, TOKEN_INFORMATION_CLASS](https://learn.microsoft.com/en-us/windows/win32/api/winnt/ne-winnt-token_information_class)).
  Assistive technology that drives other applications' interfaces commonly
  runs with it.
- Establishes: a candidate for an assistive technology not on any list.
  Whether the recorder can read the token of another process without
  elevation is to be tested; the plan excludes administrator privileges
  for ordinary recording, so a token it cannot read is recorded as
  unreadable, not as absent.

### Magnification

- `MagGetFullscreenTransform` returns the full-screen magnification
  factor, where 1.0 means no magnification, and the offsets of the
  magnified view
  ([Microsoft](https://learn.microsoft.com/en-us/windows/win32/api/magnification/nf-magnification-maggetfullscreentransform)).
  The page does not say whether a process reads the transform set by
  another process, and Magnifier's lens and docked views are not the
  full-screen magnifier.
- ZoomText and SuperNova are found by their modules, as above.
- Establishes: the zoom level and view over time, which the analysis
  needs to relate the screen capture to what the participant saw.
- Tested 2026-10-07 with Windows Magnifier
  ([Magnifier capture validation](../validation/magnifier-capture-2026-10-07.md)):
  read from another process, the transform reported level 2 and its
  offset in the full screen view and level 1 in the lens and docked
  views. The recorder's desktop frames show the full screen view
  unmagnified and the lens and docked views as seen, so without the
  transform a recording does not show what a participant using full
  screen magnification saw. A third-party magnifier is not tested.

### Audio by process

- Each audio session gives "the process identifier of the audio session"
  ([Microsoft, IAudioSessionControl2::GetProcessId](https://learn.microsoft.com/en-us/windows/win32/api/audiopolicy/nf-audiopolicy-iaudiosessioncontrol2-getprocessid)).
- Establishes: that a screen reader or other speech output was producing
  audio, and when, which is evidence of use. The plan's process loopback
  capture of the screen reader's audio builds on the same identity.

### Windows accessibility settings

- Revised 2026-10-07: these settings, with the other accessibility
  preferences of Windows and the browser, are recorded at the start and on
  each change as designed in
  [accessibility preferences](accessibility-preferences.md); this section
  states only why they matter here.
- The registered `ATs` list also names Windows settings, such as sticky
  keys, filter keys, mouse keys, toggle keys, high contrast, caret width,
  and cursor scheme, with `Simple Profile` `SystemSetting` (see "The target
  machine").
- High contrast is read with `SPI_GETHIGHCONTRAST`
  ([Microsoft](https://learn.microsoft.com/de-de/previous-versions/windows/embedded/ms858566(v=msdn.10))),
  and the keyboard settings with their `SystemParametersInfo` actions, as
  Chromium reads sticky keys (`browser_accessibility_state_impl_win.cc`,
  lines 351 to 355). Color filters are under
  `HKCU\Software\Microsoft\ColorFiltering`
  ([Winaero](https://winaero.com/how-to-enable-color-filters-in-windows-11/)),
  and text size under `HKCU\Software\Microsoft\Accessibility`
  ([ElevenForum](https://www.elevenforum.com/t/change-text-size-in-windows-11.933/)).
- Establishes: the settings in effect, which change what the participant
  saw and how input behaved.

### Input devices and injected input

- The recorder records each raw input event's device handle
  (`src/Recorder.Collectors.Input/RawInputCollector.cs`, line 313) but not
  what the device is. `GetRawInputDeviceInfo` gives the device interface
  name and device information for a handle
  ([Microsoft](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getrawinputdeviceinfoa)),
  from which the vendor, product, and HID usage are read.
- The HID usage tables define a Braille Display page (0x41) and an Eye and
  Head Trackers page (0x12)
  ([USB-IF, HID Usage Tables 1.5](https://www.usb.org/sites/default/files/hut1_5.pdf)),
  so such devices are identified by their type when they use those pages.
- Keyboard-emulating switch interfaces "send keypresses directly to the
  computer", while some software, such as Clicker and The Grid, reads
  switch interfaces itself
  ([BLTT](https://www.bltt.org/switch/drivers.htm)). A head pointer such
  as the HeadMouse Nano "uses standard mouse drivers"
  ([Orin](https://www.orin.com/)). Such devices are an ordinary keyboard or
  mouse to Windows, and only their vendor and product name them.
- Input synthesized by software is marked: a low-level keyboard hook's
  `LLKHF_INJECTED` bit marks an event injected by any process, and
  `LLKHF_LOWER_IL_INJECTED` one injected by a process at lower integrity
  ([Microsoft, KBDLLHOOKSTRUCT](https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-kbdllhookstruct)).
  Raw input is reported to give injected input a null device handle
  ([Stack Overflow](https://stackoverflow.com/questions/37411535/detect-if-keyboard-or-mouse-events-are-triggered-by-a-software));
  this is to be confirmed on the target machine. Neither names the process
  that injected the input.
- Establishes: which device each input came from, and that input was
  synthesized. Joining synthesized input to an on-screen keyboard, voice
  control, scanning, or eye gaze software is an inference from timing and
  the running processes.

### The instrumented Chromium

- Chromium's own detection, above, runs in the recorded browser. Its
  results and its accessibility mode changes are not recorded today.
  Recording them over the bridge would give the browser's view of the
  assistive technology reaching it, at a protocol change.
- The recorder's UI Automation collector is itself a client, and Chromium
  counts a loaded `uiautomationcore.dll` as a UI Automation client (line
  417). The browser's accessibility mode in a recording may therefore be
  turned on by the recorder, and a recording states whether the UI
  Automation collector was on.

## What cannot be observed

- Assistive technology that is not on the computer: a hardware video
  magnifier, a screen reader on a phone, or physical aids such as a key
  guard.
- A keyboard-emulating switch interface or a head pointer whose vendor and
  product are not known: it is an ordinary keyboard or mouse.
- Which process injected a given input, and the internal state of a screen
  reader, such as its virtual buffer or review cursor, which the plan
  already places outside capture.
- A product on no list, without the UIAccess flag, and loading no known
  module.

## Proposed recording

A new collector, on a channel of its own, `system.assistive-technology`,
proposed with these records:

- `assistive-technology-inventory`, at the start and stop of a recording:
  the registered `ATs` entries with their `ATExe` and `Simple Profile`, the
  known list's version, the Windows build, and which of the signals below
  are available on this machine.
- `assistive-technology-process-started` and
  `assistive-technology-process-exited`: a process whose executable is
  registered or known, or that has the UIAccess flag, with its path, file
  version, product name, process ID, parent process ID, start time, and
  the basis of the match (`registered`, `known`, or `ui-access`). How
  processes are watched, by polling or by a system notification, is to be
  settled.
- `assistive-technology-module-seen`: a known module found in a watched
  process (the instrumented Chromium, and the foreground process), with
  the module's path and file version, and the process.
- `assistive-technology-setting`: the Windows accessibility settings at
  the start, and each change, with the running-state values and the
  screen reader flag.
- `input-device`: each raw input device at the start and on arrival or
  removal, with its handle, interface name, vendor, product, and HID usage
  page and usage.
- `audio-session`: each audio session's process ID, start, and end.
- On build 26100 and later, `uia-client-connected` and
  `uia-client-disconnected`, with process ID and name.

The analysis layer then states, for each assistive technology, when it was
running and on what basis, and when it was in use and on what evidence
(speech audio, injected input, UI Automation traffic, magnification
changes), each as an inference with its basis.

## Questions to settle

- Whether a low-level keyboard and mouse hook is added for the injected
  flags, given its cost on every input event, or the null device handle of
  raw input is enough once confirmed.
- How often modules of watched processes are listed, and the cost.
- Whether Chromium's detection and accessibility mode are recorded over
  the bridge, at a protocol change.
- How the full screen transform is recorded over time, and whether the
  player shows the magnified part of each frame; Windows Magnifier's
  transform is readable from another process (tested 2026-10-07), and the
  lens and docked views are already in the desktop frames.
- The known list's contents, format, and where it is kept and versioned.

## Required tests

- Unit tests of the payload contracts and validator for each record; of
  matching processes to registered and known entries; of reading device
  information; and of the analysis layer's running and in-use inferences
  from generated sequences, including an assistive technology started and
  stopped during a recording.
- Integration tests of the collector against the Windows APIs on the
  target machine, including the cost of each signal.
- A system test on the target machine: a recording in which NVDA, Narrator,
  Magnifier, and the on-screen keyboard are each started and stopped,
  showing each identified with its version and its running time, NVDA's
  module found in the instrumented Chromium, its audio session joined to
  it, and on-screen keyboard input found as injected; the same recording
  with the UI Automation collector off, to separate the recorder's effect
  on the browser's accessibility mode.

## The target machine

Read on 2026-10-07:

- Windows 10 Pro 22H2, build 19045.6466. The UI Automation client
  information interface is not available.
- 29 registered `ATs` entries, all part of Windows. Five name an
  executable: `magnifierpane` (`Magnify.exe`, `Magnifier`), `Narrator`
  (`Narrator.exe`, `screenreader`), `osk` (`osk.exe`, `osk`),
  `SpeechReco` (`sapisvr.exe`, `speech`), and `CursorIndicator`
  (`EoaExperiences.exe`, `cursorindicator`). The rest are settings, such
  as `stickykeys`, `filterkeys`, `highcontrast`, and `colorfiltering`.
  The `Configuration` value is empty.
- NVDA 2026.2 is a portable copy in `C:\Users\User\Desktop\NVDA`, with the
  installer in `Downloads`. It is not installed, so it is neither in the
  `ATs` list nor among installed programs; it is found by its executable,
  `nvda.exe`, as a known entry. The owner starts and stops it as needed.
- With NVDA not running, `SPI_GETSCREENREADER` was 0, and neither
  `Narrator\NoRoam` nor `ScreenMagnifier` existed under `HKCU`.
