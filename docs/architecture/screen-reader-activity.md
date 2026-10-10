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
  content under the pointer (the owner, 2026-10-10).

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

Built 2026-10-10 (step 2a), not yet run on the target machine. A
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
  keys; the payload rules; the summaries and key names. The integration
  and system tests below are still to run.

## Key disposition

Derived at playback from the records, with the rule versioned so that a
recording can be read again under a later rule.

- Join: a hook record joins a raw input record of the same key and
  direction within 300 ms, where an injected hook record joins only a raw
  input record with no device handle, and a physical one only a record
  with a device handle. A page `keydown` joins the raw input record it
  follows within the window used by the validation scripts.
- Kept: a physical key with a hook record and no raw input record. When a
  screen reader is running, this is attributed to it as an inference: the
  records do not name the process that kept the key.
- Passed: a key with a raw input record from a device; "received by the
  page" when a page `keydown` joins it.
- Injected: a hook record with the injected flag, or a raw input record
  with no device handle. Joined to the screen reader when it follows a
  kept key of the same virtual key within the window, as NVDA's Tab did;
  otherwise the injecting process is unknown.
- Unjoined records are shown as they are, with the reason.

## The screen reader's windows

UI Automation already records them. At playback, the records whose
process is a detected screen reader are grouped into episodes, such as
the elements list: the window opening and closing, the items focused and
their names, the text typed into its filter, and the button pressed. The
keys typed into such a window are passed keys, shown with the episode.

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
- A system test on the target machine, extending
  `scripts/Test-ScreenReaderKeys.ps1`: every key of the script's hook is in
  the recorder's hook with the same disposition; NVDA started after the
  recording begins, and restarted during it, is followed by a reinstalled
  hook with no keys lost after it; keys to an elevated window are
  checked and the result recorded.
