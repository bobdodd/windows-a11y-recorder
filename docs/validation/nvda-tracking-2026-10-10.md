# Tracking NVDA: Validation, 2026-10-10

The first run on the target machine of the assistive technology
collector, as built in
[screen reader activity](../architecture/screen-reader-activity.md),
"Tracking NVDA". Recorded by the owner; analysed from the recording's
records and its two audio files.

## Setup

- Target machine: Windows 10 Pro 22H2, build 19045. Output device
  "Speakers (Realtek(R) Audio)"; microphone "Microphone (Razer Seiren
  Mini)".
- Screen reader: portable NVDA 2026.2 (`C:\Users\User\Desktop\NVDA`),
  started after the recording began and quit before it ended.
- Recorder: package wr-142d2fb, run from source, with the instrumented
  Chromium.
- Recording: 20261010-195245-0671e59cf2a74cf79aee5cd6093884f7, started
  19:52:45.562 UTC, 105.5 s long. The owner browsed a page with NVDA
  speaking and talked over it.

## Results

Times are seconds from the start of the recording.

| What | Recorded | Against |
|---|---|---|
| NVDA started | Windows start 19:53:04.962 UTC, 19.40 s; record at 19.66 s | Within one 250 ms poll; the time held is Windows' own |
| Version and copy | 2026.2.0.57664, portable | The copy on the desktop |
| Helper | `lib\2026.2\x86\nvdaHelperRemoteLoader.exe`, child of NVDA | From NVDA's folder |
| In the browser | `lib\2026.2\x64\nvdaHelperRemote.dll` in Chromium process 11004 at 29.06 s | Chromium came to the foreground at 29.10 s |
| NVDA exited | Windows exit 19:54:26.215 UTC, 100.65 s; exit code 0; record at 100.68 s | The helper exited 4 ms later |
| Module gone | 101.22 s, the browser still running | After NVDA's exit |

Sound, compared with the recording's system sound and microphone files on
a 20 ms grid (a bin is sound when its largest sample reaches 0.001, as the
collector's threshold; the microphone's speech when it reaches 0.1):

- 23 periods of sound, all from NVDA's main process.
- 96% of the bins with system sound were in NVDA's periods. The page made
  no sound of its own.
- The owner's speech with NVDA silent (37.4 to 43.5 s, and 87.2 s) started
  no period. Speech over NVDA (about 64 to 66 s, 75.8 to 76.2 s, and 81 to
  83.6 s) left the periods following NVDA's sound.
- Where NVDA spoke after silence, its period started 10 to 40 ms after the
  system sound (for example 46.06 s against 46.02 s, and 79.88 s against
  79.86 s).

So on this machine, with this version and output, the per-process meter
separates NVDA's sound from the microphone, as was assumed.

## Defects found and fixed

- NVDA's first sound was partly missed: its startup sound began at
  22.24 s, and its period at 22.96 s. The audio sessions were listed every
  second, and NVDA's session, opened by that sound, was not listed until
  then. Fixed: the sessions are listed every 100 ms while a running NVDA
  has no session, and every second once it has.
- A file without a version resource was recorded with `fileVersion` and
  `productVersion` as empty strings (the helper and the module). Fixed:
  recorded as null. The payload rules still accept the empty strings of
  this recording.

Both fixes need a short run: NVDA started during a recording, with its
startup sound's period starting with the system sound.

## The run after the fixes

Recording 20261010-202006-12fce23c0acd40aa90883283017c3602, with package
wr-e16347c: NVDA started during the recording, spoke, and was quit. The
recording has no audio files, as audio was not captured, so the check
against the system sound could not be made.

- Versions: the helper's and the module's `fileVersion` and
  `productVersion` are null. Fixed.
- Times: NVDA started at 11.65 s by Windows' clock (record at 11.68 s),
  its helper at 12.90 s (record at 12.99 s), and exited at 33.80 s (record
  at 33.96 s). The module was in the browser from 13.47 s to 33.82 s.
- First sound: its period started at 13.05 s, 0.06 s after the helper's
  record. In the first run it started 0.61 s after the helper's record,
  and 0.72 s after the system sound. This points to the fix working, but
  without the system sound it is not confirmed. The next run confirms it.

## The run with audio, after the fixes

Recording 20261010-202211-725404eb32ee45e0917f509b8e24673c, with package
wr-e16347c and audio captured: NVDA started during the recording, spoke,
and was quit.

- NVDA started at 8.46 s by Windows' clock (record at 8.69 s), its helper
  at 9.84 s, and both exited at 39.21 s (records at 39.40 s). Versions of
  files without one are null.
- The system sound, on the same 20 ms grid and threshold as above, had 8
  periods, and NVDA had 8, one for each.
- The startup sound began at 9.86 s in the system sound, and its period at
  9.96 s: 0.10 s late, against 0.72 s in the first run, and within the
  100 ms listing interval and one 20 ms sample. Every later period started
  within 15 to 40 ms of the system sound.

The fix for the first sound is confirmed. Up to about 0.1 s of a new
session's first sound can still be missed.
