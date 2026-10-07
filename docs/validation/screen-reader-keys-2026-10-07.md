# Screen Reader Keys: Validation, 2026-10-07

Tests which keys pressed while NVDA runs reach the recorder's raw input,
which reach the page, and what the recorder holds of NVDA's own windows.
Run by the owner with `scripts/Test-ScreenReaderKeys.ps1`; analysed from
the script's key log and the two recordings below.

## Setup

- Target machine: Windows 10 Pro 22H2, build 19045, one monitor.
- Screen reader: portable NVDA 2026.2 (`C:\Users\User\Desktop\NVDA`),
  started before the script, with the default NVDA key (Insert).
- Recorder: package wr-c9f2591, run from source.
- The script installs a low-level keyboard hook after NVDA's, so it is
  called before NVDA's, and registers for raw input as the recorder does
  (`RIDEV_INPUTSINK`). It blocks nothing.
- Key log: `C:\Users\Public\Downloads\screen-reader-keys-20261007-194638`.
- Recordings: 20261007-234745-98861df8873846528144d76ad2dc2ffa (a first
  attempt at the steps) and
  20261007-234903-90dc96c33b3041c199863bbfec54496b (the full steps). The
  keys typed before the first recording started (the fixture address) are
  in the key log only.

## Results

The full steps, from recording 20261007-234903, in seconds from the first
key of the log. "Script raw" and "recorder raw" are raw input from a real
keyboard device; "page" is a trusted `keydown` dispatched within 250 ms
before to 400 ms after the key.

| Time | Key | Script raw | Recorder raw | Page |
| --- | --- | --- | --- | --- |
| 151 to 166 | H three times, 2, D, K, B (browse mode) | no | no | no |
| 168 to 170 | Down Arrow three times (browse mode) | no | no | no |
| 175 to 176 | Insert+T | no | no | no |
| 180, 181 | Tab twice | no | no | see below |
| 185 to 186 | Insert+F7 | no | no | no |
| 190 to 203 | Down Arrow twice and Escape, in the elements list | yes | yes | no |
| 212 to 221 | E, A, B, C | yes | yes | yes |

- Every key NVDA treats as a command is missing from raw input, the
  script's as well as the recorder's, and is present only in the
  low-level hook. This matches the published measurement that a key
  blocked in a low-level hook loses its raw input
  ([openMouse](https://github.com/alstonmendonca/openMouse)), on Windows 10
  here; it is not a defect of the recorder's collector.
- Each physical Tab was kept by NVDA, and NVDA injected a Tab in its
  place at the same instant (the hook's injected flag set). The injected
  Tab is in raw input with device handle 0 where physical keys have a
  device handle (65601 here), and the page received it: a trusted
  `keydown`, then focus moved to node 86 and then node 108.
- Keys typed into NVDA's elements list reach raw input; it is an
  ordinary window.
- E, A, B, and C were passed to the page and typed into the text field
  (node 134, selection offsets 1 to 4), so NVDA was passing keys through at
  that point. Enter was not pressed. What put the field in focus is not
  shown by the records analysed here.
- From the first quick-navigation key (151 s) to Insert+T (176 s), the
  page recorded no focus change and no selection change: browse mode
  navigation left no trace in the page's focus or selection.
- UI Automation recorded NVDA's elements list: the window opening and
  closing, and focus on its tree item "Third link; same page". In the
  first attempt it also recorded focus on the dialog's "Filter by:" edit
  field and its "Activate" button.

## What this establishes

- Raw input cannot record screen-reader commands. With NVDA, the
  quick-navigation keys, the browse mode arrows, and NVDA+key commands are
  absent from the recorder's keyboard evidence as built.
- A low-level keyboard hook installed after the screen reader's sees
  them, without blocking them.
- A raw input record with no device handle is injected input; here it was
  NVDA's own Tab.
- A screen reader's own windows are recorded by UI Automation.

## Limits

- One machine, Windows 10, NVDA 2026.2 portable with its default
  settings; JAWS and Narrator are not tested.
- Only the order "screen reader first, hook second" was tested.
  `SetWindowsHookEx` "always installs a hook procedure at the beginning of
  a hook chain" ([Microsoft, about hooks](https://learn.microsoft.com/en-us/windows/win32/winmsg/about-hooks)),
  so a hook installed before the screen reader's is called after it, and would not
  see the keys the screen reader keeps; restarting the screen reader
  during a recording changes the order.
- The "page" column pairs keys and dispatches by time only: the
  dispatches at 180 and 181 s are of NVDA's injected Tabs, though the
  script's table pairs them with the physical Tabs.
