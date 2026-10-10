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
