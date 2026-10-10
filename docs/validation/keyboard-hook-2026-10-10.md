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
not done: no elevated window is in the foreground records.

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
- Keys to an elevated window.
- Whether a key NVDA passed was passed because NVDA was in focus mode:
  after the second start, H reached the page four times on the form
  before NVDA kept it again. That is for the mode band of step 2c.
