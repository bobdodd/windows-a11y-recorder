# Recording an Android device: implementation plan

Written 2026-11-07 for the first version agreed in
[Recording an Android device](android-recording.md): the phone connected
to the PC by USB, recorded with the PC's microphone. Slices 0 and 1 are
built; the others are not. The owner answered its three questions on 2026-11-08;
see [Decisions of 2026-11-08](#decisions-of-2026-11-08).

Each part below is marked as one of:

- **Checked**: seen on the owner's PC or Pixel, or read in the source
  named.
- **Inference**: expected from what was checked, and confirmed by the test
  named with it.
- **Not checked**: an open point, with the slice that settles it.

## What the first version records

| Source | How | Clock on the phone | Stored as |
|---|---|---|---|
| Screen | scrcpy 5.0.1 server, video socket, H.264 | encoder time stamps (inference: CLOCK_MONOTONIC) | the encoded stream as received, one event per packet, and PNG frames taken from it |
| Phone sound | scrcpy server, audio socket, `output`, raw 16-bit PCM | Android's audio time stamp, CLOCK_MONOTONIC (checked in source) | `audio/device.wav`, one event per buffer |
| PC microphone | as now (`AudioCollector`) | session clock | `audio/microphone.wav`, as now |
| Touches and hardware keys | `adb shell -tt getevent -lt` | CLOCK_MONOTONIC (checked in documentation) | raw lines, and touch contacts in screen pixels |
| What TalkBack said | `adb logcat -v monotonic`, TalkBack's lines | CLOCK_MONOTONIC (checked on the Pixel, same scale as touches) | raw lines, and utterances parsed from them by a versioned parser |
| Accessibility events and tree | companion app's accessibility service | event time from `SystemClock.uptimeMillis()` (checked in source), and the app's own receipt time | events and tree snapshots |
| Settings | `adb shell settings list` at start, companion app's observers after | receipt time in the app | settings records |
| Phone clock | companion app answers clock requests over an adb socket | `System.nanoTime()` | mapping samples |

Not in the first version: Chrome's DevTools Protocol (decided 2026-11-08), the
PC's desktop in the same recording (decided 2026-11-07), wireless
debugging (decided 2026-11-07), the phone's microphone, and analysis of
phone recordings (decided 2026-11-08).

## How the recorder is built today

The plan follows the recorder's existing structure, read from the source
on 2026-11-07 (commit `1f42a04`):

- A recording is run by `SessionCoordinator`, which takes a factory of
  collectors (`WindowsCollectorFactory.Create`) and `RecordingOptions`.
  Each collector implements `ICaptureCollector`: it declares its channels,
  reports its capability, and writes `RecorderEvent`s to the session's
  event sink.
- `RecorderEvent` already has a `NativeTimestamp` (domain, value, unit), a
  `ClockMappingId` and a `TimestampUncertaintyNanoseconds`, which the
  collector contracts require of a collector with its own clock
  ([collector contracts](collector-contracts.md), "Timestamp contract").
  The instrumented Chromium's `BrowserClockMapper` already maps another
  clock to the session clock from request and reply times, taking the
  midpoint and half the round trip.
- Events go into one MCAP recording file per recording, with PostgreSQL
  holding the catalog and the file's index
  ([Change-Driven Browser Capture and Recording Files](change-driven-recording.md),
  "Recording file"). Each built-in channel's payloads are checked by
  `EventPayloadValidator` before they are written.
- The desktop picture is PNG files, `frames/desktop/<sequence>.png`, with a
  `desktop-frame` event on `graphics.desktop.frames` for each. Sound is
  WAV files with an `audio-buffer` event per buffer. The player
  (`PlaybackIndex`, `SessionTimelineControl`) and the MP4 export
  (`Recorder.Export`) read those.
- The installer is built on the development PC by
  `scripts\Build-Installer.ps1`, which fetches pinned components and
  checks their hashes ([Windows installer](windows-installer.md)).

## New parts

- **`src/Recorder.Collectors.Android`**: the adb runner, phone discovery
  and checks, the scrcpy client, the `getevent` and `logcat` readers, the
  companion app's connection, the phone clock mapper, and the collectors.
  An `AndroidCollectorFactory` sits beside `WindowsCollectorFactory`.
- **`android/companion`**: the companion app, in Java, built from the SDK
  tools by `scripts/Build-CompanionApp.ps1` (slice 0).
  Package `io.github.bobdodd.a11yrecorder.companion`, named after the
  repository's GitHub owner as Android package names usually are.
- **Recorder, player, export and installer** changes, listed in each
  slice.

## Time

All the phone's sources are put on the session clock through one phone
clock domain, `android-monotonic`, for CLOCK_MONOTONIC:

- `getevent` gives times "in the CLOCK_MONOTONIC timebase"
  ([Android, getevent](https://source.android.com/docs/core/interaction/input/getevent)).
  Checked.
- The scrcpy server takes sound times from
  `AudioRecord.getTimestamp(..., AudioTimestamp.TIMEBASE_MONOTONIC)`
  ([scrcpy 5.0.1, AudioRecordReader.java](https://github.com/Genymobile/scrcpy/blob/a60891aea193d92e7e5c3942700eca63f9d19a5f/server/src/main/java/com/genymobile/scrcpy/audio/AudioRecordReader.java)).
  Checked in source.
- `logcat -v monotonic` times were on the same scale as the touch times
  in checks 3 and 4. Inference that they are CLOCK_MONOTONIC.
- Video packet times are the encoder's presentation times, sent unchanged
  ([scrcpy 5.0.1, Streamer.java](https://github.com/Genymobile/scrcpy/blob/a60891aea193d92e7e5c3942700eca63f9d19a5f/server/src/main/java/com/genymobile/scrcpy/device/Streamer.java)).
  Which clock the display's frames are stamped with is not stated there.
  Inference that it is CLOCK_MONOTONIC.
- Android sets an accessibility event's time with
  `event.setEventTime(SystemClock.uptimeMillis())` when it is sent
  ([AOSP, AccessibilityManager.java line 699](https://github.com/aosp-mirror/platform_frameworks_base/blob/1cdfff555f4a21f71ccc978290e2e212e2f8b168/core/java/android/view/accessibility/AccessibilityManager.java#L699)).
  That this is the same clock as CLOCK_MONOTONIC, in milliseconds, is
  inference. The app also stamps each event with `System.nanoTime()` when
  it receives it.

Mapping:

- The recorder asks the companion app for `System.nanoTime()` several
  times a second over the app's socket. Each exchange gives a sample: the
  midpoint of the recorder's send and receive times against the phone's
  time, with half the round trip as its error bound, as
  `BrowserClockMapper` does. Samples are recorded on their own channel.
- Each phone event keeps its phone time as its `NativeTimestamp`. Its
  session time comes from a straight line fitted to the samples so far,
  which follows drift; its uncertainty is the error bound of the nearest
  samples.
- If the phone sleeps, CLOCK_MONOTONIC stops. A jump between phone time
  and session time in the samples is recorded as a discontinuity, as the
  contracts require, and the line is fitted again after it.
- Before the companion app is connected, or if it cannot be, events are
  stamped with the session clock when the recorder receives them, with
  the receipt-time quality flag.

The one-timeline check (slice 8) shows whether the inferences hold.

## Slices

Each slice is built, tested in the Linux build environment, and then run
on the PC and Pixel with a target test of at most five steps and on-screen
checks, before the next starts. The tests below are outlines; each
slice's full test goes into a validation plan,
`docs/validation/android-recording-plan.md`, when the slice is built.

### Slice 0: tools

- **adb.** Not bundled. The Android SDK licence says "you may not copy
  (except for backup purposes), modify, adapt, redistribute ... the SDK or
  any part of the SDK", except where an open source licence of a
  component applies
  ([Android, SDK terms 3.4 and 3.5](https://developer.android.com/studio/terms)).
  An "Android setup" window in the recorder, like model setup, downloads
  SDK Platform-Tools r37.0.1, the current stable release in Google's
  repository list
  ([Google, repository2-3.xml](https://dl.google.com/android/repository/repository2-3.xml)),
  after showing the licence and a link to it, into
  `%LOCALAPPDATA%\Windows A11y Recorder\android\platform-tools`. The
  recorder checks the download against values fixed in its source: 8,044,989
  bytes, SHA-1 `e03e78b1d80b396f1c3358e31251cb31740e1110` as Google's list
  gives, and SHA-256
  `45f4d63113e895ebde0c90f194099a4676b6ac653bd28d54314a9e022bbc1a99`,
  computed from that download on 2026-11-07. The proposal said the
  installer would download it; doing it in the app instead puts the
  licence in front of the person at the point of download, as model setup
  does for models.
- **USB driver.** Not needed on the owner's PC: Windows' own WinUSB
  driver served the phone's ADB interface (feasibility checks, 2026-11-07).
  The checks panel (slice 1) says what to do if the phone is not seen.
- **scrcpy server.** Bundled. `scrcpy-server-v5.0.1` is 733,930 bytes with
  SHA-256 `764eb6f79811d5211fe9df341120882ba9994c7a61b897d7bf3fb662e53bc536`,
  as the release lists
  ([scrcpy v5.0.1](https://github.com/Genymobile/scrcpy/releases/tag/v5.0.1)).
  It is Apache 2.0
  ([scrcpy, LICENSE](https://github.com/Genymobile/scrcpy/blob/master/LICENSE)),
  so it goes into `android\` in the install folder with its licence, and
  into `THIRD-PARTY-NOTICES.txt`.
- **Companion app build.** By `Build-Installer.ps1` on the development
  PC (decided 2026-11-08), which runs `scripts\Build-CompanionApp.ps1`.
  The script fetches a pinned Java development kit, the SDK Build-Tools
  37.0.0 and the Android 17 SDK platform on first use, and the owner
  accepts the Android SDK licence there once. It builds the app straight
  from those tools (`aapt2`, `javac`, `d8`, `zipalign`, `apksigner`), not
  with Gradle, and the app is written in Java, not Kotlin, so the build
  downloads only those three pinned files and nothing from Maven
  repositories (decided while building part 0; see
  [Windows installer, "Android files"](windows-installer.md#android-files)). The Linux build environment
  builds the app too, to test changes before they reach the PC. The app
  is signed with a key kept on the build machine outside the repository,
  so a new version installs over the old one. If the key ever changes, the
  recorder removes the old app and installs the new one, and turns the
  service on again (slice 2).
- **Target test.** Open Android setup, accept the licence, download; the
  window shows "Platform-Tools 37.0.1 installed" and the folder.
- **Built (2026-11-08).** `Recorder.Collectors.Android` holds the pinned
  values (`AndroidTools`), the checked unpacking of Platform-Tools
  (`PlatformToolsInstaller`) and the status of each program
  (`AndroidToolsStatus`). The Android menu's "Set up Android recording..."
  opens `AndroidSetupWindow`. `android/companion` is the app's skeleton:
  the accessibility service is declared, with `isAccessibilityTool`, and
  receives nothing yet. The validation steps are in
  [the Android recording validation plan](../validation/android-recording-plan.md).

### Slice 1: the phone, found and checked

- **Recorder.** A recording target, "Android device", beside the
  desktop's options. Choosing it shows a checks panel:
  - adb ready, and its version;
  - the phone found, with its state (`device`, `unauthorized`, `offline`,
    or not seen), model, Android version, build and security patch,
    screen size;
  - TalkBack running or not, read from the enabled services;
  - the companion app installed and on (filled in by slice 2);
  - "Display speech output" on or off, if it can be read (not checked
    whether it can; otherwise the panel reminds the tester to check it,
    as check 2 found it off at first).
  For a phone not seen, the panel gives the steps the feasibility checks
  needed: USB debugging on, the cable's mode set to File transfer, and
  the "Allow USB debugging?" prompt accepted.
- **adb server.** The recorder runs its own adb. Another tool, such as
  Android Studio, may already be running an adb server of another
  version, so before starting one the recorder checks for one and, if it
  finds one, says so rather than replacing it without warning.
- **Code.** An `IAdb` interface over the adb process, with a fake for
  tests, as the PowerShell checks were tested against a fake adb.
- **Target test.** With the phone connected and TalkBack on, choose
  "Android device"; the panel shows "Pixel 10 Pro XL", "Android 17",
  "1080 by 2404" and "TalkBack: on". Unplug the cable; within a few
  seconds the panel shows "Phone: not found" and the steps.
- **Built (2026-11-08).** The side panel's new "Recording target" group
  offers "This PC's desktop" and "Android device" (Alt+V). Choosing the
  Android device hides the capture channels and the Chromium settings
  and shows "Android device checks", which `AndroidDeviceChecks` in
  `Recorder.Collectors.Android` fills in every 2 seconds. A change of the
  phone's state, such as unplugging it, is also spoken. "Start
  recording" with the Android device chosen says the recording is not
  built yet.
  - Each check is one adb command through `IAdb`: `adb version` once,
    `adb devices -l`, and for a connected phone one `adb -s <serial>
    shell` command that prints the model, Android version, build,
    security patch, `wm size` and the enabled accessibility services,
    each after a marker. Tests run them against a fake that answers with
    what the Pixel printed in the feasibility checks.
  - The adb server: before starting one, the recorder asks port 5037 for
    a running server's version, with the request adb's own client sends
    ([adb, overview](https://android.googlesource.com/platform/packages/modules/adb/+/refs/heads/main/docs/dev/overview.md)),
    and lists the adb programs running. adb's client stops a server whose
    version differs from its own and starts its own
    ([adb, adb_client.cpp](https://android.googlesource.com/platform/packages/modules/adb/+/refs/heads/main/client/adb_client.cpp)).
    So a server from another program is used if its version matches
    the recorder's (41 for Platform-Tools 37.0.1); otherwise the panel
    names its program and version and stops there, leaving it running.
    The recorder's own server is left running when the recorder closes,
    as adb's servers are.
  - "Display speech output": not read, so the panel always reminds the
    tester where to turn it on. It is set in TalkBack's own settings
    screens, and the inference is that TalkBack keeps it in its own
    storage, which adb cannot read on a phone that is not rooted. Not
    checked: whether `adb shell settings list secure` on the Pixel shows
    it.
  - The companion app line waits for slice 2.
  - Access keys: "Desktop frames" is now Alt+T, so Alt+D opens the
    Android menu (validation plan, part 0 results). Alt+T is also "Stop
    recording", which is never available while the channels can be
    changed.

### Slice 2: the companion app and the phone clock

- **App.** One accessibility service and no launcher icon. Its service
  configuration: all event types, `canRetrieveWindowContent`,
  `flagRetrieveInteractiveWindows`, `flagReportViewIds`,
  `flagIncludeNotImportantViews` (each node records whether it is
  important for accessibility, so the player can show TalkBack's view of
  the tree), `isAccessibilityTool="true"` (decided 2026-11-07), and no
  request for explore by touch. It performs no actions. It has no
  internet permission and stores nothing on the phone.
- **Connection.** The service opens a local socket in Android's abstract
  namespace, `a11yrecorder_companion`, which the recorder reaches with
  `adb forward tcp:0 localabstract:a11yrecorder_companion`, as Chrome's
  DevTools socket is reached
  ([Chrome, remote debugging](https://developer.chrome.com/docs/devtools/remote-debugging)).
  Any app on the phone could connect to such a socket, so the service
  accepts only a peer whose user is adb's shell, checked with
  `LocalSocket.getPeerCredentials()`
  ([Android, LocalSocket](https://developer.android.com/reference/android/net/LocalSocket)).
  Messages are lines of JSON with a protocol version.
- **This slice's messages.** Hello (app version, protocol version,
  Android version), clock requests and replies, and the display's
  rotation and size when they change.
- **Install and turn on.** From the checks panel: `adb install -r` with
  the APK from the install folder, then add the service to
  `enabled_accessibility_services` with `adb shell settings put secure`,
  keeping every service already listed so TalkBack stays on. That
  `adb install` avoids Android's restricted settings block is reported by
  developers, not checked
  ([android-recording.md](android-recording.md), "Accessibility tree and
  events"); this slice checks it on the Pixel.
- **Recorder.** The phone clock mapper (see "Time"), a generalised form
  of `BrowserClockMapper`, and a `clock.android` channel for the samples.
- **Target test.** In the checks panel choose "Install companion app";
  the panel shows "Companion app: installed, on". On the phone, Settings,
  Accessibility shows the service and TalkBack both on. Start and stop a
  recording of 30 seconds; the recording's details show clock samples
  with a largest error bound in milliseconds.

### Slice 3: the screen

- **Server.** The recorder pushes the bundled server to
  `/data/local/tmp`, sets up `adb reverse localabstract:scrcpy_<scid>` to
  a loopback port it listens on, and starts the server with matching
  version `5.0.1`, `video_codec=h264`, `audio_source=output`,
  `audio_codec=raw`, `control=false`, and nothing that changes the phone
  (no `show_touches`, no `stay_awake`). The socket order, codec id,
  session packets and 12-byte packet headers are as scrcpy documents
  ([scrcpy, develop.md](https://github.com/Genymobile/scrcpy/blob/master/doc/develop.md),
  "Protocol"). That page warns the protocol "may (and will) change at any
  time", so the client is written against the 5.0.1 source and tested
  with captured bytes.
- **Stored.** `android/screen.h264`, the stream as received, with an event
  per packet on `graphics.android.video`: byte offset, size, phone time,
  key frame and configuration flags, and the capture session (width and
  height, a new one on each rotation). This is the complete record of the
  picture.
- **Frames.** Windows' Media Foundation H.264 decoder decodes the stream
  during the recording. Microsoft gives its largest size as 4096 by 2304
  ([Microsoft, H.264 video decoder](https://learn.microsoft.com/en-us/windows/win32/medfound/h-264-video-decoder)),
  and the phone's picture is 2404 high. Checked on the development PC on
  2026-11-07: Windows decoded a frame of check 2's recording (H.264 High
  profile, 1080 by 2404) through `Windows.Media.Editing`. Each decoded
  frame whose pixels differ from the frame before is written as PNG,
  `frames/android/<sequence>.png`, with a `device-frame` event on
  `graphics.android.frames` giving the frame's phone time, session time
  and the packet it came from, and a quality flag saying the picture was
  compressed on the phone. The server repeats the last frame after
  100 ms without change
  ([scrcpy 5.0.1, SurfaceEncoder.java](https://github.com/Genymobile/scrcpy/blob/a60891aea193d92e7e5c3942700eca63f9d19a5f/server/src/main/java/com/genymobile/scrcpy/video/SurfaceEncoder.java)),
  so the pixel comparison keeps such repeats out. Every changed frame
  becomes a PNG, with no rate limit, so nothing brief is missed. Disk
  use and processor load are measured on the Pixel in this slice before
  any limit is considered. If decoding falls behind, the stream is still
  complete, and the frames it did not reach are taken from it after
  Stop, before the recording is completed.
- **Player.** The phone's frame takes the desktop's place, fitted to the
  height of the frame area.
- **MP4 export.** Uses the phone's frames as the picture. The export
  already shrinks a frame to fit within 4096 by 2304 with its shape kept
  ([MP4 export](mp4-export.md), "Encoding"), so a portrait phone picture
  is exported at about 1035 by 2304.
- **Secure windows** are expected to record as blank (design, "Limits").
- **Target test.** Start an Android recording, open three apps on the
  phone, stop. In the player the phone's screen shows each app as the
  time bar moves, upright and filling the frame's height. Rotate the
  phone during a second recording; the player shows the landscape
  picture after the rotation.

### Slice 4: the phone's sound

- **Stored.** The audio socket gives raw PCM at 48 kHz, two channels,
  16 bits
  ([scrcpy 5.0.1, AudioConfig.java](https://github.com/Genymobile/scrcpy/blob/a60891aea193d92e7e5c3942700eca63f9d19a5f/server/src/main/java/com/genymobile/scrcpy/audio/AudioConfig.java)).
  It is written to `audio/device.wav`, with an `audio-buffer` event per
  packet on `audio.device`, giving the byte offset, length and phone time,
  and the session time from the mapping. The export's sound placement
  (`AudioPlacement`) then places it from those times without change.
- **Level.** While recording, the recorder shows the phone sound's level.
  If sound is present but its loudest moments stay low, it shows "The
  phone's sound is very quiet. Raise the phone's Accessibility volume
  now." The threshold is set from check 1: a loudest half second of 2.9%
  of full scale was too quiet to use, and 32.0% at about 80%
  Accessibility volume was usable.
- **Volume over adb.** Not checked. Android's shell has
  `media_session volume --stream <n> --get` and `--set`
  ([AOSP, VolumeCtrl.java](https://github.com/aosp-mirror/platform_frameworks_base/blob/1cdfff555f4a21f71ccc978290e2e212e2f8b168/services/core/java/com/android/server/media/VolumeCtrl.java)).
  This slice checks whether it reads and sets the Accessibility volume on
  the Pixel, and whether setting it during the capture changes the
  captured level, as the volume keys do
  ([scrcpy issue 3790](https://github.com/Genymobile/scrcpy/issues/3790#issuecomment-1472079314)).
  If it does, the recorder offers to set it; if not, the message above is
  all it does.
- **Player and export.** The phone's sound takes the system sound's place,
  labelled "Phone sound", with its own volume slider. The MP4 export
  treats it as the system sound, as the design says.
- **Target test.** Record 20 seconds while swiping through the Home
  screen with TalkBack on, raising Accessibility volume to about 80%
  during the recording. The level shown in the recorder rises after each
  swipe, and the quiet message goes once the volume is raised. In the
  player the timeline has a "Phone sound" lane across the whole
  recording, and the volume panel has a "Phone sound" slider.

### Slice 5: touches

- **Read.** `adb shell getevent -lp` at the start, for each input device's
  name, properties and axis ranges, and the touch screen chosen as the
  device with `ABS_MT_POSITION_X` and `INPUT_PROP_DIRECT`. Then
  `adb shell -tt getevent -lt`, through a terminal on the phone, because
  check 4 found `getevent` holds its output back when read through a pipe.
- **Stored.** Every raw line, with its phone time, on
  `input.android.raw`, and on `input.android.touch` one record per touch
  report: each finger's slot, tracking id, position in screen pixels,
  pressure and size where reported, and down, move or up. Positions are
  converted from the panel's units with the axis ranges and the
  display's rotation from the companion app, keeping the last known x and
  y when a report gives only one, as the feasibility script does.
  Hardware keys (volume, power) come from the same lines.
- **Fixtures.** Check 4's file holds only touch positions and times, and
  can be used as a test fixture.
- **Player.** Touches are drawn on the phone's picture as the pointer is
  drawn now: a circle for each finger while it is down, numbered when
  there is more than one, with its recent path.
- **Target test.** Record three taps at the top, middle and bottom of the
  screen, then a two-finger swipe. In the player a circle appears at each
  tap's place on the phone's picture, and two numbered circles move
  together during the swipe.

### Slice 6: what TalkBack said

- **Read.** `adb logcat -v monotonic` from the recording's start, keeping
  TalkBack's lines by its process, and following TalkBack to a new
  process if it restarts. TalkBack's log level must be Verbose; the
  recorder cannot set it, and shows "No TalkBack speech lines yet" until
  the first line arrives, so a tester who forgot can fix it at once.
- **Stored.** Every TalkBack line as received, with its phone time, on
  `screen-reader.talkback.log`. A parser, with its version in each
  record, makes `screen-reader.talkback.speech` from them: utterance
  started (text, utterance id, causing event type and time), word reached,
  and utterance interrupted, using the line forms check 3 found. The raw
  lines are kept so a new parser can run again on old recordings, because
  these are TalkBack's internal log lines and can change with any
  TalkBack version.
- **Privacy.** TalkBack's verbose log "exposes the contents of your
  screen to your device's logs"
  ([Google Issue Tracker](https://issuetracker.google.com/issues/517413745)).
  The recorder keeps only TalkBack's lines, and reminds the tester at
  Stop that verbose logging is still on. The repository is public, so the
  parser's test fixtures are lines from check 3 cut to those needed and
  reviewed by the owner before they are committed.
- **Player.** A "TalkBack said" lane, like the NVDA lane, with each
  utterance's text at its start time, interrupted ones marked, and the
  words reached shown when an utterance is selected.
- **Target test.** With TalkBack's log at Verbose, record three swipes on
  the Home screen. The lane shows three app names and their usage hints
  at the times of the swipes, and the hint cut off by the next swipe is
  marked interrupted.

### Slice 7: accessibility events and tree

- **App.** Sends every accessibility event with its type, event time,
  receipt time, package, class, window, source node, text, content
  description, change types and the other fields the event carries
  ([Android, AccessibilityEvent](https://developer.android.com/reference/android/view/accessibility/AccessibilityEvent)).
  Sends a snapshot of every window's tree at the start, on each window
  change and on content changes: for each node its bounds on screen,
  class, text, content description, state description, hint, view id,
  actions, states (checked, selected, enabled, focused, accessibility
  focused, heading), importance for accessibility, pane title,
  collection and range information, labelled-by, and traversal order.
  Chrome supplies its web content to accessibility services as a tree of
  virtual nodes
  ([Chromium, Chrome Accessibility on Android](https://chromium.googlesource.com/chromium/src/+/HEAD/docs/accessibility/browser/android.md)),
  so websites in Chrome are in this tree without the DevTools Protocol.
- **Rate.** How often content changes arrive is measured on the Pixel in
  this slice before any limit is set, as the design says. Until then,
  every change gets a snapshot.
- **Stored.** `accessibility.android.events` and
  `accessibility.android.tree`.
- **Player.** An "Accessibility events" lane, and the tree view used for
  UI Automation showing the phone's tree at the selected time, with the
  accessibility-focused node highlighted on the picture.
- **Target test.** With TalkBack on, record swipes through Settings and
  opening one page. The events lane shows an accessibility focus event at
  each swipe; selecting one shows, in the tree, the item TalkBack spoke,
  and its outline on the phone's picture.

### Slice 8: settings, the one-timeline check, and the whole recording

- **Settings.** At the start, `adb shell settings list secure`, `system`
  and `global`, keeping the accessibility-related keys named in a list in
  the source and documented with it, such as the enabled services,
  TalkBack, explore by touch, color inversion, font scale, display size,
  and the text-to-speech rate. After that, the companion app's content
  observers on the same keys
  ([Android, Settings.Secure](https://developer.android.com/reference/android/provider/Settings.Secure)).
  TalkBack's own preferences, such as verbosity, are kept inside TalkBack
  and are not in these lists. Stored on `preferences.android`, shown in a
  settings panel like the Windows one
  ([accessibility preferences](accessibility-preferences.md)).
- **One-timeline check.** One recording in which a tap opens an app: the
  touch-down time, the accessibility event's time, the first video frame
  that shows the change, TalkBack's utterance start, and the sound's
  start are compared on the session clock. The expected order is touch,
  then event, then frame and utterance, then sound. A source out of order
  by more than its uncertainty shows which inference in "Time" failed.
- **Whole recording.** A five-minute recording with every source, checked
  for: nothing refused by the writer, clock error bounds, frame count
  against packets, sound length against the recording's length, and the
  MP4 export.
- **Installer.** `Build-Installer.ps1` adds the scrcpy server, checked by
  its SHA-256, and the companion app's APK to `android\`, and their
  licences to the notices. Validated with a fresh install on the
  development PC, then on the Alienware.
- **Target test (settings).** Start a recording, turn color inversion on
  and off on the phone, stop. The settings panel shows color inversion
  on, then off, at the two times.

## Risks, and where each is settled

| Point | Status | Settled in |
|---|---|---|
| Media Foundation decodes the phone's 1080 by 2404 H.264 | Checked on the development PC with one frame; the Alienware not checked | slice 3 |
| Video packet times are CLOCK_MONOTONIC | Inference | slice 8, one-timeline check |
| Accessibility event times match CLOCK_MONOTONIC | Inference from source | slice 8 |
| `adb install` avoids the restricted settings block | Reported by developers | slice 2 |
| Accessibility volume can be read or set over adb | Not checked | slice 4 |
| "Display speech output" can be read over adb | Not checked | slice 1 |
| PNG for every changed frame keeps up on the PC | Not measured | slice 3 |
| Content change rate allows a snapshot per change | Not measured | slice 7 |
| TalkBack's log lines keep their form | Internal to TalkBack; raw lines kept | each TalkBack update |

## Questions for the owner

1. **Where the companion app is built.**
   - (a) Recommended: by `Build-Installer.ps1` on the development PC, as
     the rest of the installer is. The script fetches a pinned Java
     development kit and the Android SDK command-line tools on first use,
     and you accept the Android SDK licence there once. The build
     environment here also builds it, to test changes before they reach
     the PC.
   - (b) By GitHub Actions, with the APK attached to a release that
     `Build-Installer.ps1` downloads and checks by hash. Nothing extra on
     the PC, but every installer build depends on a release being made
     first.
2. **Chrome's DevTools Protocol.**
   - (a) Recommended: after the first version. Websites in Chrome are
     recorded through the accessibility tree, the picture, touches and
     TalkBack from slice 7 on; the DevTools Protocol adds page
     structure, navigation and network events.
   - (b) In the first version, as a slice after slice 7.
3. **Analysis of phone recordings.**
   - (a) Recommended: after the first version. The first version records,
     plays and exports phone recordings, and the analysis is planned
     once real phone recordings exist to design it against.
   - (b) In the first version, as a slice after slice 8.

## Decisions of 2026-11-08

The owner answered the three questions:

- **Companion app build (question 1, option a).** By the installer build,
  `Build-Installer.ps1`, on the development PC.
- **Chrome's DevTools Protocol (question 2, option a).** After the first
  version.
- **Analysis of phone recordings (question 3, option a).** After the
  first version, once real phone recordings exist.
