# Android recording: feasibility checks

Written 2026-11-07 for the four checks in
[Recording an Android device](../architecture/android-recording.md),
before anything is built. Each test checks one behaviour in at most five
steps, with what to see on the screen.

The checks use one script, `scripts/Test-AndroidFeasibility.ps1`. It
downloads scrcpy 5.0.1 for Windows, which includes adb, from
[the scrcpy release](https://github.com/Genymobile/scrcpy/releases/tag/v5.0.1),
checks the file's SHA-256 against the published one, and keeps it in
`artifacts\android-checks\tools` in the repository. Everything the checks
write goes to `artifacts\android-checks`, which git ignores. Nothing is
installed on the phone, and the script changes no setting on it.

What was checked before this plan: the script was run in the Linux build
environment with PowerShell 7.4 against a stand-in for adb and scrcpy
that returns sample output, to check its parsing and printing. It has
not yet been run against a phone, or in Windows PowerShell 5.1.

## Before the tests

On the phone:

1. Settings, About phone: tap "Build number" 7 times, until "You are now
   a developer" appears.
2. Settings, System, Developer options: turn on "USB debugging".
3. Connect the phone to the PC with a USB cable that carries data.
4. On the phone, in "Allow USB debugging?", tick "Always allow from this
   computer" and choose "Allow". If the question does not appear, run test
   1 once; adb asks for it.

On the PC, open PowerShell (a normal window, not "Run as administrator")
and get the script:

```powershell
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass -Force
$repo = "C:\Users\User\windows-a11y-recorder"
git -C $repo pull --ff-only
$check = Join-Path $repo "scripts\Test-AndroidFeasibility.ps1"
```

Each test below runs one command in that window. If the window is
closed, run the four lines again first.

## Test 1: the phone is connected

1. Unlock the phone.
2. Run `& $check -Check Connect`
3. Read the window.

**On screen:** the first time, "Downloading scrcpy-win64-v5.0.1.zip from
GitHub..." and "scrcpy 5.0.1 is in ...". Then "Model: Pixel 10 Pro XL",
the Android version and API level, the build, security patch, screen
size, the TalkBack version with "on" or "off", and last "PASSED: the
phone is connected and authorized." If it reads "adb sees no phone",
install the
[Google USB Driver](https://developer.android.com/studio/run/win-usb) and
run it again. Send the whole output.

## Test 2: the phone's whole sound (`output`)

1. Turn on TalkBack (Settings, Accessibility, TalkBack).
2. Run `& $check -Check Sound -Source output`
3. Follow the prompts: keep still until about 7 seconds, then swipe right
   3 times, slowly, until about 15 seconds.
4. Read the chart.

**On screen:** "Sound from 'output': " with about 20 seconds, 48000 Hz
and 2 channels", then a line per half second with a level and a bar of `#`.
Before about 7 seconds, the levels are near 0% with no bar. After it,
several groups of bars, each a few half seconds long, one group for
each thing TalkBack said. The last line gives how many half seconds were
above 2%. While this runs the phone is silent, and its sound plays on
the PC instead; that is how `output` works. Send the chart.

## Test 3: the phone's sound with duplication (`playback`)

1. Keep TalkBack on.
2. Run `& $check -Check Sound -Source playback`
3. Follow the prompts as in test 2.
4. Read the chart.

**On screen:** the same layout as test 2, with "Sound from 'playback'".
The phone keeps its sound this time. If TalkBack is captured, the
groups of bars after 7 seconds look like test 2's. If the chart stays
near 0% throughout, TalkBack is not captured by `playback`. Send the
chart.

## Test 4: TalkBack's text is in the video

1. In TalkBack's settings, open Advanced settings, then Developer
   settings, and turn on "Display speech output".
2. Run `& $check -Check SpeechText`
3. Follow the prompts: swipe right 3 times, slowly, from about 4
   seconds.
4. When the recording opens, play it.

**On screen:** while it runs, a window titled "Speech text check" shows
the phone, with TalkBack's text at the bottom of the phone's screen, the
same text in the window. After 20 seconds the window closes and the MP4
opens in the PC's video player, where TalkBack's text appears at the
bottom of the picture after each swipe. Send a screenshot of one frame
with the text, or describe what is missing.

## Test 5: TalkBack's text is in its log

1. In TalkBack's Developer settings, set "Log output level" to
   "Verbose".
2. Run `& $check -Check Log`
3. Swipe right 3 times, slowly, before "Stop." appears.
4. Type one word that TalkBack showed on the phone (for example from
   "Network & internet") and press Enter.

**On screen:** "TalkBack (process ...) wrote ... log lines; the whole
log has ...", then "TalkBack's lines containing '...': " with a count,
and up to 15 of those lines, each starting with a time in seconds. A
count of 0 means the log does not hold the spoken text. Send the output.
Google's issue tracker warns that TalkBack's verbose log can include
what is on screen and typed
([Google Issue Tracker](https://issuetracker.google.com/issues/517413745)),
so set "Log output level" back afterwards if you prefer.

## Test 6: touches are seen

1. Keep TalkBack on.
2. Run `& $check -Check Touch`
3. When "For 12 seconds" appears, tap once near the top of the screen,
   once in the middle and once near the bottom.
4. Read the window after 12 seconds.

**On screen:** "Touch screen: /dev/input/event..." with its name and
ranges, then "Touches: 3", and 3 lines "at ... s: x ..., y ... pixels"
with y increasing from near the top to near the bottom. A line beginning
"FAILED: getevent was refused" means touches cannot be read without
root. Send the output.

## After the checks

Tests 2 and 3 answer question 1 of the proposal, which sound source the
recorder uses. Tests 4 to 6 show which of TalkBack's text, its log and
the touches the recorder can rely on. The design is updated with the
results before any building starts.
