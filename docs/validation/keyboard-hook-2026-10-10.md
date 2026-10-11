# The keyboard hook on the target machine, 2026-10-10

The first run of step 2a, the recorder's own low-level keyboard hook (see
[screen reader activity](../architecture/screen-reader-activity.md), "The
keyboard hook"), on the target machine: Windows 10 build 19045, portable
NVDA 2026.2, the instrumented Chromium, package wr-876ab63.

Recording 20261010-211432-1b79ed73df014f84ba1f066b1f5edbc6, about 282 s.
The owner started the recording with NVDA not running, started NVDA, used
heading, landmark, and link navigation, Tab, and the elements list on a
page, filled in fields of a form, quit NVDA, started it again, used H and
Tab, and quit it. The optional step of typing into an elevated window was
not done: no elevated window is in the foreground records. It was run
next; see "The elevated window".

## Results

- Keys: the hook recorded 282 key events and raw input 136. Every raw input
  key had a hook call of the same scan code and direction within 300 ms:
  none was missed by the hook.
- Kept keys: H, D, and K in browse mode, NVDA+F7 (Insert held, with its
  repeats, then F7), and Tab were in the hook's records and not in raw
  input's. Shift was passed while D was kept (Shift+D).
- NVDA's Tab: four times, a physical Tab kept by NVDA was followed within
  2 ms by an injected Tab, down and up, with the injected flag, as in the
  test of 2026-10-07. Three times NVDA kept a physical Tab with no
  injected Tab after it; what NVDA did instead is for step 2b.
- Passed keys: arrow keys in the elements list, the letters, digits, Tab,
  Space, and Enter typed into the form, and keys while NVDA was not
  running were in both records.
- Installations: one at the start, one at each of NVDA's two starts
  (14.05 s and 236.51 s, the same times as NVDA's process records), and
  260 refreshes, one a second (median spacing 1.000 s) while NVDA ran and
  none while it did not. No installation failed and no loss was found.
- No key was recorded twice across the refreshes' overlaps, and no key was
  dropped.
- Callback time: the longest of any installation was 184 microseconds, the
  95th percentile of the installations' longest 16.5 microseconds, against
  Windows' timeout of at most 1000 ms.

## Not shown by this run

- How long after NVDA starts its hook is in place. Before a refresh puts
  the recorder's hook first again, a key NVDA keeps would be in neither
  record, so such a key leaves no trace. No key was pressed in the first
  seconds after either start, so the run neither shows nor rules out a
  missed key there; the refreshes bound the time to about a second.
- Whether a key NVDA passed was passed because NVDA was in focus mode:
  after the second start, H reached the page four times on the form
  before NVDA kept it again. That is for the mode band of step 2c.

## The elevated window

Recording 20261010-213058-530aff5c98704c93997622eececa42ff, package
wr-876ab63, about 142 s. The owner opened Windows PowerShell as
administrator from the search pane with the mouse (a right click and
Run as administrator), typed `hello` into it and cleared it with Escape,
pressed Tab three times in the browser, started NVDA, typed `hello` into
the same window again, pressed H three times in the browser, and quit
NVDA. The foreground records show the window as process 22444, titled
"Administrator: Windows PowerShell".

- While the elevated window was in the foreground, from 14.0 s to 28.4 s
  and from 98.7 s to 116.3 s, neither the hook nor raw input recorded a
  key, and raw input recorded no mouse input either: no movement, and not
  the button press that moved the foreground back to the browser (its
  release, at 116.5 s, was recorded). From 12.2 s to 14.0 s, the
  permission prompt's secure desktop, raw input recorded no mouse input.
- As both records lost the same keys, the loss check found no loss, which
  is right: the hook was not removed. Keys after each episode were
  recorded at once: the four Tabs passed, and the three H keys were kept
  by NVDA, at 117.9 s, 119.2 s, and 120.6 s.
- The hook was installed again every second while NVDA ran (67
  refreshes, the longest spacing 1.029 s); no installation failed; no key
  was dropped or recorded twice; the longest callback was 380
  microseconds.

The recorder runs without administrator rights, so input to a window of
an elevated process does not reach it. Nothing in the recording marks
these periods as times when input could not be recorded; only the
foreground window's title shows it.

## Marking when input could not be recorded

Recording 20261010-224236-a9813e7e610b40fca8513f4d4b9cd042, package
wr-51372a3, about 155 s: the elevated window steps again, with the
records of "Input the recorder cannot receive"
([screen reader activity](../architecture/screen-reader-activity.md)).

- The recorder recorded itself at medium integrity (8192), without
  UIAccess, and the user's desktop, `Default`, at the start.
- The elevated PowerShell (process 22444) was read as high integrity
  (12288), with no problem, and recorded as not recordable each time it
  came to the foreground: from 32.15 s to 48.90 s and from 102.14 s to
  119.17 s. Every other window was medium or low, and recordable,
  including NVDA's (medium, without UIAccess, as a portable copy) and the
  search pane and the shell's flyouts (low).
- In those periods the hook and raw input recorded no key and raw input
  no mouse input, as in the first run; the mouse release just before each
  period, on the taskbar, was recorded. Keys either side were recorded:
  three Tabs at about 50 s and five H keys from 126.9 s.
- The owner saw the band in the player: "the channels are greyed out when
  focus is in the elevated powershell window".
- The owner confirmed the properties panel's "Input recordable" row read
  "not recordable" only in those periods.
- The keyboard hook was installed again every second while NVDA ran (79
  refreshes, the longest spacing 1.015 s), with no loss, no failure, and
  no key dropped; the longest callback was 350 microseconds.

Not shown by this run: the secure desktop. The elevated PowerShell was the
one left open from the previous run, so no permission prompt appeared,
and the input desktop stayed `Default` throughout (one `input-desktop`
record, at the start).

## The key outcomes in the player

Recording 20261010-211432 opened in the player by the owner, with the key
outcomes of step 2b ([screen reader activity](../architecture/screen-reader-activity.md),
"Key disposition"), rule 1.

Package wr-61aee0c:

- 28.5 s, Tab before NVDA started: "passed", as expected.
- 33.5 s, the Tab NVDA kept: "kept, screen reader running: NVDA". NVDA's
  own Tab, 2 ms later, could not be selected: at the greatest zoom of the
  time, 32 times, the two were in one pixel column.
- 51.8 s, H: kept, as expected.
- 92.8 s, F7 (of Insert+F7), its release: "kept, screen reader running:
  NVDA", as expected.
- 181.4 s onwards, typing: "passed, received by the page", as expected.

Package wr-5d1dafb, after the owner's requirements of the same day (a kept
key names the injected key that followed it; zoom to 4,096 times; every
event at a clicked point listed in "Selected event values"); all three
confirmed by the owner:

1. A click at 33.5 s at the normal zoom listed the kept Tab, NVDA's Tab,
   and their raw input records together, the kept Tab ending "followed by
   an injected Tab, received by the page".
2. Zoomed in, the kept Tab and NVDA's Tab were separate markers, each
   selectable.
3. A click in the mouse lane where it was busy listed up to 50 events and
   said where there were more.
