# Recording an Android device

Proposed 2026-11-07. Questions 2 to 5 agreed on 2026-11-07 (see
[Decisions](#decisions-of-2026-11-07)); question 1 waits for check 1. Not
built.

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

Neither page says whether TalkBack's speech is included. Since app capture
excludes screen reader speech (see "Shape"), `playback` may well leave it
out. That is inference, and check 1 decides it. The choice between the two
is question 1.

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
  Check 2 confirms they reach the recorded picture.
- With verbose logging, TalkBack writes to the system log, which the
  recorder can read over adb. Google's own bug template warns that
  "TalkBack logs exposes the contents of your screen to your device's
  logs"
  ([Google Issue Tracker](https://issuetracker.google.com/issues/517413745)).
  Whether the log holds the spoken text, and with what times, is check 3.
  If it does, TalkBack's speech is recorded as text, which NVDA does not
  allow.
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
- The same stream gives the hardware keys, such as volume and power. Text
  typed on the on-screen keyboard is not in it. Text changes come from the
  accessibility events.

### Settings

At the start and on each change, the recording holds the phone's
accessibility-related settings, in the same form as the Windows ones
([accessibility preferences](accessibility-preferences.md)). Examples are
the enabled accessibility services, TalkBack, explore by touch, color
inversion, font size and display size. The companion app watches each
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
  off, and whether "Display speech output" is on. Each source reports its
  capability as the collector contracts require.
- **Player.** The phone's picture takes the desktop's place, with touches
  drawn as the pointer is now, and the TalkBack and accessibility events
  in their own lanes. MP4 export treats the phone's sound as the system
  sound.
- **Installer.** It bundles the pinned scrcpy server and the companion
  app. Whether Google's adb and USB driver may be bundled needs checking
  against their terms. Until then, the installer downloads them from
  Google during installation, as it does for models.

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

The owner answered questions 2 to 5:

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

Question 1, the device sound source, is decided from tests 2 and 3 of
those checks.
