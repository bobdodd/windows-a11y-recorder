# Screen Reader Activity

## Status

Proposed 2026-10-07, agreed 2026-10-07, not built. Listed in
[outstanding work](analysis-outstanding-work.md). The owner accepted a
low-level keyboard hook for recording on 2026-10-07. This design follows
[assistive technology detection](assistive-technology-detection.md), which
records which screen reader is running, and the
[screen reader keys validation](../validation/screen-reader-keys-2026-10-07.md),
which measured what reaches the recorder while NVDA runs.

On 2026-10-10 the owner set the goals for NVDA and the terms of the work,
recorded in "Decisions of 2026-10-10", and the first step, tracking NVDA,
was built; see "Tracking NVDA". Its first run on the target machine is in
[the NVDA tracking validation](../validation/nvda-tracking-2026-10-10.md).
The keyboard hook and the rest of this design are not built.

## The problem

A screen reader user moves through a page in several ways: Tab and the
arrow keys, single-letter commands for headings, landmarks, links, and
other elements, and lists of elements pulled from the page to choose
from. An auditor viewing a recording needs to see what the participant
did and what the screen reader did in response.

The validation established, with NVDA 2026.2 on the target machine:

- The keys NVDA keeps as commands (quick navigation, the browse mode
  arrows, NVDA+key) are missing from raw input, the recorder's and an
  independent capture alike. A low-level keyboard hook installed after
  NVDA's saw every one of them.
- NVDA kept each physical Tab and injected a Tab in its place. The
  injected key is in raw input with no device handle, and the page
  received it.
- Keys in NVDA's own windows, such as the elements list, reach raw input,
  and UI Automation records those windows.
- Browse mode navigation left no change of focus or selection in the page.

## What is recorded and what is inferred

Recorded at runtime, each with its basis:

1. Every key event, from a low-level keyboard hook (new).
2. Every key event that reached the input system, with its device, from
   raw input (built).
3. Every key event the page received, from the instrumented Chromium's
   dispatch records (built).
4. The screen reader's own windows, from UI Automation (built).
5. Which screen reader runs, its version, and its readable settings, from
   [assistive technology detection](assistive-technology-detection.md)
   (designed).
6. The system's sound, which includes the screen reader's speech (built).

Derived from those records, by a stated rule, at playback:

- Each key's disposition: kept by the screen reader, passed on, or
  injected by software ("Key disposition").

Inferred, each shown as an inference with its basis:

- Which command a kept key was, from the screen reader's product, version,
  keyboard layout, and key commands.
- Whether the screen reader was in browse or focus mode, from the keys it
  kept and passed.
- What the screen reader had to announce, from the recorded accessibility
  tree and the commands given (step 4 below), not from the audio: the
  owner ruled out speech recognition on 2026-10-10.
- Where the screen reader's reading position was (not designed here; see
  "Not in this design").

## Decisions of 2026-10-10

The owner's two goals for NVDA: "One, I want to know what it says. Two, I
want to know how the user is interacting with it (how they are using the
shortcut keys etc, heading navigation, landmark navigation, pulling out
lists of links etc). It is important for testing and for remediation so
that the developer understands how the issue was caused and can repeat
the process to test the remediation."

The terms, in the owner's words where quoted:

- No NVDA log: "I don't want to use the log. Work it out from the
  recording."
- No speech recognition. The microphone and the system sound are often in
  one channel, so what NVDA said is worked out from the recorded
  accessibility tree and the commands given: "I prefer to inspect the
  accessibility tree knowing the commands given."
- No add-ons or settings in NVDA: "Ideally, I don't really wand plugins or
  special settings within NVDA". This is also the prototype plan's rule
  that the recorder "must not inject code into the screen reader or modify
  its behavior" ([prototype plan](../prototype-plan.md)). The recorder
  reads; it changes nothing in NVDA.
- Track NVDA's processes, "to know when it is operating (potentially at
  least) operating".
- What the player shows from the tree and the commands is labelled what
  NVDA had to announce, not what NVDA said. The chain of inference breaks
  at a command the model does not handle, and the player shows where.

The order of work, as agreed:

1. Tracking NVDA: running, reached the browser, and making sound. Built;
   see "Tracking NVDA".
2. The keyboard hook and the commands, as designed below.
3. A test in the instrumented Chromium of the `PerformAction` and scroll
   requests NVDA makes during heading and landmark navigation (see "Not in
   this design").
4. The reading position, inferred from the accessibility tree and the
   commands, with the player's list of steps and a view of the tree.

Step 2 is built in parts, each tested on the target machine before the
next, as agreed on 2026-10-10:

- 2a, recording the keys: the recorder's own low-level keyboard hook, kept
  first in the chain, with its installations recorded. Built; see "The
  keyboard hook". Until then only the test script
  (`scripts/Test-ScreenReaderKeys.ps1`) had a hook, and the recorder had
  only raw input, which the keys NVDA keeps never reach.
- 2b, what happened to each key, at playback; see "Key disposition".
- 2c, NVDA's commands, the browse or focus mode band, and NVDA's windows;
  see "Settings and key commands".
- A low-level mouse hook, later in step 2, on the same terms as the
  keyboard hook: needed in case the user is using NVDA's speaking of the
  content under the pointer (the owner, 2026-10-10). Built 2026-10-11; see "The mouse hook".

The open questions of step 2, settled by the owner on 2026-10-10:

- Staying first in the chain: while a screen reader runs, the hook is
  installed again every second, the new hook before the old is removed;
  see "The keyboard hook".
- The command data: a data file in the repository for each NVDA version,
  with the source of each command. A version without its own file uses the
  nearest earlier version's, and the player says so.
- NVDA's settings: the keyboard layout, the NVDA modifier keys, and the
  custom key commands are read from NVDA's own configuration files, and
  nothing in them is changed.
- The join window stays 300 ms, to be checked on the target machine.
- The helpers recorded stay NVDA's children from its folder: in the three
  runs on the target machine they were NVDA's helper and nothing else.

## Tracking NVDA

Built 2026-10-10. First run on the target machine the same day
([validation](../validation/nvda-tracking-2026-10-10.md)): the processes,
the module, and the periods of sound matched, and two defects were fixed.
The assistive
technology collector (`src/Recorder.Collectors.AssistiveTechnology`) is
always on, as the Windows settings collector is, and records on the
`system.assistive-technology` channel
(`src/Recorder.Contracts/AssistiveTechnologyRecords.cs`). It needs no
administrator rights, loads nothing into NVDA or the browser, and changes
no setting. Each fact is recorded at one of three levels, each with its
basis.

### Running

From the process list (Toolhelp), every 250 ms:

- NVDA's main process is a process whose executable is `nvda.exe`.
- A helper is a child of a main process whose executable is in the main
  process's folder or below it, such as `nvda_slave.exe`. A child from
  elsewhere, such as a program NVDA was asked to start, is not recorded.
  This was the planned default for the open question of which helpers to
  record; it can be changed.
- Each process's start and exit times are Windows' own
  (`GetProcessTimes`), so the polling interval does not blur them, and a
  copy already running when the recording starts is recorded with its
  real start time and `runningAtStart`. A process is known by its ID and
  start time, so an ID Windows reuses is a new process.
- The record holds the executable's path, file and product versions (null
  for a file without a version resource), and
  the copy: installed when the `UninstallDirectory` value of
  `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\NVDA` names
  its folder, as NVDA's own `config.isInstalledCopy` decides it
  ([NVDA source, config](https://github.com/nvaccess/nvda/blob/master/source/config/__init__.py);
  [the key](https://github.com/nvaccess/nvda/blob/master/source/config/registry.py)),
  and otherwise portable. Both registry views are read.

Records: `assistive-technology-process-started` and
`assistive-technology-process-exited`.

### Reached the browser

From the module lists of the instrumented Chromium's processes (the
processes whose executable is the recording's `chrome.exe`), every second
while NVDA runs or its module is seen: `nvdahelperremote.dll`, the module
Chromium itself matches to NVDA in `DiscoverAssistiveTech`, seen in or gone
from each process. Reading a module list reads; it does not inject. A
process whose list cannot be read is passed over, so the absence of the
module there is not evidence.

Records: `assistive-technology-module-loaded` and
`assistive-technology-module-unloaded`, with `hostExited` when the
browser process ended with the module loaded.

### Making sound

From the peak meter of each of NVDA's processes' audio sessions on every
active output device (`IAudioSessionManager2`, the session's process ID
from `IAudioSessionControl2`, and `IAudioMeterInformation`), sampled every
20 ms. The sessions are listed again every second, and every 100 ms while
a running NVDA has none yet, as its first sound opens its session. The
method follows
[Matthew van Eerde's per-session peak meters](https://matthewvaneerde.wordpress.com/2012/06/08/getting-audio-peak-meter-values-for-all-active-audio-sessions/).
The meter is NVDA's own stream before the system mixes it, so the
microphone and other programs' sound do not reach it.

- A period of sound starts with the first sample at or above 0.001 and
  ends when no sample has reached it for 250 ms, so the pauses between
  words do not split an utterance. The end record comes after the gap;
  its `lastSoundAt` is when the sound stopped, and the player uses that.
- A period also ends when the process exits or the recording stops.
- The session's active state is not used: NVDA keeps its audio device
  awake after speech for a set time
  ([NVDA 2026.2 user guide](https://download.nvaccess.org/releases/2026.2/documentation/userGuide.html)),
  so a session stays active through silence
  ([Microsoft, OnStateChanged](https://learn.microsoft.com/en-us/windows/win32/api/audiopolicy/nf-audiopolicy-iaudiosessionevents-onstatechanged)).
- Sound is not speech. A period may be speech, a beep, or one of NVDA's
  sounds; the player says "playing sound".
- On the owner's instruction of 2026-10-10 ("Let's assume it works for now
  and proceed"), the meter was assumed to work. The first run on the
  target machine supports it there: the owner's speech, alone or over
  NVDA, started no period, and NVDA's periods held 96% of the system
  sound. NVDA's other audio outputs and other machines are untested.

Records: `assistive-technology-sound-started` and
`assistive-technology-sound-ended`. The first record,
`assistive-technology-watch`, gives the products watched, the intervals,
the threshold and gap, the browser's executable, and why the sound could
not be measured, if it could not.

### Storage and the player

- The payload rules are in `EventPayloadValidator`; the database tables
  are made by migration 0026; the playback index keeps the records whole
  (version 9).
- The properties panel has a "Screen reader" group with three rows for
  NVDA: running (with version, copy, and process), in the browser, and
  audio. Each row has the change buttons and count, the audio row's count
  being of periods of sound.
- The timeline has a lane and a filter for the records.
- A recording made before the records shows "not recorded".

### Tests of tracking NVDA

- Unit tests (`tests/Recorder.Tests/AssistiveTechnologyTests.cs`): the
  periods of sound from samples, including pauses shorter than the gap
  and the ends by exit and stop; matching NVDA's executable, module, and
  helpers by folder; the copy; the payload rules, including inconsistent
  records; the panel rows and their change times; the archive and the
  event summaries.
- Database tests: every record type is written and read back
  (`EvidenceTableTests`).
- A system test on the target machine: start NVDA after the recording
  begins, speak with it, talk over it, and quit it; then check that the
  start and exit times match Windows', that the module is seen in the
  browser, that the periods of sound match NVDA's sound and not the
  microphone, and that the startup sound's period starts with the system
  sound. First run 2026-10-10
  ([validation](../validation/nvda-tracking-2026-10-10.md)), with the
  first sound checked again after its fix; up to about 0.1 s of a new
  session's first sound can be missed.

## The keyboard hook

Built 2026-10-10 (step 2a). First run on the target machine the same day
([validation](../validation/keyboard-hook-2026-10-10.md)): it recorded
every key raw input did and the keys NVDA kept, refreshed once a second
while NVDA ran, and its longest callback was 184 microseconds. A second
run found that while an elevated window is in the foreground neither the
hook nor raw input receives any key, nor raw input any mouse input, and
nothing marks the gap; see the validation. A
collector, `windows.keyboard-hook`
(`src/Recorder.Collectors.Input/KeyboardHookCollector.cs`), installs a
`WH_KEYBOARD_LL` hook on its own thread and records every call. It is on
when keyboard and mouse are recorded, as raw input is.

- It observes only: every call passes the key to the next hook unchanged,
  and the collector never blocks, delays, or alters a key.
- The hook "is called in the context of the thread that installed it",
  which "must have a message loop"
  ([Microsoft, LowLevelKeyboardProc](https://learn.microsoft.com/en-us/windows/win32/winmsg/lowlevelkeyboardproc)),
  so it runs in the recorder's own process and nothing is loaded into the
  screen reader.
- The callback copies the event into a bounded queue and returns; the
  record is written from another thread. A hook that exceeds the
  `LowLevelHooksTimeout` "is silently removed" on Windows 7 and later, with
  "no way for the application to know", and the timeout is at most 1000 ms
  on Windows 10 version 1709 and later (same source).
- Record `hook-keyboard` on channel `input.keyboard-hook`
  (`src/Recorder.Contracts/KeyboardHookRecords.cs`): the virtual key, scan
  code, the flags and each of them (up, extended, injected, injected at
  lower integrity, Alt down), the extra information, the event's own time
  in milliseconds, the receipt time, and the installation that saw it.
- Order: `SetWindowsHookEx` "always installs a hook procedure at the
  beginning of a hook chain"
  ([Microsoft, about hooks](https://learn.microsoft.com/en-us/windows/win32/winmsg/about-hooks)),
  and a screen reader's hook that keeps a key does not pass it on. A
  screen reader may install its hook some time after its process starts,
  later than tracking sees the process, so one reinstall when it starts is
  not enough. As agreed on 2026-10-10:
  - while a screen reader's main process runs, the hook is installed again
    every second (`refresh`), and at once when one starts during the
    recording (`screen-reader-started`);
  - the new hook is installed before the old one is removed, so a key is
    always seen by one of them, and only the newest records, so no key is
    recorded twice;
  - a time when the hook was not first is therefore at most about a
    second, and the first kept key after a screen reader starts shows when
    it ended.
- Each installation is recorded as `hook-installed`: its number and
  reason (`recording-started`, `screen-reader-started`, `refresh`,
  `hook-lost`); whether it was installed, and the problem if not; the
  previous installation's number, its count of keys, and its longest
  callback in microseconds, as evidence against the timeout; and the
  keys dropped because the queue was full.
- Loss: the collector compares its records with raw input as it runs, the
  raw input collector passing it each key in the process. A raw input key
  from a device with no hook call of the same scan code and direction
  within the join window either side means the hook was removed; the
  collector reinstalls it and records `hook-installed` with reason
  `hook-lost`, the raw key's time and scan code, and the time of the last
  key the hook saw before it. Keys are matched by scan code because raw
  input gives Shift, Ctrl, and Alt without their side; injected keys,
  which have no device, and raw input's fake keys are not checked.
- Keys are recorded verbatim, as raw input already records them,
  including typed passwords; the hook adds the keys the screen reader
  keeps. It changes the volume of keyboard evidence, not its kind, under
  the existing privacy and data-handling policy.
- Raw input stays as built: it is the only record of the device a key came
  from.
- Storage and the player: the payload rules are in
  `EventPayloadValidator`; the database tables are made by migration 0027;
  the hook's keys share the timeline's keyboard lane and filter with raw
  input's, and the event list names the key, its direction, and whether it
  was injected.
- Tests built with 2a (`tests/Recorder.Tests/KeyboardHookTests.cs`, and
  the database tests in `EvidenceTableTests`): the loss check from
  generated sequences, including a kept key, a kept physical Tab replaced
  by an injected one, Shift without its side, and injected and fake raw
  keys; the payload rules; the summaries and key names. The system test
  below was run on 2026-10-10, with the elevated window; the integration
  tests on Windows are still to write.

## The mouse hook

Built 2026-10-11, proposed and agreed the same day, and first run on the
target machine the same day; see "First run on the target machine" below.
The owner's answers to the proposal's questions:

- Every pointer move is recorded: "yes, I want to create heat maps so I
  will probably need that granularity". Heat maps are a later step and are
  not built here.
- The mouse hook is installed again with the keyboard hook: "yes, It is
  used by NVDA for announcing content under cursor".
- No head pointer or eye tracking software is on the target machine: "no
  it is not, for now the issue is with NVDA and heatmaps". Input injected
  by a test program stands in for it.

The collector of the keyboard hook, `windows.keyboard-hook`
(`src/Recorder.Collectors.Input/KeyboardHookCollector.cs`), also installs a
`WH_MOUSE_LL` hook, on the same thread, now named "Keyboard and mouse
hooks", on the same terms as the keyboard hook:

- It observes only: every call passes the event on unchanged. The hook
  "is called in the context of the thread that installed it", which "must
  have a message loop", and one that takes too long "is silently removed
  without being called", with the timeout at most 1000 ms on Windows 10
  version 1709 and later
  ([Microsoft, LowLevelMouseProc](https://learn.microsoft.com/en-us/windows/win32/winmsg/lowlevelmouseproc)).
  The callback copies the event into the collector's bounded queue and
  returns.
- Its records are on their own channel, `input.mouse-hook`
  (`src/Recorder.Contracts/MouseHookRecords.cs`), so a recording's mouse
  hook records are counted, filtered, and stored apart from the keys.
- Record `hook-mouse`, one for every call: the installation that saw it;
  the message, and from it the kind (`move`, `button-down`, `button-up`,
  `wheel`, `horizontal-wheel`, `other`) and the button (`left`, `right`,
  `middle`, `x1`, `x2`); the point "in per-monitor-aware screen
  coordinates"; the mouse data, and for a wheel its delta, the signed high
  word, "positive ... forward, away from the user"; the flags, and each of
  them, injected and injected at lower integrity; the extra information;
  and the event's own time in milliseconds
  ([Microsoft, MSLLHOOKSTRUCT](https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-msllhookstruct)).
- Pen and touch: Windows marks the mouse messages it makes from a pen or
  touch screen in the extra information, with the signature `0xFF515700`
  under the mask `0xFFFFFF00`, and bit `0x80` set for touch
  ([Microsoft, system events and mouse messages](https://learn.microsoft.com/en-us/windows/win32/tablet/system-events-and-mouse-messages)).
  The record gives `penOrTouch` as `pen`, `touch`, or null.
- Installations: the mouse hook is installed with the keyboard hook, at
  the start, at once when a screen reader starts, and every second while
  one runs; the new hook before the old is removed, as for the keys. Each
  is recorded as `hook-mouse-installed`, with the keyboard hook's reasons
  and fields, counted in events: the previous installation's events and
  longest callback, and the events dropped. NVDA passes every mouse event
  on, as below, so the order of the hooks does not decide what the
  recorder sees while NVDA is the only other hook; it would for software
  that keeps mouse events.
- Loss: as for the keys, the collector compares its records with raw
  input's. A button pressed or released on a device that raw input
  records, with no hook call of the same button and direction within the
  join window either side, means the mouse hook was removed: both hooks
  are installed again, the mouse hook's record with reason `hook-lost`,
  the raw button's time and button, and the time of the last event the
  hook saw; the keyboard hook's with reason `mouse-hook-lost`. A keyboard
  hook loss gives the mouse hook's record reason `keyboard-hook-lost`.
  Moves and wheels are not checked: raw input's moves are a device's
  counts, not the pointer's steps, and do not pair one for one. The left
  and right buttons are matched as one, because raw input gives the
  physical button and the hook the button after the buttons are swapped
  in the settings, which is not recorded. Injected events, which raw input
  records with no device, are not checked.
- Raw input stays as built: it is the only record of the device.

### NVDA's mouse settings

NVDA's settings record (`nvda-settings`) gains the `[mouse]` section's
values, as written in `nvda.ini`, or null where it holds none:
`enableMouseTracking`, `ignoreInjectedMouseInput`, `mouseTextUnit`,
`reportObjectRoleOnMouseEnter`, `audioCoordinatesOnMouseMove`, and
`reportMouseShapeChanges`. A setting absent from the file has NVDA's
default: mouse tracking on, ignoring injected mouse input off, the text
unit a paragraph, the others off
([NVDA configSpec.py](https://raw.githubusercontent.com/nvaccess/nvda/release-2026.2/source/config/configSpec.py)).
A written value is read as NVDA's configuration library reads a boolean:
`on`, `1`, `true`, `yes` and `off`, `0`, `false`, `no`, in any case
([configobj validate.py](https://raw.githubusercontent.com/DiffSK/configobj/master/src/configobj/validate.py));
any other is not known. Records made before 2026-10-11 lack the six
fields and still pass the recorder's checks. The settings are read when
tracking first sees NVDA's process, so a change made while NVDA runs is
seen only after NVDA is started again.

### What NVDA does with an event

NVDA's mouse handler
([mouseHandler.py](https://raw.githubusercontent.com/nvaccess/nvda/release-2026.2/source/mouseHandler.py),
`internal_mouseEvent`) decides, in this order:

1. An injected event is ignored where NVDA ignores injected mouse input,
   or while NVDA is making its own mouse events (`ignoreInjected`, set
   around `executeMouseEvent`); buttons too.
2. Every event is ignored where mouse tracking is off.
3. Otherwise the position is noted. A move marks that the mouse moved, and
   NVDA reads the object at the latest position the next time it runs its
   queue (`pumpAll`, `executeMouseMoveEvent`, `objectFromPoint`), so not
   every move is read. A left or right button press queues a stop of
   speech.

NVDA always passes the event on. The player infers its outcome from
these rules and the settings of the NVDA running at the time; its
`ignoreInjected` periods are not recorded, so an injected event's outcome
says so.

### The player

- Selected event values shows each `hook-mouse` record's outcome first
  (`src/Recorder.Session/MouseActivity.cs`, rule 1): what it was and where,
  and who made it, from a device, injected by software (at a lower
  integrity level where flagged), or from a pen or touch screen; while
  NVDA runs, what NVDA did with it, as an inference, with the settings it
  rests on; the raw input record of the same event from a device within
  50 ms either side, with its device, or that there was none.
- The properties panel's Screen reader group has two rows, with the
  change buttons and count: "Pointer", the latest mouse event, its point
  and what made it, stepping through every event; and "NVDA mouse
  tracking", the two settings that decide the outcomes, as read at NVDA's
  start, stepping through NVDA's starts and exits. A recording without
  the mouse hook's records has neither row.
- The timeline puts the hook's events on the mouse lane, under the mouse
  filter; the event list names the kind, button, point, and whether it was
  injected or from a pen or touch screen.
- Storage: the payload rules are in `EventPayloadValidator`; migration
  0030 makes the tables `mouse_hook_events` and `mouse_hook_installations`
  and adds the six settings to `assistive_technology_settings`; the
  playback index, version 16, keeps the mouse hook's and raw input's mouse
  records whole.

### Limits

- What was under the pointer is not recorded here; step 3 records the
  browser's hit tests ([accessibility requests](accessibility-requests.md)).
- The left and right buttons are matched as one for the loss check.
- NVDA's settings are read at its start; a change while it runs is not
  seen.
- NVDA reads only the latest position at each run of its queue; the
  player says a move was noted, not that it was read.
- Moves and wheels are not checked for loss.

### Tests

- Unit tests (`tests/Recorder.Tests/MouseHookTests.cs`, and the database
  tests in `EvidenceTableTests`): the messages, wheel delta, pen and touch
  signature, and raw buttons; the loss check from generated sequences,
  with the buttons swapped and an injected click; the payload rules, and an
  older settings record; the reading of NVDA's `[mouse]` settings and of
  booleans; the outcomes and rows, including through the playback index.
- An integration test on Windows (`tests/MouseHookProbe`): it runs the
  collector and raw input in its own process and injects, with
  `SendInput`
  ([Microsoft, MOUSEINPUT](https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-mouseinput)),
  moves, each button, both wheels, and a click marked as touch, over its
  own window; it checks the records, the reinstallation when a screen
  reader starts, the longest callback, every record against the
  recorder's checks, and the player's outcomes.
- A target run with NVDA: the physical mouse and the probe's injected
  input, first with NVDA's "ignore mouse input from other applications"
  off, then on, NVDA restarted after the change.

### First run on the target machine

Package `43bc82d`, recording `20261012-003548-2377708138ab4e598dc99dc7dda1e894`,
232 seconds, run by the owner on 2026-10-11 as above: NVDA started 60
seconds in, the physical mouse used, the probe run, NVDA's "ignore mouse
input from other applications" turned on, NVDA restarted at 186 seconds,
and the probe run again.

- The mouse hook recorded 8,065 events: 8,035 from the mouse (7,957
  moves, 38 left and 1 right click) and 30 injected by the probe, 15 in
  each run. Raw input recorded 8,064 mouse records.
- Every one of the 8,035 events from the mouse was joined with a raw input
  record from a device within 50 ms.
- The mouse hook was installed 164 times: at the start, at each of NVDA's
  two starts, and 161 refreshes while NVDA ran. No hook was lost, and no
  event was dropped. The longest callback was 5,972.5 microseconds
  (installation 39, at the right click); every other was under 1,700.
- The probe's second run passed all 20 checks. The first run's output was
  written over by the second.
- NVDA's settings record of each run held `enableMouseTracking` written as
  `False`: mouse tracking was off in this copy of NVDA's configuration
  throughout. `ignoreInjectedMouseInput` was not written in the first run
  (off, the default) and was written `True` in the second, read after the
  restart as designed.
- The player's outcomes follow from those settings and NVDA's order of
  rules: in the first run every event, from the mouse or injected, "NVDA
  ignores it, inferred: NVDA's mouse tracking is off"; in the second, the
  injected events "NVDA ignores it, inferred: injected, and NVDA ignores
  mouse input from other applications", the injected check coming first.
  The Pointer and NVDA mouse tracking rows gave the same values.
- Not shown by this run, because tracking was off: an outcome with
  tracking on, where NVDA notes a move and a left or right press stops
  speech. The second run, below, shows it.

### Second run on the target machine, mouse tracking on

Package `43bc82d`, recording `20261012-005438-eb89151848024f6fb04042e603ccb318`,
163 seconds, run by the owner on 2026-10-11 after turning NVDA's mouse
tracking on: NVDA started 8 seconds in with "ignore mouse input from other
applications" on, the physical mouse used, the probe run at 79 seconds,
the setting turned off, NVDA restarted at 130 seconds, and the probe run
again at 144 seconds.

- NVDA's settings records: the first run `enableMouseTracking` `True`,
  `ignoreInjectedMouseInput` `True`; the second `True` and `False`.
- The mouse hook recorded 4,971 events, 4,941 from the mouse and 30
  injected; every one from the mouse was joined with a raw input record
  from a device. 155 installations, no hook lost, no event dropped; the
  longest callback was 36.7 microseconds. The probe's second run passed
  all 20 checks.
- The player's outcomes, rule 1:
  - before NVDA started and between its runs, events from the mouse with
    no NVDA part;
  - with NVDA running, moves from the mouse "NVDA notes the position,
    inferred", left presses "NVDA stops speech, inferred", releases and
    wheel turns "NVDA notes the position and does nothing more with it";
  - the first probe run, every injected event "NVDA ignores it, inferred:
    injected, and NVDA ignores mouse input from other applications";
  - the second probe run, injected moves noted, the injected left and
    right presses, the touch one too, "NVDA stops speech, inferred", the
    middle and X buttons and the wheels noted only.
- NVDA's sound, measured, agrees but does not prove it: NVDA was making
  sound through both probe runs, as the probe's window took the focus. In
  the second run a period of sound ended at 143.96 seconds, 80 ms after
  the injected left press at 143.88; in the first, the period from 76.82
  to 85.59 seconds went on through the injected left press at 79.03.

## Input the recorder cannot receive

Agreed with the owner on 2026-10-10, after the run with an elevated
window ([validation](../validation/keyboard-hook-2026-10-10.md), "The
elevated window") found that while a window of an elevated process is in
the foreground, neither the keyboard hook nor raw input receives any key,
nor raw input any mouse input, and that nothing in the recording marked
the gap. Built the same day, and run on the target machine the same day
([validation](../validation/keyboard-hook-2026-10-10.md), "Marking when
input could not be recorded"): the elevated window's level was read as
high and its periods marked; the secure desktop is still to check.

The owner's decisions:

- Elevated programs are not recorded: "For the testing I envisage, we
  wouldn't want to track anything running at elevated privilege". Giving
  the recorder UIAccess, which would let it "read input for all integrity
  levels by using low-level hooks, raw input"
  ([Microsoft, UIAccess policy](https://learn.microsoft.com/en-us/previous-versions/windows/it-pro/windows-10/security/threat-protection/security-policy-settings/user-account-control-only-elevate-uiaccess-applications-that-are-installed-in-secure-locations)),
  and running it as administrator are ruled out.
- What is on screen stays recorded, the elevated window's title and the
  screen frames included: "if it's on screen it's part of the test".
- The recording marks when input could not be recorded, and the player
  shows it.

Records, on `window.foreground`, by the foreground window collector
(`src/Recorder.Contracts/InputRecordabilityRecords.cs`). They record
privilege facts only; no input or content is taken from an elevated
window.

- `recorder-integrity`, once at the start: the recorder's own integrity
  level and UIAccess flag, from its process token.
- `foreground-integrity`, with each `foreground-window` record and at the
  same time: the window's process's integrity level, its relative
  identifier
  ([Microsoft, well-known SIDs](https://learn.microsoft.com/en-us/windows/win32/secauthz/well-known-sids)),
  and UIAccess flag, read from its token; and `inputRecordable`, false when
  the level is higher than the recorder's. Where the token cannot be read
  the level and `inputRecordable` are null and the problem is recorded; it
  is not guessed. A separate record leaves the `foreground-window` record
  and its table as they were.
- `input-desktop`, at the start and whenever the desktop that receives
  input changes: its name, or the problem when the recorder cannot open
  it, as with the secure desktop of a permission prompt; input is
  recordable only on the user's desktop, `Default`. The change is found by
  Windows' event that "the active desktop has been switched"
  ([Microsoft, event constants](https://learn.microsoft.com/en-us/windows/win32/winauto/event-constants))
  and by a check every 250 ms, in case the event is not delivered across
  desktops; the record's reason says which.

All of the channel's records are written by the collector's one queue, in
the order of their times, each time taken with its write under one lock.
Before 2026-10-11 the input desktop and recorder integrity records were
written at once from their threads, while foreground windows waited in
the queue; the first foreground window of every recording, queued before
the first input desktop record was written, went backward in time, and
the check refused it and its `foreground-integrity` record
(`event-time-regressed`), which made every recording since 20261010-224236
fail.

In the player:

- Input is not recordable on another desktop, or while the foreground
  window's level is higher than the recorder's; a window whose level could
  not be read is shown as unknown, not as a gap
  (`src/Recorder.Session/InputRecordabilityTimeline.cs`).
- The timeline draws each such period as a striped band over the keyboard
  and mouse lanes.
- The properties panel's "Keyboard and mouse" group has an "Input
  recordable" row: "recordable", "not recordable" with the reason, such as
  "high integrity window, powershell" or "a desktop the recorder cannot
  open, such as the secure desktop", or "unknown" with the problem; with
  the change buttons and count. The timeline is drawn, so the row is where
  the reason is read.
- The event list summarises each record.
- Storage: the payload rules are in `EventPayloadValidator`; migration
  0028 makes the tables; the playback index keeps the records whole
  (version 10).
- Key disposition (step 2b) treats these periods as gaps, so a key missing
  from both records there is not reported as lost, and notes a press or
  release that fell in one.

Tests:

- Unit tests (`tests/Recorder.Tests/InputRecordabilityTests.cs`, and the
  database tests in `EvidenceTableTests`): the level names and the rule;
  the payload rules; the periods from the records of the elevated window
  run, the desktop before the window, an unreadable level; the panel row
  and its change times; the summaries.
- A system test on the target machine: the elevated window run again,
  showing a period for each elevated episode and for the permission
  prompt, with input recorded either side, and whether an elevated
  process's token can be read.

## Key disposition

Step 2b. Agreed with the owner on 2026-10-10: the outcomes and their
wording ("I guess so"); the windows to be tried and adjusted after testing
("We will see, we build and we test"); and the event list and details as
enough for now, with nothing on the timeline ("It's enough for now").

Derived at playback from the records, so a recording made before it is
read the same way; nothing changes in the recorder
(`src/Recorder.Session/KeyDispositions.cs`). The rule carries a version,
1, so a recording can be read again under a later rule.

Each key record, down and up, gets one outcome:

- Passed: a key from the keyboard that both the hook and raw input
  recorded, so it went on to Windows; "received by the page" when the
  page's trusted `keydown` joins it.
- Kept: a key from the keyboard that the hook recorded and raw input did
  not, so a program stopped it. "screen reader running", with the
  product, when a screen reader's main process was running; this is an
  inference, as Windows does not say which program kept the key.
- Injected: a key a program sent, not the keyboard: a hook record with the
  injected flag, or a raw input record with no device handle. Linked to a
  kept key of the same virtual key that it follows, as NVDA's own Tab
  after the Tab it kept.
- Unexplained: shown as recorded, with the reason, such as "raw input
  only, hook not installed".

A note is added where a key's press or release fell in a period when
input was not recordable (see "Input the recorder cannot receive"), such
as "release not recorded: input not recordable"; such a key is not judged
lost.

The joins, from the first keyboard hook run
([validation](../validation/keyboard-hook-2026-10-10.md)), recording
20261010-211432:

| Join | Window | Measured |
| --- | --- | --- |
| A hook record to a raw input record of the same scan code, direction, and source (injected or from a device) | 50 ms either way | raw input 0.2 to 1.9 ms after the hook, 136 pairs |
| An injected key to the kept key of the same virtual key it follows | 50 ms after | NVDA's Tab about 2 ms after the Tab it kept |
| A passed or injected key down to the page's next trusted `keydown` | 0 to 100 ms after | 0.7 to 2.3 ms, and once 58 ms, 44 keydowns |

The page's `keydown` record does not say which key it was, so it is
joined by order and time only.

A kept key also names the injected key that followed it, as "followed by
an injected Tab, received by the page", with the injected records in its
details: NVDA's Tab is 2 ms after the Tab it kept, too close to select
apart at the zoom of the time (owner, 2026-10-10).

In the player, the status line's description of a key record, and of a
joined `keydown`, ends with its outcome, and the selected event's details
start with the outcome and the records it was joined with, and their
times. The playback index keeps the key records, the hook installations,
and the page's trusted `keydown` dispatches whole for this (version 11),
so a recording made before is read again, once, when it is opened. A
recording without a recording file, from before 0013, gets no outcomes.

Tests: unit tests of each outcome and note, from the patterns of the
recordings of 2026-10-10; a system test on the target machine, opening
recording 20261010-211432 in the player and comparing the outcomes with
the steps taken.

## NVDA's commands, mode, and windows (step 2c)

Agreed with the owner on 2026-10-10: built in three parts, each tested on
the target machine before the next: 2c-1, the commands; 2c-2, browse or
focus mode, inferred; 2c-3, NVDA's windows. The first command data covers
the quick reference's Basic NVDA commands, Navigating with the System
Focus, and Browse Mode, with the Elements List; other sections are added
as recordings need them. The mode and NVDA's windows each have a band on
the timeline, as well as a properties panel row: "I want a band on the
timeline".

What the target machine showed, read without change on 2026-10-10:
NVDA's portable `userConfig\nvda.ini` sets `NVDAModifierKeys = 7`, which
NVDA's configuration specification defines as "1: CapsLock, 2:
NumpadInsert, 4: ExtendedInsert", with a default of 6
([NVDA configSpec.py](https://raw.githubusercontent.com/nvaccess/nvda/master/source/config/configSpec.py)),
so Caps Lock is also an NVDA key there; the keyboard layout is not set, so
it is the default, `desktop`; there is no `gestures.ini`, and there are no
profiles or add-ons. In recording 20261010-211432 the Elements List came
to the front at 92.819 s, and UI Automation recorded each tree item
focused in it, by name; the Up arrows from 99.95 s moved through it, which
is why they passed without reaching the page.

### 2c-1, the commands

The recorder reads NVDA's settings when its main process starts, or is
running at the start, and records them (`assistive-technology-settings`):
from `userConfig` in a portable copy's folder, or `%APPDATA%\nvda` for an
installed copy. It reads only, and records only the keyboard layout, the
NVDA modifier keys, the multiple press timeout, the browse mode options,
the names of any configuration profiles, whether profile triggers are
set, and the custom key commands of `gestures.ini`, each as written, with
the folder and any problem. A copy started with another configuration
path is not followed. The settings are those at the process's start; a
change saved during the recording is not seen.

At playback, the key combination of each kept key is worked out from the
keys held down with it (Shift, Ctrl, Alt, Windows, and the NVDA keys of
the settings), named as NVDA names keys (`NVDA+f7`, `shift+h`), and looked
up in the command data for the product and version
(`src/Recorder.Session/ScreenReaderCommands/nvda-2026.2.json`), in the
keyboard layout recorded, with the defaults of the configuration
specification where a setting is not written. A custom command in the
settings comes first. A kept key's outcome gains its command, as an
inference with its basis, such as "command: next heading (H), inferred";
the same command again within the multiple press timeout is noted as
pressed twice. A version without its own data uses the nearest earlier
version's, and says so. A kept key with no command stays kept. A lone
Ctrl or Shift, which NVDA passes on and acts on (stop and pause speech),
is labelled as a passed key's command.

Built as key outcome rule 2: the commands are worked out only for
physical keys while NVDA's main process runs; a key held down is labelled
at its first press, not at the keyboard's repeats; an NVDA key is noted as
such; and the details give the command's source, the keyboard layout, the
NVDA keys, and whether each comes from nvda.ini or NVDA's default. The
first data has 90 commands. Storage: the payload rules are in
`EventPayloadValidator`; migration 0029 makes the tables
(`assistive_technology_settings`, with its profiles and custom gestures);
the playback index keeps the records whole, as the channel's other
records. Tested at the unit level (`ScreenReaderCommandTests`), and
against recording 20261010-211432, which predates the settings record and
so used NVDA's defaults: H was labelled next heading, D next landmark,
Shift+D previous landmark, K next link, and Insert+F7 at 92.8 s and
122.4 s the elements list. The system test on the target machine, with
the settings recorded, is outstanding.

Added on 2026-10-12, as the owner agreed after the arrow key recording
(20261012-163736), where kept arrows had no command: the quick
reference's Navigating with the System Caret (17 commands, such as say all
and next sentence; four have no laptop key there, so none is listed), and
the browse mode caret movement and selection keys (29), which the quick
reference does not list. NVDA binds those in its source, `cursorManager.py`
([NVDA 2026.2 cursorManager.py](https://raw.githubusercontent.com/nvaccess/nvda/release-2026.2/source/cursorManager.py)),
whose scripts have no descriptions but Find's, so the recorder names them
from the scripts' names (`moveByLine_forward` is "next line"), and the
data's section says so and gives that source. Find, copy (Ctrl+C), and next
and previous sentence are left out of that section, as the reference lists
Find and the sentences and copy is not a caret key. The data now has 136
commands; over recording 20261012-163736, each Down arrow is labelled next
line and each Up arrow previous line.

### 2c-2, browse or focus mode

Agreed with the owner on 2026-10-11: the rules below ("yes"); where the
recording does not show the mode, it is unknown ("We should be honest and
say unknown"); the band is on the Screen reader lane ("yes").

The evidence, while NVDA's main process runs, the instrumented browser is
the front window, and no Ctrl, Alt, Windows, or NVDA key is held (Shift
may be):

- browse mode: a kept letter, digit, arrow, or Tab, or a kept browse mode
  command such as NVDA+F7; NVDA keeps keys in browse mode, and with its
  default of trapping keys that are not commands
  ([NVDA configSpec.py](https://raw.githubusercontent.com/nvaccess/nvda/master/source/config/configSpec.py),
  `trapNonCommandGestures`), even a letter that is not a command;
- focus mode: a letter, digit, arrow, or Tab that passed and that the page
  received;
- a switch: NVDA+Space kept switches to the other mode
  ([NVDA 2026.2 commands quick reference](https://download.nvaccess.org/releases/2026.2/documentation/keyCommands.html#browse-mode)),
  and Escape kept, with no Escape sent on after it, returns to browse
  mode. NVDA keeps Escape in both modes, and sends it on where it does not
  switch: in browse mode, in focus mode entered with NVDA+Space, and in a
  menu
  ([NVDA browseMode.py, release-2026.2](https://raw.githubusercontent.com/nvaccess/nvda/release-2026.2/source/browseMode.py),
  `script_disablePassThrough`), seen as an injected Escape just after the
  kept one. Such an Escape shows no mode. A kept Tab stays browse mode
  evidence, as NVDA keeps Tab only in browse mode (`script_tab`), sending
  it on or moving the focus itself.

A key that passed but that no page received, such as one in the browser's
toolbar, shows neither mode. Keys in NVDA's own windows, such as the
Elements List, do not count, as the browser is not in front.

The spans (`src/Recorder.Session/ScreenReaderModeTimeline.cs`): a mode
from its first evidence to its last; where two evidences differ with no
switch between, the span between them is "changed between", because NVDA
switches by itself (on focusing a form field, for example) without a key.
A page load (a committed cross-document navigation of a tab's page), and
NVDA starting or exiting, end the inference, as NVDA starts a new page in
browse mode by default. Before a mode's first evidence and after its last,
the mode is unknown. Between two evidences of one mode, the mode is drawn
as unbroken, an inference: it could have switched and switched back
without a key the recording shows.

In the player: the band in a strip of its own, 16 pixels high, directly
under the Screen reader lane, so the lane's event marks never cover it;
browse mode solid blue, focus mode solid amber, and the span in which it
changed grey stripes, with the words "browse", "focus", or "changed"
written in the band where they fit, so it does not rely on colour; the
strip is empty where the mode is unknown. Clicking the strip moves the
playhead to that point, so the NVDA mode row shows the mode, with the
mouse alone. Contrast against the timeline's background: browse 3.2:1,
focus 6.8:1, the stripes 5.7:1; the words 5.2:1 (white on blue) and 6.8:1
(dark on amber), the word "changed" on a dark backing. The Screen reader group has an "NVDA mode" row: for
example "browse mode, inferred from H kept (next heading) at 00:00:51.782
to NVDA+f7 kept (elements list) at 00:02:02.382", "changed between
00:04:12.221 and 00:04:13.613, from focus mode to browse mode, inferred",
or "unknown; last seen focus mode, ...", "unknown; a page loaded at ...",
or "unknown; NVDA exited at ...", with the change buttons and count.

Against recording 20261010-211432: browse mode from 33.5 s to 122.4 s;
unknown after the page loads from 145.4 s; focus mode from 181.4 s to
195.6 s; NVDA exited at 228.0 s and started at 236.5 s, so the keys
between show nothing; focus mode from 244.1 s to 246.3 s; page loads at
248.8 s and 250.1 s; focus mode at 252.2 s, then changed between 252.2 s
and 253.6 s; browse mode from 253.6 s to 265.8 s. Against 20261011-035348:
browse mode from 40.7 s to 120.6 s. Tested at the unit level
(`ScreenReaderModeTests`); the system test on the target machine is
outstanding.

First target run, 2026-10-11: no band, and the row always unknown. The
player opens a recording from its playback index, which keeps only the
records playback reads, and version 11 did not keep the foreground window
records, so no key was seen with the browser in front. The checks before
it had read the exported records, not the index. Version 12 keeps them,
and an older index is derived again when the recording is opened; a test
now builds the mode through the index and the archive, as the player does
(`ThePlayerFindsTheModeThroughThePlaybackIndex`), and fails without the
fix.

Third target run, 2026-10-11, recording 20261011-144503: NVDA+Space at
61.9 s, typing from 68.0 s, Escape at 70.9 s, Tab at 74.7 s. NVDA kept
Escape and sent its own on 1.4 ms later, so focus mode did not end; the
first rule, Escape kept means browse mode, drew a "changed between" span
to the Tab. Corrected as above: focus mode from 68.0 s to 74.7 s. NVDA+Space
from a mode not known now reads "unknown; NVDA+Space switched from an
unknown mode at ...". NVDA was then exited from its menu and crashed on
exit (0xC0000409 in UIAutomationCore.dll), and the recorder crashed in
the same place after its collectors had stopped. Open fault, agreed with
the owner to note it without crash dumps: it did not recur in the next
recording (20261011-150323), where NVDA exited cleanly. The crash left
the recorder's database server running, which held the files the start
script removes; it was stopped with `pg_ctl stop -m fast`.

Fourth target run, 2026-10-11, recording 20261011-150323: Tab kept at
35.5 s, browse mode; the form page reloaded at 39.2 s to 40.0 s, ending
the inference; NVDA+Space at 49.3 s from an unknown mode; focus mode from
53.3 s to 56.1 s; both Escapes, at 59.2 s and 62.2 s, kept and sent on,
so no switch, as NVDA's source says for focus mode entered with
NVDA+Space.

Second target run, 2026-10-11: the band was there, but drawn within the
Screen reader lane, about 6 pixels high, focus mode's upright stripes were
gold flecks that read as event marks. Agreed with the owner ("All three"):
a strip of its own, solid fills with words, and a click in the strip
moves the playhead, as above.

### 2c-3, NVDA's windows

Agreed with the owner on 2026-10-11: the proposal below; NVDA's main
frame, which comes to the front for a moment before each of its dialogs,
is folded into the window that follows it ("I guess we merge"); and a
window that another window replaces in front "left the front", not
"closed" ("Yes"), as the foreground records do not show a window closing.

The evidence, in recording 20261010-211432: the foreground window records
name NVDA's main process (the `screen-reader` role of the assistive
technology records, process 20008) with each window's title, and UI
Automation's focus records name each element focused in it:

- "NVDA", NVDA's main frame, at 92.768 s, then "Elements List" at
  92.819 s, its tree items focused by name ("Contact Us" to "CNIB Come to
  Work"), until the Perplexity app came to the front at 105.010 s;
- the Elements List again from 122.384 s to 133.987 s;
- an untitled window from 221.978 s, focused items a menu ("Context") and
  its items ("Exit", "Donate", "Save configuration"), the NVDA menu;
- "Exit NVDA" from 223.483 s, its combo box "What would you like to do?"
  and its OK button pressed at 225.470 s.

The spans (`src/Recorder.Session/ScreenReaderWindowTimeline.cs`): while a
window of NVDA's main process is the front window, from its foreground
window record to the next one of another window, NVDA exiting, or the end
of the recording. NVDA's main frame, titled "NVDA", is folded into the
NVDA window that follows it; alone, as when NVDA starts, it is a span of
its own. A window is named by its title; an untitled one with a menu or
menu item focused in it is the "NVDA menu", another untitled one "NVDA
window (untitled)". A span whose window came to the front within 1 s
after a kept key whose command opens that window (NVDA+F7, the elements
list; NVDA+N, the NVDA menu; NVDA+Q, Exit NVDA, by the command data's
names) is noted as "opened by NVDA+f7 (elements list) at ..., inferred".
A window opened from the tray icon has no such key, and no opener.

In the player: a second strip, 16 pixels high, under the mode strip, as a
mode span can cover an NVDA window (browse mode from 33.5 s to 122.4 s
covers the first Elements List); solid green (#3F6B3A) with the window's
name in white where it fits, 6.2:1, one pixel short at the right so that
windows that follow one another show as two; empty where none is in
front. Clicking it moves the playhead there, as the mode strip does. The
Screen reader group has an "NVDA window" row: for example "Elements List,
in front from 00:01:32.768, opened by NVDA+f7 (elements list) at
00:01:32.766, inferred; focus: tree item "Contact Us" at 00:01:33.009", or
"none in front; Elements List left the front at 00:01:45.010, when
"Perplexity" (Perplexity) came to the front", with the change buttons and
count, at each span's start and end and at each element focused in the
window, UI Automation's repeat of the same element at once given once.

Storage: playback index version 13 keeps the foreground window records
whole, for their process ID and title, and the UI Automation focus
records (`focus-changed`) whole, a few hundred a recording; an older
index is derived again when the recording is opened. Mode inference is
unchanged: keys in NVDA's windows already do not count. Not in this
part: what the page did after the Elements List closed, which belongs to
the reading position (step 4), and the text typed into the list's filter.

Against recording 20261010-211432, as the player opens it: the spans
above, with both Elements Lists opened by NVDA+F7, the NVDA menu from
221.969 s to 223.483 s, Exit NVDA to 225.498 s; NVDA's main frame alone
at 15.7 s and 238.1 s as NVDA started. Tested at the unit level
(`ScreenReaderWindowTests`, including through the playback index).

First target run, 2026-10-11, package 74c5c3f: the owner confirmed the
green strips line up with NVDA's dialogs, and the NVDA window row reads
correctly in the properties panel ("Yes the green strips line up with
NVDA dialogs, and they appear correctly in the properties panel").

Second target run, 2026-10-11: "I can't step through the actions when an
NVDA window is open, only between openings". The row's steps were only
each window's coming to the front and leaving it. Each element focused in
the window is now a step too: in recording 20261010-211432, the Elements
List from 92.768 s has ten, from "Contact Us" at 93.009 s to "CNIB Come
to Work" at 102.055 s.

Then: "they also need to [be] events on the timeline too (so that I can
click on the timeline and then arrow through the events for the dialog".
The focus records are on the UI Automation lane, among about 1,200 other
UI Automation events a second on the target machine, so they cannot be
stepped through there. The window strip now has its own events: the
foreground window record that brought the window to the front, each focus
record of its steps, and the record of the window that replaced it, each
marked at the strip's foot. Clicking the strip selects the nearest event
of the window clicked, shown in Selected event values with the playhead
moved to it; Left, Right, Home, and End step through the strip's events,
across windows; Up and Down leave it for the lanes above and below, and
moving past the Screen reader lane with Up or Down enters it. A click in
the strip where no NVDA window was in front moves the playhead, as
before.

Then: "When I look at one of the events, I don't see anything identifying
it as an NVDA dialog event". The records are UI Automation's and the
foreground window collector's, unchanged. Selected event values now gives,
before a record of an NVDA window, the window and the record's part in
it, as the key outcome comes before a key record: for example "NVDA
window: Elements List, in front from 00:02:02.384 to 00:02:13.987, opened
by NVDA+f7 (elements list) at 00:02:02.382, inferred; left the front at
...", then "This record: element 6 of 11 focused in it, tree item "CNIB
Deafblind Community Services"". A record that ends one window's span and
starts the next, as Exit NVDA replacing the NVDA menu, is given for both.

### 2c-4, what NVDA's windows show

Asked by the owner on 2026-10-11, after the 2c-3 runs: "what do we
actually store for these dialogs when they open, do we record the entire
menus, or is it just the content reported on focus?" It was only the
latter. The UI Automation collector reads each event's own element
(`TreeScope_Element`), so a window was recorded only as far as the
elements focused or selected in it. In recording 20261010-211432, NVDA's
main processes (it was started twice) gave 51 focus changes, 42 structure changes that do not name the
child, 18 selections, and 3 invokes, and no property or text changes: not
the rest of the Elements List, its element type, its filter text, or the
option chosen in Exit NVDA. The owner chose to record the windows' trees,
"for test reporting. To know accurately what NVDA was displaying at any
time. It should also help with automated test analysis later", and agreed
the design on 2026-10-11: limits of 5,000 elements and 2 seconds, "Only
NVDA", and the outline and summary below.

A window snapshot collector (`NvdaWindowSnapshotCollector`, collector
type `windows.ui-automation.nvda-window-snapshots`) is given each event
the UI Automation collector records, with its element, and NVDA's main
process IDs from assistive technology tracking. It reads a window of
NVDA's main process, the element's ancestor whose parent is the desktop:

- when focus enters it from another window or from outside NVDA
  (`focus-entered-window`);
- when the window has been quiet for 300 ms after a structure change,
  selection, invoke, text change, or watched property change in it, so
  one burst gives one snapshot (`window-changed`);
- when focus moves within it to an element the last snapshot did not hold
  (`focused-element-not-in-snapshot`), or held with another name, value,
  toggle, selection, expansion, or enabled state
  (`focused-element-changed`).

The tree is UI Automation's control view, the view assistive technology
is given, read in document order, each element before its children. Each
element has its parent's index, its depth, and its place among its
siblings; its runtime ID, name, control type, automation ID, class,
framework, access key, help text, enabled, off screen, focus, and
password states, and bounding rectangle; and, only where the element says
it has the pattern, its value and whether that is read only, its toggle
state, whether it is selected, and its expanded or collapsed state, so a
default is never taken for a state. A password's value is not recorded. A
read stops at 5,000 elements or after 2 seconds, and the record says
which (`truncated`). A child list whose read fails ends there, with the
failure in the record's `problems`, and the read goes on.

The trees are read on a thread and UI Automation connection of their own,
so the UI Automation collector's queue never waits for them, and written
under their own collector instance on `accessibility.uia.events`, so
their times are in order within it. Notices queue up to 256; any refused,
and any whose handling failed, are counted and stated at stop as
collector omissions. NVDA's windows are read only while NVDA runs; no
other process's windows are read. The database's typed evidence tables do
not cover the record; it is stored as a recorded event, as other records
without a typed table are.

The record, `window-snapshot`: `reason`; `trigger`, the event that led to
it (type, ID, and time); `processId`; `elementCount`; `truncated`, null,
`element-limit`, or `time-limit`; `readNanoseconds`; the limits;
`elements`; and `problems`. It is timed at the start of the read. The
validator requires the window first, each element after its parent and
one level deeper, the count to match, a snapshot stopped at the element
limit to hold that many, and no password value.

In the player: each snapshot is a step of the NVDA window row and an
event of the window strip, belonging to the span of its process it was
read in. The row adds what the window showed at the playhead, for
example "showing at 00:01:33.020: 2 radio buttons, 47 tree items;
selected: "Links" (radio button, selected), "Contact Us" (tree item,
selected, focused); Filter by: ""; focus: ..."; the counts are of the
window's items (tree, list, menu, data, and tab items, radio buttons, and
check boxes). Selected event values gives a snapshot as an indented
outline before the record, each element under its parent with its type
and states, for example `"Contact Us" (tree item, selected, focused)`,
then any reads that failed. Playback index version 14 keeps each snapshot
without its elements, with the summary in their place
(`WindowSnapshots.Compact`); the outline is read from the recording.

Unknown until the target machine shows it: whether NVDA's wx dialogs
raise UI Automation events when the Elements List is filled again, on
choosing another element type or typing in the filter. If they do not, a
change is read only when focus next moves to an element it changed. The
target run checks this; a fallback, such as reading again while the
window is in front, is a question for then.

First target run, 2026-10-11, package d6f0ab5, recording
20261011-195613: 18 snapshots, none truncated, read in 28 to 406 ms, with
no evidence dropped. The Welcome to NVDA dialog, with its three check
boxes and keyboard layout "desktop". The Elements List from 23.199 s:
read on focus entering it, with 63 links and the Links type selected;
again on choosing Headings (an invoke, then a structure change), 16
headings; and again as the filter was typed, from text changes and
structure changes, with the filter's value and the tree each time ("n",
13 headings; "ne", 3; "new", 1; back to "n" and empty; "vo", 3; "vol",
2). So NVDA's wx dialogs do raise the events, and the question above is
settled for the Elements List. The NVDA menu, a menu named "Context" with
its nine items, after NVDA's untitled window, read as a single pane. Exit
NVDA, with its combo box's value, "Exit". In NVDA's dialogs the window and
the tree also say they have the keyboard focus, so the summary names the
deepest element that has it; a value that repeats the name, as each tree
item's does, is not repeated. Playback index version 15 derives the
summaries again.

The Windows probe's first run on the target machine passed 12 of 16
checks. Two failures were the probe's own: it found the filter's label,
which has the edit's name, in place of the edit. The other two were the
collector's: a window owned by another, a second form shown by the
first, is placed under its owner in UI Automation's tree, so it was read
as part of the first, and focus coming back was not a new entry. A
window is now the element's nearest ancestor that is a top-level Win32
window, whose parent is the desktop window, as an owned window is.

Second run of the probe, 2026-10-11, package f027fc5: all 17 checks
passed. The second form was read on its own, 8 elements, and focus coming
back read the dialog again; the tree of 6,000 items stopped at the
element limit, 5,000, in 919 ms.

In the player, package f027fc5, recording 20261011-195613: the owner
confirmed the snapshots show on the window strip: "I have checked the
recording and I saw the NVDA settings when I look at the green strips".

Tested at the unit level (`WindowSnapshotTests`: the walk's order and
limits on a fixed tree, a failed child list, the validator, the summary,
the outline, the compact form, and the player through the playback
index), and on Windows by `tests/WindowSnapshotProbe`, which runs the UI
Automation and snapshot collectors in its own process, standing in for
NVDA's, against a dialog of standard Win32 controls built like the
Elements List, and checks a snapshot on focus entering it, on selecting
another tree item, on typing in the filter with the tree filled again, on
choosing the other type, on another window of the process and back, and
a stop at a limit with 6,000 tree items; and that every record passes the
recorder's checks with no evidence dropped.

## The reading position (step 4)

Agreed with the owner on 2026-10-12. The reading position is the object
NVDA's browse mode caret was last on, inferred from what NVDA asked of
Chromium, with the accessibility tree and the commands given, and shown
with its basis. Step 3 showed what it rests on: in the two NVDA recordings
of 2026-10-12, H, Shift+H, D, Shift+D, the Down and Up arrows, and the
Elements List's "Move to" each sent `scrollToMakeVisible` on the object
moved to, Tab sent `focus`, and Enter `doDefault`
([accessibility requests](accessibility-requests.md), "The target run" and
"Arrow keys, 2026-10-12").

The rules:

1. A scroll request from NVDA (`scrollToMakeVisible`) moves the reading
   position to its object. The basis is the request, with the key and its
   command, or the click, it is linked with.
2. When the page's focus moves, in browse mode or focus mode or after a
   focus request, the reading position moves to the focused object. The
   target run showed the caret follows the focus in browse mode too (see
   below).
3. A scroll request on the object already at the reading position keeps
   it there; where within it, such as the line of a paragraph, is said to
   be unknown.
4. A navigation key NVDA kept that sent nothing, such as H after the last
   heading, leaves the reading position where it was, marked uncertain
   from that key until the next request or focus change.
5. Before the first request or focus change on a page, and after the page
   changes, the reading position is unknown. NVDA may start at the top,
   but nothing recorded shows it.

The object is described as in the Under the pointer (Chromium) row: its
role, name, the text it holds, and the object it is in.

Built in three parts, each tested on the target machine before the next:

- 4a, the reading position: a Properties row, "Reading position
  (inferred)", in the Screen reader group, with change buttons and a
  count; a timeline band; and the basis in Selected event values.
- 4b, the list of steps: each move, with its time, the key and command or
  the click, the object reached, and the basis; a step moves the playhead
  to it, with targets of 44 by 44 pixels.
- 4c, a view of the tree: read only, at the playhead, rebuilt from the
  accessibility checkpoints, with the reading position and the object
  under the pointer marked; as the checkpoints do not record deletions,
  each node's latest version is the best known.

What NVDA had to announce is a later step, as it needs NVDA's rules of
speech for each role.

Before 4a, a run on the target machine of the browse mode commands not yet
tried (say all, Ctrl+Home and Ctrl+End, moving by character and word, Home
and End, Page Down and Page Up, Find, moving into a frame, focus mode, and
selecting), to check the rules against them. Tests of the rules are at the
unit level, and of the row, band, list, and tree view at the system level
on the target machine.

### The target run of 2026-10-12

Recording 20261012-182946-7c45f6facb4043b7b2d9ea8a6f65621c, at dc330ca,
on the test page (`tests/fixtures/accessibility-requests`), with the
pointer still on the taskbar, read from the requests NVDA made of Chromium
(`accessibility-action-sent`), the page's focus and selection records, the
key outcomes, and the foreground window records.

- **Page start**: a click in the page at 4.9 s set the page's caret, at the
  end of the link "Read more about the test"; NVDA asked nothing until the
  first key, 45 s later. Rule 5 holds: nothing shows where NVDA started.
- **Ctrl+End and Ctrl+Home** each sent `scrollToMakeVisible`: on the
  footer's paragraph, and on "Test site" (`strong`, the first object).
- **Lines**: each Down arrow sent `scrollToMakeVisible` on the next object:
  the link "Home", the heading "Accessibility requests test page", then
  "Section one", then the paragraph after it.
- **Within the paragraph**: Right arrow three times, Ctrl+Right three
  times, End, and Home each sent `scrollToMakeVisible` on the same
  paragraph, with no accessibility focus action. The target rectangle was
  the whole paragraph's (866 by 55, at 0, 0), the same each time, so it does
  not show the line, word, or character. Rule 3 holds.
- **Page Down**, twice, sent `scrollToMakeVisible` on the same paragraph;
  Page Up then sent it on "Test site", at the top.
- **H and Shift+H** sent `scrollToMakeVisible` on each heading, and **into
  the frame**: H after "Section two" went to "Inside the frame" in the
  frame's own tree (another tree ID), and the next H back to "Section
  three" in the page's. Down arrows also went into the frame: the paragraph
  after "Section two", "Inside the frame", then "Frame button".
- **Find**: NVDA+Ctrl+F opened NVDA's Find window, whose typed letters
  reached it; Enter there sent `scrollToMakeVisible` on the paragraph
  "Content that scrolls under the sticky header.", 0.16 s after the key.
  **NVDA+F3** found no other match: it sent nothing, and NVDA's window "0
  matches" came to the front. The next Ctrl+Home, at 340.4 s, went to that
  window, not the page; the page came back to the front at 344.7 s, and at
  346.3 s the page's selection was set, by the browser, to the end of the
  heading "Section three"'s text, which nothing recorded explains.
- **Say all** (NVDA+Down arrow, at 382.3 s) sent nothing while NVDA read,
  to Shift (pause speech) at 395.7 s and Ctrl (stop) at 413.1 s. The
  reading position moved as NVDA read, without a trace. The next Ctrl+Home
  sent `scrollToMakeVisible` on "Test site" with no accessibility focus
  action, as Chromium's accessibility focus was still there from before.
- **Tab**: the first Tab, which NVDA kept, sent `focus` on the link "Home",
  and the page's focus moved there. The next two Tabs and Shift+Tab passed
  to the page, which moved its focus to the links "Section two", "Far
  down", and "Section two" again, with no requests. Chromium's
  accessibility focus stayed on "Test site" throughout, so it alone is not
  the reading position after a focus change: rule 2 is needed.
- **Focus mode**: NVDA+Space, then Tab twice moved the page's focus to "Far
  down" and the button "Show message"; NVDA+Space back to browse mode, and
  Down arrow then sent `scrollToMakeVisible` on the link "Read more about
  the test", the object after the button. So the browse mode caret had
  followed the focus to the button, as rule 2 says.
- **Selection**: Shift+Down arrow, twice, sent nothing, and the page's
  selection did not change. The caret moved with the selection, without a
  trace.

The Tab results follow NVDA's browse mode: Tab and Shift+Tab move the
focus to the next or previous focusable object after the caret when the
caret is not within the focused object, and are passed to the page when it
is, or when the caret last moved because the focus did
([NVDA 2026.2 browseMode.py](https://raw.githubusercontent.com/nvaccess/nvda/release-2026.2/source/browseMode.py),
`_tabOverride`). The first Tab moved the focus from "Test site", the
caret's object, to the next focusable one. The keys with traces sent their request 16
to 40 ms after the key.

### Decisions after the target run

Made on 2026-10-12. The owner left rules 6 and 7 to the recorder's
judgment, and agreed to the selection rule.

6. **Say all** makes the reading position unknown from the key until the
   next request or focus change. NVDA's reference says say all "Starts
   reading from the current position of the system caret, moving it along
   as it goes"
   ([NVDA 2026.2 commands quick reference](https://download.nvaccess.org/releases/2026.2/documentation/keyCommands.html#navigating-with-the-system-caret)),
   and the run showed it sends nothing as it goes, so where it stopped is
   not in the recording. "Uncertain" would claim more than is known.
7. **A key while a window other than the browser is in front**, such as
   NVDA's "0 matches" or its Find window, leaves the reading position where
   it was, not uncertain: the key went to that window, not to the browse
   mode document, as the foreground window records show. A move made from
   NVDA's window, such as Find's Enter, sends its own request, and rule 1
   applies to it.
8. **Selection commands** that send nothing (the Shift+arrow commands, and
   the other selection commands of the command data) make the reading
   position unknown, as say all does: NVDA's source moves the caret with
   the selection
   ([NVDA 2026.2 cursorManager.py](https://raw.githubusercontent.com/nvaccess/nvda/release-2026.2/source/cursorManager.py)).
   Rule 4's "uncertain" stays for other kept keys that send nothing, such
   as Find next with no match.
9. **Page Down and Page Up** follow rule 1, marked uncertain. NVDA moves
   them by its lines per page, 25 by default (`linesPerPage`, in
   `cursorManager.py` and NVDA's configuration specification), but in the
   run both Page Downs sent a request on the paragraph the caret started
   on, so the request may not be where the caret went. A short test can
   settle it later.

Test runs on the target machine from now on cover one behaviour each, in
at most five steps, with what to check on screen, as the owner asked: "You are
creating too complex a test script. I can't remember it all and I lose my
place trying to follow. That reduces my ability to observe."
The owner checks the player by reading it on screen as an auditor, not
with a screen reader, so each step says what to look for, not what to
listen for (2026-10-12).

### 4a as built

Built on 2026-10-12, rule version 1 (`ReadingPositionTimeline` in
`Recorder.Session`, key `screen-reader.reading-position`). It is inferred at
playback from:

- the actions sent to the page (`scrollToMakeVisible` and `focus`), with
  the key or click each is linked with, as Selected event values gives them
  ([accessibility requests](accessibility-requests.md), "Actions, keys, and
  clicks");
- the keys NVDA kept, with their commands (key disposition);
- the windows brought to the front (`window.foreground`), against the
  browser's process;
- the page's focus records (`browser.interaction`, `focus-changed`, outcome
  `focused`) in page documents, whose DOM node is matched to an object by
  the accessibility checkpoints' `domNodeId`;
- the pages loaded in the tab's main frame, other than the browser's own
  pages (`chrome:` and the like).

Decisions made in building it:

- A kept key whose command sent a request or focus makes no step of its
  own; the request does, with the key in its basis.
- The navigation commands of rule 4 are those named "next ...",
  "previous ...", or "move ...", and start of line, end of line, top of
  document, bottom of document, find next, and find previous. The Elements
  List, Find (which opens a window), and the toggles are not.
- Rule 4 needs a position to mark uncertain: a move that sends nothing
  while the position is unknown leaves it unknown.
- A page focus change to the object already at the reading position makes
  no step, as after a focus request. One to a DOM node the checkpoints do
  not name makes the position unknown, saying so.
- A second say all, or a second selection command, while the position is
  already unknown by the same rule makes no step, as it changes nothing.
- At one time, a page load comes first, then a key, then a request, then a
  focus change.
- `setAccessibilityFocus`, `clearAccessibilityFocus`, and `doDefault` are
  not used: they follow a move or act on the object, and do not move the
  caret.

In the player:

- The Properties row "Reading position (inferred)", in the Screen reader
  group, gives the object as the Under the pointer (Chromium) row does, and
  "where within it is unknown", "focused", or why it is uncertain or
  unknown. Its change buttons and count step through every step.
- A third strip on the timeline, under NVDA's windows: a known object is
  solid purple (#6A4C93) with its role and name or the start of its text
  where they fit, an uncertain one purple stripes with the word
  "uncertain", and where the position is unknown, the strip is empty. White
  on #6A4C93 is 6.85 to 1. Clicking the strip moves the playhead there.
- Selected event values starts, for a record that moved the position or
  the key or click linked with it, with "Reading position after it",
  the rule, and the basis.

The playback index is at version 19: it keeps the page's focus records
whole.

Over the target run of 2026-10-12, the rules give 48 steps: 26 by rule 1,
6 by rule 2, 9 by rule 3, 1 by rule 4 (Find next with no match), 1 by
rule 5, 1 by rule 6, 1 by rule 8, and 3 by rule 9. The Tabs give the focus
steps on Home, Section two, and Far down, and after focus mode, Down moves
from the "Show message" button to the "Read more about the test" link.

### 4b as built

Agreed with the owner on 2026-10-12 and built the same day. The list of
steps is a tab, "Reading position steps", beside Browser navigation
correlation in the panel under the timeline, so the video and timeline
keep their size.

- Each step is a row at least 44 pixels high, with its time, what is known
  in words (Known, Same object, Uncertain, or Unknown), what moved it, and
  where it went, as the Properties row gives it. What moved it is the key
  and NVDA's command, such as "downArrow (next line)", the click, the key
  with "no NVDA command" where NVDA named none, "the page's focus moved",
  or "a page was loaded".
- A click on a step, or Enter or Space on it, selects the step's record:
  the request, the focus record, the key, or the page's navigation. That
  moves the playhead to it, and Selected event values gives the basis, as
  the owner chose, not a second line in each row.
- The step at the playhead is marked with a purple bar, bold text, and the
  words "at the playhead", as playback moves. The list never scrolls by
  itself, so rows do not move under a head pointer; "Show current step"
  scrolls to it. A line above the list says which step is at the playhead.
- The tabs and the button are at least 44 by 44 pixels.
- The tabs take the player's dark panels and light text, as its group
  boxes do: text #CDCCCA on the panel's #1C1B19 is 10.7 to 1, and on an
  unselected tab's #201F1D 10.3 to 1. The selected tab has a bar of
  #B79CE6 along its top (7.3 to 1 against the panel) and bold text; each
  tab's edge is #75736F (3.5 to 1 or more), and a focused tab's edge takes
  the system highlight colour. The first build used Windows' default white
  tab panel, which left the light text at a very low contrast, as the
  owner's screenshots of 2026-10-12 showed.
  The owner checked the fix on the target machine the same day.

Over the target run of 2026-10-12, every one of the 48 steps has a record
to select.

Checked on the target machine on 2026-10-12 with the five-step test of
selecting a step: the owner found it works functionally.

### 4c, the tree view, as built

Agreed with the owner on 2026-10-12 and built the same day, as a third tab,
"Accessibility tree", beside the other two, which tears out as they do
([player layout](player-layout.md), "Tabs that tear out").

What the target run of 2026-10-12 showed of the checkpoints: Chromium sent
the page's whole tree once, 79 objects at 2.98 s, then only the objects
that changed, mostly one at a time; the frame's tree, 11 objects, was first
sent at 204.77 s, when NVDA moved into the frame. The frame object is marked
`has_child_tree`, but which tree is its own is not recorded. Each frame's
navigation names its page (`context.pageId`), so a frame's document is tied
to its page by that.

- The tree is the page's, as Chromium last sent each object at or before
  the playhead (`AccessibilityTreeTimeline` in `Recorder.Session`), in its
  children's order. The browser's own pages are left out, as in the
  reading position's rules. A note above the tree says removals are not
  recorded, so an object may be shown after it was gone.
- With one frame object and one frame document, the frame's tree is nested
  under the frame object; before it was first sent, the frame object says
  "its document's tree is not yet in the recording". With several, the
  frame trees follow the page's, and each frame object says which is its
  own is not recorded.
- A child an object lists that no checkpoint has sent by the playhead is a
  row saying so; objects no recorded version lists as a child are listed
  after the page's tree, under a heading saying they may have been removed.
- Each row is at least 44 pixels high: the role and name, or the start of
  the text it holds, the node, and when its version was recorded, with
  "reading position" and "under the pointer" in words where they apply; the
  reading position's row also has a purple bar. An object with children
  has a 44 by 44 pixel expand button.
- When the tab is first shown for a recording, only the path to the
  reading position is expanded, or the root when the position is unknown,
  as the owner chose. The tree then updates as the playhead moves but never
  scrolls, expands, or collapses by itself; "Show reading position" and
  "Show under the pointer" expand the path to their object and scroll to
  it.
- Clicking an object, or Enter on it, shows its version's record in
  Selected event values and leaves the playhead where it is. As first
  built, the click moved the playhead to when the version was recorded, as
  the owner had chosen; on the target machine the owner found it sent the
  playhead back to 00:00:02.98 for most objects, whose only version is the
  page's first tree, so the owner chose on 2026-10-12 that it should not
  move. The row's "recorded at" time says when the version was sent.
- Detail text #4A4945 is 9.0 to 1 on white and 7.0 to 1 on the selected
  row's #E6E0F0; the reading position's bar, #6A4C93, is 6.9 to 1 on white.

Checked on the target machine on 2026-10-12, with the run of that day:
the tree, its frame, the path to the reading position, and a click
showing a row's record without moving the playhead all worked.

Found in building it: Chromium's inline text boxes have negative ids, such
as `child_ids=-1000000032,-1000000033`, which the reading of child lists did
not accept, so their parents seemed to list no children. Fixed for the tree
and for the Under the pointer row's parents.

## What NVDA had to announce

Agreed with the owner on 2026-10-12 and built the same day. For each
reading position step, the player works out what NVDA had to announce
there, by NVDA 2026.2's own rules, from the page's recorded tree and NVDA's
recorded settings. It is what NVDA had to announce, never what it said:
speech cut off by the next key, or not heard, is not known, and the
recording holds no speech to check it against (speech recognition is ruled
out). Each is labelled "inferred", and where the model does not cover a
case it says "not modelled yet" or "unknown" and why, rather than guess.

### What the owner chose

- Where it shows: both, as a second line on each row of Reading position
  steps, and as a fourth tab, "NVDA had to announce".
- Settings: record all the NVDA settings the rules need.
- Checking: NVDA's Speech Viewer may be turned on for the test runs only;
  the recorder neither needs it nor reads it.
- Scope: the first scope below.

### The first scope

- Tab and Shift+Tab, and the page's focus moving, in browse mode: NVDA's
  focus rule (reason FOCUS). In focus mode NVDA reports the object instead,
  which is "not modelled yet".
- Quick navigation by heading, heading at a level, link, unvisited and
  visited link, button, graphic, list, list item, landmark, and frame
  (reason QUICKNAV).
- Down and Up Arrow, next and previous line (reason CARET, by line).
- Any other command, a page load, or a key passed to the page is "not
  modelled yet"; after one, what NVDA last read (its container stack) is
  not known, and the next step says so.
- The roles covered: document, section, paragraph, text frame (strong and
  emphasis), landmarks and region, heading with its level, link, button,
  graphic, list and list item, frame, and status. A step within or holding
  an object of any other role, a table for one, is "unknown".

### NVDA's rules ported

- Which function speaks: on focus, `event_gainFocus` calls
  `speakTextInfo` with reason FOCUS for the focused object
  ([browseMode.py](https://raw.githubusercontent.com/nvaccess/nvda/release-2026.2/source/browseMode.py)); quick navigation reads the
  item with `TextInfoQuickNavItem.report`, by its first line for a list,
  landmark, or frame (same file); moving by line reads the line with
  `_caretMovementScriptHelper`
  ([cursorManager.py](https://raw.githubusercontent.com/nvaccess/nvda/release-2026.2/source/cursorManager.py)).
- What is said: `getTextInfoSpeech` and `getControlFieldSpeech`
  ([speech.py](https://raw.githubusercontent.com/nvaccess/nvda/release-2026.2/source/speech/speech.py)): the fields NVDA left are said
  first, by line only ("out of list"; a landmark is left silently); the
  fields it was already in are said again only where they are links,
  headings, buttons, or graphics; the fields entered are said; then the
  text with the fields within it. On focus and quick navigation a link's
  or heading's text comes before its role, and a named landmark says its
  name and role. A link's states come before its role. "blank" where
  nothing else is said.
- Which fields speak at all: the presentation categories of
  `getPresentationCategory` ([textInfos](https://raw.githubusercontent.com/nvaccess/nvda/release-2026.2/source/textInfos/__init__.py)),
  with the document formatting settings (a link with reportLinks off is
  silent, for one).
- The wording: role names ([role.py](https://raw.githubusercontent.com/nvaccess/nvda/release-2026.2/source/controlTypes/role.py)),
  "same page" and "visited" ([state.py](https://raw.githubusercontent.com/nvaccess/nvda/release-2026.2/source/controlTypes/state.py),
  [processAndLabelStates.py](https://raw.githubusercontent.com/nvaccess/nvda/release-2026.2/source/controlTypes/processAndLabelStates.py)),
  landmark names ([aria.py](https://raw.githubusercontent.com/nvaccess/nvda/release-2026.2/source/aria.py)), and "with N items".
  "Same page" is NVDA's `isSamePageURL`
  ([urlUtils.py](https://raw.githubusercontent.com/nvaccess/nvda/release-2026.2/source/utils/urlUtils.py)).
- The buffer: how NVDA's Chromium backend makes its browse mode text from
  the tree ([gecko_ia2.cpp](https://raw.githubusercontent.com/nvaccess/nvda/release-2026.2/nvdaHelper/vbufBackends/gecko_ia2/gecko_ia2.cpp),
  [gecko_ia2.py](https://raw.githubusercontent.com/nvaccess/nvda/release-2026.2/source/virtualBuffers/gecko_ia2.py)): a link's,
  button's, heading's, or graphic's name is its content; a name not from
  the content (an aria-label, a title) is always said, unless a visible
  label gives it; a block is decided by its display alone, and Chromium's
  serialization leaves out `display=block`
  ([ax_node_data.cc](https://raw.githubusercontent.com/chromium/chromium/main/ui/accessibility/ax_node_data.cc));
  a line is a block's inline run.
- The text is shown as the Speech Viewer joins the parts, with two spaces
  between them ([speechViewer.py](https://raw.githubusercontent.com/nvaccess/nvda/release-2026.2/source/speechViewer.py)), each part's
  spaces collapsed.

### NVDA's reporting settings

NVDA's settings record (`assistive-technology-settings`) gains 35
settings, each a member named by its key, as written in nvda.ini or null
where NVDA's default applies, with their defaults from NVDA's
configuration specification ([configSpec.py](https://raw.githubusercontent.com/nvaccess/nvda/release-2026.2/source/config/configSpec.py)):
from `[documentFormatting]`, what is reported of links, link types,
graphics, headings, lists, block quotes, groupings, landmarks, articles,
frames, figures, clickables, and tables, and the formatting settings; from
`[presentation]`, shortcut keys and descriptions; from `[virtualBuffers]`,
the maximum line length, screen layout, and say all on page load; and from
`[annotations]`, details and ARIA descriptions. Migration
`0032_nvda_reporting_settings.sql` adds their columns. Formatting reported
(fonts, colour, emphasis, and the rest) is not modelled, so where one is
on, each step says so and is uncertain.

A recording made before these settings were recorded, as every recording
before 2026-10-12, is worked out with NVDA's defaults, and every step says
so and is marked uncertain. This was the reading taken of the owner's "Yes"
to the question; showing "unknown" instead for such recordings is a one
line change if the owner prefers it.

### As built

- `NvdaAnnouncementTimeline` in `Recorder.Session`, built with the
  playback archive from the reading position's steps, the tree, NVDA's
  settings at each step, and the mode band. The steps now keep their NVDA
  command (`CommandNames`) and passed key (`KeyName`); the tree's objects
  keep Chromium's serialization and description.
- Each announcement is inferred, uncertain, not modelled, or unknown, with
  its basis. Uncertain where NVDA's settings are not recorded, where what
  NVDA last read is not known, where NVDA's mode at a focus change is not
  known, where a heading's level is taken from its tag (Chromium sends the
  level only in its extended mode,
  [ax_object.cc](https://raw.githubusercontent.com/chromium/chromium/main/third_party/blink/renderer/modules/accessibility/ax_object.cc)),
  where a name comes from another element whose visibility is not
  recorded, where an object has a description or a state the model does
  not cover, where a line is longer than NVDA's maximum line length, or
  where screen layout is off.
- Reading position steps: each row has a second line, "NVDA had to
  announce (inferred): ...", or "(inferred, uncertain)", or "not modelled
  yet" or "unknown" with why.
- "NVDA had to announce", a fourth tab, tears out as the others do: each
  step's time, what is known, what moved the reading position, and the
  text, in rows at least 44 pixels high. As in Reading position steps, the
  row at the playhead is marked but the list never scrolls by itself;
  "Show current step" scrolls to it, and a click, or Enter or Space, moves
  the playhead to the step and selects its record.
- Selected event values gives, for a step's record or its key, what NVDA
  had to announce and the basis, after the reading position.
- Not recorded, and so noted in each basis: whether an object has a click
  handler, for which NVDA would add "clickable" to an object that is not a
  link or button.

Worked out for the target run of 2026-10-12, with NVDA's defaults, for
example: Tab to the Home link, "banner landmark  Main  navigation
landmark  Home  same page  link"; quick navigation to the second heading,
"Section one  heading  level 2"; and Down Arrow onto the frame's heading,
"Same-site frame  frame  heading  level 2  Inside the frame".

Unit tests: `NvdaAnnouncementTests`, 11 tests: focus, quick navigation,
lines entering and leaving a list and landmarks, the recorded settings,
a recording without them, a command not modelled, a role not modelled,
NVDA's same page rule, and the settings record's validation.

Checked on the target machine on 2026-10-13, with recording
20261013-125012, made on a live site with NVDA's Speech Viewer on: three
quick navigations by heading, two Down Arrows, and a Tab. The recording
held NVDA's reporting settings, all at NVDA's defaults but say all on page
load, which was off. The owner watched the Speech Viewer while navigating
and found its content correct; no screenshot was taken, as one adds to the
Speech Viewer's content. The player's lines for those steps were:

- H: "main landmark  Bright futures start here!  heading  level 2"
  (uncertain, as NVDA had read the page on its own on loading);
- H: "News  heading  level 2";
- H: "This September, help champion guide dog access  heading  level 3";
- Down Arrow, twice: the paragraph's whole line of 200 characters, both
  marked uncertain, as NVDA splits a line longer than its maximum line
  length of 100 and which part it read is not modelled;
- Tab: "Read more about This September, help champion guide dog access
  link".

Not shown by this check: where NVDA split the long line, which the model
does not work out. Only one Tab is in the recording, at 25.73 s.

## The screen reader's windows

UI Automation already records them. At playback, NVDA's windows in front
are spans, with the element focused in each, as built in "2c-3, NVDA's
windows", and what each showed, read as its whole tree, as built in
"2c-4, what NVDA's windows show". Their closing is not shown: the
foreground records show only that another window came to the front.

## Settings and key commands

Assistive technology detection reads the screen reader's readable
configuration at the start of a recording, without changing it. For NVDA,
a portable copy keeps "all settings and add-ons in a directory called
userConfig, found in the NVDA directory", and an installed copy in the
user's profile
([NVDA user guide](https://download.nvaccess.org/releases/2026.2/documentation/userGuide.html)).
The values used here are the keyboard layout, the NVDA modifier keys,
the custom key commands, and the browse mode options.

The commands of each product and version are kept as data in the player,
with their source, starting with NVDA's documented browse mode commands
and the elements list. A kept key is labelled with its command, for
example "H: next heading", as an inference from the product, version,
layout, and custom commands recorded; with no match it is shown as a kept
key.

## Speech audio

Superseded in part on 2026-10-10: speech recognition is ruled out, and
the periods of NVDA's sound come from its own audio session's meter (see
"Making sound"). The rest stands as a record of the constraint.

The system sound already carries the screen reader's speech, mixed with
the page's sound. Capturing one process's sound needs
`AUDIOCLIENT_ACTIVATION_TYPE_PROCESS_LOOPBACK`, whose minimum supported
client is Windows 10 build 20348
([Microsoft](https://learn.microsoft.com/en-us/windows/win32/api/audioclientactivationparams/ne-audioclientactivationparams-audioclient_activation_type));
the target machine, build 19045, cannot. On such builds speech stays in
the system sound; where the build allows, the screen reader's sound is
recorded as its own channel.

## The player

A "Screen reader" lane on the timeline shows:

- each key, with its disposition, and for a kept key its inferred
  command and the basis;
- the screen reader's window episodes;
- an inferred mode band: where the screen reader keeps keys that would
  otherwise reach the page, browse mode; where they pass, focus mode; with
  the basis;
- gaps where the hook was not recording, and where it was not first in
  the chain (a screen reader started before the hook was reinstalled).

## Not in this design

- The screen reader's reading position. Browse mode navigation left no
  trace in the page's focus or selection. Requests the screen reader
  makes of Chromium, such as scrolling an element into view, all pass
  through `RenderAccessibilityImpl::PerformAction`
  (`content/renderer/accessibility/render_accessibility_impl.cc`, line 328
  in the Windows checkout); whether NVDA makes any during browse mode
  navigation is untested; step 3 tests it. With the recorded
  accessibility tree and the commands, they are the basis of step 4.
- Braille output.
- Screen readers other than NVDA, until each is tested.

## Questions to settle

- Whether to record `PerformAction` requests in the instrumented Chromium,
  after a test of what NVDA requests during browse mode navigation (step
  3).
- The join window, if the target machine shows longer delays than the
  validation.

The others were settled on 2026-10-10; see "Decisions of 2026-10-10".

## Required tests

- Unit tests of the `hook-keyboard` and `hook-installed` contracts and
  validator; of the join and the dispositions from generated sequences,
  including a kept physical Tab replaced by an injected one, keys in a
  screen reader window, and unjoined records; of hook loss detection; and
  of command labelling against the command data.
- Integration tests on Windows: the hook installs without administrator
  privileges, never blocks a key, and records the injected flags; the
  callback's duration stays far below the timeout under load.
- For NVDA's window snapshots: unit tests of the walk, its limits, the
  validator, the summary, outline, and compact form; an integration test
  on Windows against a dialog of standard Win32 controls
  (`tests/WindowSnapshotProbe`); and a target run with NVDA's Elements
  List, NVDA menu, and Exit NVDA dialog.
- For the mouse hook: unit tests of the contract, validator, loss check,
  NVDA's mouse settings, and the outcomes; an integration test on Windows
  that injects input (`tests/MouseHookProbe`); and a target run with the
  physical mouse and injected input, with NVDA ignoring injected mouse
  input off and then on.
- A system test on the target machine, extending
  `scripts/Test-ScreenReaderKeys.ps1`: every key of the script's hook is in
  the recorder's hook with the same disposition; NVDA started after the
  recording begins, and restarted during it, is followed by a reinstalled
  hook with no keys lost after it; keys to an elevated window are
  checked and the result recorded.
