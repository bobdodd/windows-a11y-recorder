# Recording an Android device

Proposed 2026-11-07. All five questions decided and all four checks run
on 2026-11-07 (see [Decisions](#decisions-of-2026-11-07) and
[the feasibility checks](../validation/android-feasibility-plan.md)).
Not built. The first version's build is planned in
[the implementation plan](android-implementation-plan.md).

## Purpose

So far every recording is of the Windows PC. The owner also needs to
record apps and websites on mobile devices. iOS is set aside for now. This
design covers Android, with the owner's Pixel 10 Pro XL as the test device.
The current Pixel release is Android 17, and Google's October 2026 update
lists the Pixel 10 Pro XL
([Google Pixel Community](https://support.google.com/pixelphone/thread/471669543)).
The phone's actual version is read in the first check below.

## Shape: the phone connected to the PC recorder

The recorder stays a Windows app. The phone is connected to the PC by USB,
and the recorder records it on the same session clock and in the same
recording format as the desktop. The phone needs Developer options and USB
debugging turned on, and one small companion app (see "Accessibility tree
and events").

A recorder that ran on the phone alone was considered and rejected. The
most useful sources need the privileges of the Android Debug Bridge (adb)
shell, which an ordinary app does not have:

- An app's own sound capture can take only media, game and unknown sound,
  and "all other usages CAN NOT be captured"
  ([Android, AudioPlaybackCaptureConfiguration](https://developer.android.com/reference/kotlin/android/media/AudioPlaybackCaptureConfiguration)).
  A screen reader's speech is a different usage
  ([Android NDK, audio usages](https://developer.android.com/ndk/reference/group/audio)),
  so an app on the phone cannot record TalkBack.
- Raw touch events can be read from an adb shell without root, because adb
  gives its shell the input group "to diagnose input issues (getevent)"
  ([adb source](https://github.com/mirror/platform_packages_modules_adb/blob/1cf2f017/daemon/main.cpp)).
  An app has no such access.

On Windows the recorder needs Google's adb, from SDK Platform-Tools
([Android](https://developer.android.com/tools/releases/platform-tools)),
and "the Google USB Driver is required to perform adb debugging on Windows
with Google devices"
([Android](https://developer.android.com/studio/run/win-usb)).

Wireless debugging is also possible, from Android 11, with the PC and phone
on the same network and a one-time pairing
([Android, adb](https://developer.android.com/tools/adb)). It frees the
participant from the cable, at the cost of video bandwidth (question 3).

## What is recorded

### Screen

- The recorder uses the server half of scrcpy, an Apache 2.0 project that
  mirrors and records Android devices
  ([scrcpy](https://github.com/Genymobile/scrcpy/blob/master/LICENSE)). The
  recorder pushes the server to the phone, starts it as the adb shell
  user, and reads its video socket. The server fails unless the client and
  server versions match exactly
  ([scrcpy, develop.md](https://github.com/Genymobile/scrcpy/blob/master/doc/develop.md)),
  so the installer ships one pinned server version (5.0.1 is current) and
  the recorder implements the client side.
- The phone encodes the picture as H.264 or H.265. Each packet carries a
  12-byte header with its presentation time, and a session packet marks
  each change of size, such as a rotation
  ([scrcpy, develop.md](https://github.com/Genymobile/scrcpy/blob/master/doc/develop.md)).
  "Timestamps are captured on the device"
  ([scrcpy, recording.md](https://github.com/Genymobile/scrcpy/blob/master/doc/recording.md)).
  In the scrcpy source (commit `a60891a`, 2026-10-08), the desktop client
  subtracts the first timestamp before writing a file
  (`app/src/recorder.c`, `pts_origin`), but the server sends the
  encoder's own times (`Streamer.writePacket`). The recorder keeps those
  device times so it can put each frame on the session clock (see
  "Time").
- The recording stores the encoded stream as received, with one event per
  packet. Still frames for the player and the analysis are taken from it.
  Unlike the desktop's PNG frames, this picture has been compressed on the
  phone, and the recording says so.
- A window marked secure is kept "from appearing in screenshots or from
  being viewed on non-secure displays"
  ([Android, FLAG_SECURE](https://developer.android.com/reference/kotlin/android/view/WindowManager.LayoutParams)).
  Google suggests it for "banking apps or password managers"
  ([Android, fraud prevention](https://developer.android.com/security/fraud-prevention/activities)).
  Such windows are expected to record as blank.

### Device sound

The scrcpy server offers two ways to capture the phone's sound
([scrcpy, audio.md](https://github.com/Genymobile/scrcpy/blob/master/doc/audio.md)):

- `output`, the default, "forwards the whole audio output, and disables
  playback on the device". The participant then hears the phone through
  the PC.
- `playback` (Android 13 and later) can keep the sound playing on the phone
  as well (`--audio-dup`), but "Android apps can opt-out, so the whole
  output is not necessarily captured".

Neither page says whether TalkBack's speech is included. Check 1 answered
it on the owner's Pixel 10 Pro XL (Android 17, TalkBack 17.1) on
2026-11-07; see "Results of check 1" below. `output` captured sound after
each TalkBack swipe and exact silence otherwise. `playback` with
`--audio-dup` captured nothing: every sample of a 20-second file was 0.
That matches Android's rule that playback capture takes only players whose
usage is `USAGE_MEDIA`, `USAGE_GAME` or `USAGE_UNKNOWN`
([Android Developers](https://developer.android.com/media/platform/av-capture)).
That TalkBack's speech has an accessibility usage is inference.

#### Results of check 1

| Run | Phone volume | Loudest half second | Sound present |
|---|---|---|---|
| `output` | as found | 2.9% of full scale | after the swipes, and once at 4.5 s |
| `output` | raised to maximum before the run | 2.9% | after the swipes |
| `playback`, `--audio-dup` | as found | 0.0% | none |
| `output` | Accessibility volume about 80%, set during the run | 32.0% | after the swipes |

The level of `output` follows the phone's volume as set while the capture
runs, not the volume set before it: raising both sliders to the maximum
beforehand changed nothing, while setting Accessibility volume to about
80% during the run raised the loudest moment about elevenfold. This
matches scrcpy issue 3790, where the capture volume follows the device
volume and pressing the volume keys during capture resets it to the
device volume
([scrcpy issue 3790](https://github.com/Genymobile/scrcpy/issues/3790#issuecomment-1472079314)).
The owner found about 80% Accessibility volume needed for a usable
recording. The recorder therefore needs to show the phone's sound level
while recording and tell the tester to raise the phone's volume during
the session when it is too low; whether it can read or set that volume
over adb is not yet checked.

Sound packets also carry device times: the server takes them from
Android's audio timestamp (`AudioRecordReader`, `timestamp.nanoTime`).

### Microphone

The PC's microphone is recorded as now. The phone's microphone is another
scrcpy source (`mic`), and could be added later.

### What TalkBack said

- TalkBack's developer settings have "Display speech output" to "view
  TalkBack speech output on the screen", and a log level of VERBOSE
  ([Android, testing](https://developer.android.com/guide/topics/ui/accessibility/testing)).
  With the first turned on, TalkBack's words are in the video as text.
  Check 2 confirmed on 2026-11-07 that they reach the recorded picture.
  Only the latest text shows, so a later utterance such as a usage hint
  replaces the name it followed, and the text covers the bottom of the
  screen.
- With verbose logging, TalkBack writes to the system log, which the
  recorder can read over adb. Google's own bug template warns that
  "TalkBack logs exposes the contents of your screen to your device's
  logs"
  ([Google Issue Tracker](https://issuetracker.google.com/issues/517413745)).
  Check 3 on 2026-11-07 (TalkBack 17.1, log level Verbose) found the
  spoken text in TalkBack's own log lines, with CLOCK_MONOTONIC times
  from `logcat -v monotonic`. For three swipes on the Home screen the
  log held, for each item:
  - the focus event and the text chosen for it, for example
    `viewAccessibilityFocused: ... ttsOutput={Speedtest}`;
  - `SpeechControllerImpl: Speaking fragment text="Speedtest",
    utteranceId=talkback_17, ... event=type:EVENT_TYPE_ACCESSIBILITY
    subtype:TYPE_VIEW_ACCESSIBILITY_FOCUSED ... time:145986290`, the
    full text with an utterance number and the time of the event that
    caused it;
  - the usage hint as a separate utterance, `Speaking fragment
    text="Double-tap to activate. ..."`;
  - `clearCurrentAndQueuedUtterancesThatDontIgnoreInterrupts:
    interrupting current item: {utteranceId:"talkback_16", ...}` when the
    next swipe cut an utterance off;
  - `FeedbackFragmentsIterator: onFragmentRangeStarted , speak word =
    Double`, one line per word as the speech engine reached it.

  So TalkBack's speech can be recorded as text, with when each utterance
  started, which event caused it, which words were reached and when it
  was interrupted, which NVDA does not allow. These are TalkBack's
  internal log lines, not a published interface, so their wording can
  change between TalkBack versions; the recorder keeps the raw lines and
  parses them as a separate, versioned step. That the word lines match
  when the words were heard is inference: they come from the speech
  engine's progress, not from the sound.
- Since step 6a of the implementation plan (2026-11-10), the recorder
  runs `logcat` for the whole recording, keeping only lines with the tag
  `talkback`, which check 3 found on 1,189 of the 1,196 lines of
  TalkBack's process. The tag is matched by the recorder, because on the
  Pixel with Android 17 logcat's own tag filter printed none of those
  lines (test 52, 2026-11-10). Every line is kept on
  `screen-reader.talkback.log`, and a versioned reader makes
  `screen-reader.talkback.speech` from them: each utterance started,
  each later fragment, each word reached and each utterance
  interrupted. The status line says when no speech lines have arrived,
  so a tester who forgot to set TalkBack's log output level to Verbose
  can fix it at once, and the status at Stop reminds the tester that the
  level is still Verbose. The player shows what TalkBack said in the
  Screen reader lane's place, with its filter named "TalkBack said", and
  in a strip under it with each utterance's text, interrupted ones
  striped and marked; the properties panel gives the latest utterance
  and the words the speech engine reached in it. The log gives no end
  for speech, so an utterance's span runs to the next one or to its
  interruption. The details are in the implementation plan, slice 6.
- Like the NVDA design ([screen reader activity](screen-reader-activity.md)),
  what TalkBack had to announce can also be inferred from the
  accessibility events below. That is later work, and is kept apart from
  what was recorded.
- Whether TalkBack is running, and whether explore by touch is on, are
  recorded from the settings (see "Settings").

### Accessibility tree and events

- A companion app, installed by the recorder, provides an accessibility
  service. "All accessibility services are notified of all events they
  have requested"
  ([Android, AccessibilityService](https://developer.android.com/reference/android/accessibilityservice/AccessibilityService)),
  so it runs alongside TalkBack and sees the same events. With
  `canRetrieveWindowContent` and `FLAG_RETRIEVE_INTERACTIVE_WINDOWS` it
  can read every window's tree (same page). This plays the part UI
  Automation plays on Windows.
- It records every event, including accessibility focus, input focus,
  window changes, content changes and the system's touch start and end
  ([Android, AccessibilityEvent](https://developer.android.com/reference/android/view/accessibility/AccessibilityEvent)).
  Announcements are recorded when they arrive, but that event type is
  deprecated from API level 36 and "accessibility services may choose to
  ignore dispatch of this event type" (same page), so newer apps may use
  other means.
- Since step 7a of the implementation plan (2026-11-10), the recorder
  asks the companion app for every event as the recording starts, and
  stores each on `accessibility.android.events` with every field it
  carries, its extra records and the item it came from. An event's time
  is set by Android, as the app sends it, from the clock that
  `System.nanoTime()` is based on, in milliseconds, so the event is
  placed on the session clock through the companion app's clock fit.
  The status line counts the events, content changes, accessibility
  focus events and window changes, and gives the most content changes in
  one second, the item last given accessibility focus, and the time the
  app takes to read each event. The details are in the implementation
  plan, slice 7.
- It takes a tree snapshot at the start, on each window change and on
  content changes. How often content changes arrive is measured before
  any limit is set, so nothing small is missed by design.
- It only observes. It performs no actions. It does not request explore
  by touch, the mode "in which touched items are spoken aloud"
  ([Android, AccessibilityServiceInfo](https://developer.android.com/reference/android/accessibilityservice/AccessibilityServiceInfo)),
  because that would change the phone for a participant who does not use
  TalkBack.
- Since Android 13, accessibility services in apps opened from a file
  manager or browser are blocked as "restricted settings", while
  installers that use Android's session API are exempt
  ([Esper](https://www.esper.io/blog/android-13-sideloading-restriction-harder-malware-abuse-accessibility-apis)).
  Developers report that `adb install` avoids the block
  ([BlockReels](https://github.com/mtpiz/BlockReels/blob/claude/appblock-android-research-drd2wr/README.md)).
  This is confirmed on the Pixel when the app is first built.
- The recorder offers to turn the service on over adb, adding it to the
  enabled services so TalkBack stays on. The participant can see it in
  Settings, Accessibility, and turn it off there.
- Views an app marks as sensitive "can be accessed only by accessibility
  services that declare `android:isAccessibilityTool="true"`", from API
  level 34
  ([Android, View](https://developer.android.com/reference/android/view/View#setAccessibilityDataSensitive(int))).
  In Android 17, Advanced Protection limits accessibility services to
  verified accessibility tools
  ([Google](https://blog.google/security/android-advanced-protection-updates/)).
  Whether to declare the flag is question 2.

### Touches

- The recorder runs `getevent -lt` in an adb shell. This gives every raw
  touch event, with timestamps "in the CLOCK_MONOTONIC timebase"
  ([Android, getevent](https://source.android.com/docs/core/interaction/input/getevent)).
  This is the phone's equivalent of the mouse hook. It sees touches before
  TalkBack interprets them, so a TalkBack gesture is recorded as the
  fingers' movement.
- Positions are in the touch panel's units. They are converted to screen
  pixels from each axis's range, which `getevent -lp` reports
  ([Bytescope](https://bytescope.dev/en/web-adb/commands/getevent/)).
- Check 4 confirmed this on 2026-11-07 without root and with TalkBack on:
  three taps at the top, middle and bottom were read as x 603, y 171;
  x 654, y 1188; and x 613, y 2155 on a screen of 1080 by 2404 pixels.
  The panel reports in finer units than pixels (an x of 7510 in the raw
  events), so the conversion is needed. `getevent` does not flush its
  output when read through a pipe, so the recorder reads it through a
  terminal on the phone (`adb shell -tt`) or must otherwise keep lines
  from being held back.
- The touch times (about 146,202 s) and the TalkBack log times from
  `logcat -v monotonic` (about 145,977 s, four minutes earlier) are on
  the same scale, which fits both being CLOCK_MONOTONIC. That is
  inference until the one-timeline check below.
- The same stream gives the hardware keys, such as volume and power. Text
  typed on the on-screen keyboard is not in it. Text changes come from the
  accessibility events.
- Since step 5a of the implementation plan (2026-11-10), the recorder
  keeps every line on `input.android.raw`, each report of the touch
  screen on `input.android.touch` with each finger's position in screen
  pixels, turned by the display's rotation, and each hardware key on
  `input.android.key`. The touch screen is the device with both position
  axes and `INPUT_PROP_DIRECT`, which marks a device whose positions are
  on the screen
  ([Linux, input event codes](https://www.kernel.org/doc/html/latest/input/event-codes.html)),
  and fingers are followed by the multi-touch protocol B's slots
  ([Linux, multi-touch protocol](https://www.kernel.org/doc/html/latest/input/multi-touch-protocol.html)).
  The details are in the implementation plan, slice 5.
- Since step 5b (2026-11-10), the player draws each finger on the
  phone's picture while it is down: a yellow circle over a black one,
  numbered from 1 in the order the fingers went down when there is more
  than one, with its path over the last second. The touches and the
  hardware keys have the mouse's and the keyboard's lanes and filters,
  named "Touches" and "Phone keys". The frames are not changed, and an
  MP4 export does not draw the touches. A circle is about a fingertip
  across on the phone's picture, and no smaller than 24
  device-independent pixels across on the PC's screen, so it stays
  visible when the picture is shown small.

### Settings

At the start and on each change, the recording holds the phone's
accessibility-related settings, in the same form as the Windows ones
([accessibility preferences](accessibility-preferences.md)). Examples are
the enabled accessibility services, TalkBack, explore by touch,
magnification, color inversion, color correction, font size and display
size. TalkBack is off in many recordings: the owner also tests
magnification, visual layout and colour, without it (2026-11-08). The companion app watches each
setting through a content observer
([Android, Settings.Secure](https://developer.android.com/reference/android/provider/Settings.Secure)),
and the recorder reads the full list over adb at the start.

### Websites

- Chrome on Android exposes the Chrome DevTools Protocol over USB. adb
  forwards it to a PC port (`adb forward tcp:9222
  localabstract:chrome_devtools_remote`), where `/json` lists the pages
  ([Chrome](https://developer.chrome.com/docs/devtools/remote-debugging)).
  This gives the page's structure, its accessibility tree, navigation and
  network events.
- That is less than the instrumented Chromium records on Windows. Building
  the instrumented Chromium for Android needs "an x86-64 machine running
  Linux"
  ([Chromium](https://chromium.googlesource.com/chromium/src/+/main/docs/android_build_instructions.md)),
  and is left for later.
- Web content inside an app can be inspected only if the app enables it
  ([Chrome, WebViews](https://developer.chrome.com/docs/devtools/remote-debugging/webviews/)).
  Otherwise it is recorded through the accessibility tree, like the rest
  of the app.

## Time

The phone has its own clocks. The video and sound times come from the
phone, `getevent` uses CLOCK_MONOTONIC, and the reference does not say
which clock an accessibility event's time uses ("Gets the time in which
this event was sent",
[Android, AccessibilityEvent](https://developer.android.com/reference/android/view/accessibility/AccessibilityEvent)).

As the collector contracts require, each phone clock is registered as its
own clock domain with mapping samples
([collector contracts](collector-contracts.md), "Timestamp contract").
The recorder asks the companion app for its clocks over an adb socket
several times a second. It takes the midpoint of each exchange, with half
the round trip as the error bound, and records drift over the session.
When the app is first built, a check confirms that touches, accessibility
events, video and sound fall on one timeline on the Pixel.

## Recorder, player and installer

- **Recorder.** A recording can target "Android device". Before start it
  shows, from its checks: the phone found and authorized, its model and
  Android version, the companion app installed and on, TalkBack on or
  off, and a reminder to turn on "Display speech output", which the
  recorder cannot read (slice 1 of the implementation plan). Each source
  reports its capability as the collector contracts require.
- **Player.** The phone's picture takes the desktop's place, with touches
  drawn as the pointer is now, and the TalkBack and accessibility events
  in their own lanes. MP4 export treats the phone's sound as the system
  sound.
- **Installer.** It bundles the pinned scrcpy server and the companion
  app. Google's adb is not bundled: the Android SDK licence does not allow
  redistributing the SDK or any part of it
  ([Android, SDK terms 3.4](https://developer.android.com/studio/terms)).
  The recorder downloads it from Google in an Android setup window, as
  model setup downloads models. The USB driver was not needed on the
  owner's PC. See [the implementation plan](android-implementation-plan.md),
  slice 0.

## Limits

- Secure screens are expected to record as blank.
- Developer options and USB debugging must stay on for the session.
- The tree is what apps expose to accessibility services, as UI Automation
  is on Windows.
- A second accessibility service is visible to apps, and some apps
  "refuse to run at all while an accessibility service is enabled"
  ([android-agent](https://github.com/Fox-Islam/android-agent)).
- With `output`, the participant hears the phone through the PC, a little
  later than on the phone itself.

## Checks before building

These use the standard scrcpy 5.0.1 and adb on the PC, with no new code,
and answer what the design depends on. They are written as tests of at
most five steps with on-screen checks in
[the feasibility checks](../validation/android-feasibility-plan.md), run
by `scripts/Test-AndroidFeasibility.ps1`.

1. Does `output` capture TalkBack's speech, and does `playback` with
   `--audio-dup`? The check shows the captured sound's levels as a
   printed chart in PowerShell, with TalkBack speaking and with it
   silent.
2. Does TalkBack's "Display speech output" text appear in the recorded
   video?
3. Does TalkBack's verbose log hold the spoken text, and with what times?
4. Does `getevent` show touches on the Pixel without root, with TalkBack
   on?

## Questions for the owner

1. **Device sound.**
   - (a) Recommended if check 1 shows TalkBack only there: `output`. The
     recording has everything the phone plays, including TalkBack, and
     the participant hears it from the PC's speakers or headphones.
   - (b) `playback` with duplication. The phone keeps its sound, but the
     recording may lack TalkBack and any app that opts out.
2. **The companion app as an accessibility tool.**
   - (a) Recommended: declare `isAccessibilityTool`. The app then sees
     sensitive views as TalkBack does, and works under Advanced
     Protection. The app is for accessibility testing and is installed by
     the recorder, not offered on Google Play, whose policy reserves the
     flag for apps whose primary purpose is helping people with
     disabilities
     ([Google Play](https://support.google.com/googleplay/android-developer/answer/10964491?hl=en-GB)).
   - (b) Do not declare it. Sensitive views are missing from the tree,
     and the app is blocked under Advanced Protection.
3. **Connection.** USB first and wireless debugging later (recommended),
   or both from the start?
4. **First version's scope.** The phone with the PC's microphone
   (recommended), or the phone and the PC's desktop in one recording?
5. **Checks first.** Run the four checks above before any building
   (recommended)?

## Decisions of 2026-11-07

The owner answered questions 2 to 5, and question 1 followed from check 1:

- **Device sound (question 1, option a).** `output`. It records
  TalkBack and everything else the phone plays; `playback` recorded
  nothing of TalkBack. The phone is silent during a recording, and the
  participant hears it from the PC. The phone's volume is set during the
  session, with Accessibility volume at about 80%.
- **Companion app (question 2, option a).** The app declares
  `isAccessibilityTool`. It is an accessibility app, is never offered on
  Google Play, and is installed by the recorder over adb on a phone in
  developer mode.
- **Connection (question 3).** USB first. Wireless debugging comes later.
- **First version (question 4).** The phone with the PC's microphone. The
  phone and the PC's desktop in one recording is not in the first
  version.
- **Checks first (question 5).** The four checks are run before any
  building, as
  [the feasibility checks](../validation/android-feasibility-plan.md).

The owner answered the implementation plan's three questions on
2026-11-08: the companion app is built by the installer build on the
development PC, and Chrome's DevTools Protocol and analysis of phone
recordings come after the first version
([implementation plan](android-implementation-plan.md#decisions-of-2026-11-08)).
