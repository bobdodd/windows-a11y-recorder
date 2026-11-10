# Recording an Android device: implementation plan

Written 2026-11-07 for the first version agreed in
[Recording an Android device](android-recording.md): the phone connected
to the PC by USB, recorded with the PC's microphone. Slices 0 and 1 are
built and tested; slice 2 is built and passed its tests on the Pixel;
the others are not built. The owner answered its three questions on 2026-11-08;
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
- **Passed (2026-11-08).** Tests 5 to 9 of
  [the validation plan](../validation/android-recording-plan.md) on the
  development PC and the Pixel.
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
  - TalkBack is reported as on or off, and off is not treated as a
    problem: the owner tests magnification, visual layout and colour
    without it (owner, 2026-11-08). The "Display speech output" reminder
    shows only while TalkBack is on.
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
- **Passed (2026-11-08).** Tests 10 to 14 of
  [the validation plan](../validation/android-recording-plan.md) on the
  development PC and the Pixel: install and turn on, a recording's
  clock samples (median error bound 2.4 ms), rotation, and the cable
  unplugged during a recording.
- **Built (2026-11-08).**
  - **App.** `RecorderAccessibilityService` starts `RecorderLink` when
    Android connects the service and stops it when the service is turned
    off. The service configuration is as above, with
    `notificationTimeout` 0. The service receives events but does nothing
    with them yet; slice 7 records them.
  - **Socket.** `RecorderLink` listens on `a11yrecorder_companion` and
    keeps a connection only if `getPeerCredentials()` gives adb's shell
    user, or root on a phone where adb runs as root; any other is closed
    at once.
  - **Protocol 1.** Lines of JSON, each with a `type`. On connecting, the
    app writes `hello` (protocol, app version name and code, Android
    release and SDK level) and then `display` together, so nothing comes
    between them. It answers each `clock` request with the request's `id`
    and its `System.nanoTime()` when it read the request (`received`) and
    when it wrote the reply (`sent`). It writes `display` again (rotation
    in degrees, display mode width and height) when the rotation or size
    changes. `CompanionProtocol.cs` in the recorder and `RecorderLink.java`
    must agree; a hello of another protocol is refused with a message to
    install the recorder's own app.
  - **The checks panel.** The companion line reads the installed version
    code with `pm list packages --show-versioncode` in the same shell
    command as the other checks, and whether the service is in
    `enabled_accessibility_services`. If the app is missing or older than
    the recorder's copy, the panel offers "Install companion app"; if it
    is installed but off, "Turn on companion app". When it is on, the
    recorder connects through `adb forward` and reads the greeting,
    showing "installed, on, and answering" with its version and protocol.
    That answer is kept while the version stays the same; a failure is
    tried again after 6 seconds.
  - **Install and turn on.** `adb install -r` with the APK in the install
    folder. If Android refuses it because the installed app is signed
    with another key, the recorder removes the installed app and installs
    its own. It then reads `enabled_accessibility_services`, adds the
    companion's service to the services already listed, writes the list
    back with `settings put secure enabled_accessibility_services` and
    `accessibility_enabled 1`, and reads it again to check the phone kept
    it. A list holding characters other than those of component names is
    left alone, and the panel asks the tester to turn the app on in
    Settings.
  - **Recording.** "Start recording" with the Android device chosen
    needs the phone connected, and records with one collector,
    `android.companion`; the desktop's capture and the instrumented
    Chromium are not started. The collector opens the forward and the
    connection, records `companion-connected` on `android.companion`, and
    asks for the phone's clock every 250 milliseconds, waiting at most 1
    second for each answer. Each answer is recorded as `clock-sample` on
    `clock.android`, with the four times, its error bound, its segment
    and whether it began a new one. Display messages are recorded as
    `display-state` on `android.display`, with the phone time as
    `NativeTimestamp` and the session time from the fit.
  - **Not reaching the phone does not stop the recording.** If the
    companion cannot be reached at the start, or the connection ends
    during the recording, the collector records `companion-unavailable`
    with the reason, reports itself Degraded with that reason, and the
    recording goes on. adb's forward closes the connection in the same
    way whether the cable was unplugged, adb stopped or the app was
    turned off, so a lost connection's reason names all three. This slice does not connect again after a lost
    connection.
  - **The fit.** `PhoneClockMapper` fits session midpoint on phone
    midpoint by least squares within each segment, each sample weighted
    by the inverse square of its error bound plus 100 microseconds, so a
    slow answer moves the line less than a quick one. A sample begins a
    new segment when the time since the segment's last sample differs
    between the two clocks by more than 20 milliseconds plus both
    samples' error bounds. That is judged from the last sample, not from
    the line: two running clocks differ in rate by parts per million,
    microseconds over a quarter second, while a line fitted to a few
    close samples can be steeper or shallower than that. A phone time
    takes the larger error bound of the samples either side of it as its
    uncertainty, or the last sample's bound beyond the last sample. A
    display message that arrives before the first sample is stamped with
    the time the recorder read it, with the `receipt-time-stamp` flag.
  - **Status.** The "Current session" list shows, for the collector,
    the companion's version, the number of clock samples, the largest
    error bound, the latest rotation and size, and any discontinuities or
    unanswered requests.
  - **Storage.** Migration 0038 adds `android_companion_connections`,
    `android_companion_unavailabilities`, `android_clock_samples` and
    `android_display_states`, and the payload validator checks that each
    sample's error bound agrees with its four times and that rotation is
    0, 90, 180 or 270.
  - **Checked (test 11).** Android 17 on the Pixel accepted the
    `settings put` for the app installed with `adb install`, with
    TalkBack off. Keeping TalkBack on through it is checked only by the
    build environment's tests, until the first screen reader recording.
  - **Checked (test 13).** The display mode's width and height stay in
    the phone's natural orientation when it rotates: on the Pixel,
    rotation went 0, 270, 0, 90, 0, 270 and the size stayed 1080 by
    2404. The reference describes the mode's physical width and height
    without saying whether they follow rotation
    ([Android, Display.Mode](https://developer.android.com/reference/android/view/Display.Mode)).

### Slice 3: the screen

- **Steps.** The slice is built in three steps, each tested on the Pixel
  before the next: 3a stores the stream and an event per packet; 3b
  decodes it to frames; 3c shows the frames in the player and the MP4
  export. Until slice 4 the server was started with `audio=false`.
- **Server.** The recorder pushes the bundled server to
  `/data/local/tmp/a11yrecorder-scrcpy-server.jar`, sets up
  `adb reverse localabstract:scrcpy_<scid>` to a loopback port it listens
  on, and starts the server with matching version `5.0.1`,
  `video_codec=h264`, `video_bit_rate=8000000`, `control=false`,
  `send_device_meta=false`, `cleanup=false`, and nothing that changes the
  phone (no `show_touches`, no `stay_awake`, no `power_on`). Slice 4 adds
  `audio_source=output` and `audio_codec=raw`. The socket order, codec
  id, session packets and 12-byte packet headers are as scrcpy documents
  ([scrcpy, develop.md](https://github.com/Genymobile/scrcpy/blob/master/doc/develop.md),
  "Protocol"). That page warns the protocol "may (and will) change at any
  time", so the client is written against the 5.0.1 source and tested
  with bytes written out by hand from it
  ([scrcpy 5.0.1, Streamer.java](https://github.com/Genymobile/scrcpy/blob/a60891aea193d92e7e5c3942700eca63f9d19a5f/server/src/main/java/com/genymobile/scrcpy/device/Streamer.java)).
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
  and a picture the same in every byte as the one before is not
  written. On the Pixel such repeats are not copies (step 3b). Every
  changed frame
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

- **Built, step 3a (2026-11-08).** `ScrcpyProtocol` reads the codec id,
  session packets and packet headers; `ScrcpyLink` pushes the server,
  opens the reverse tunnel, starts the server through a long-running adb
  shell, waits up to 15 seconds for the phone to connect (or for the
  server to stop, quoting the last three lines of its log), then removes
  the reverse tunnel, as scrcpy does once connected. The server's own
  output is kept as `android/scrcpy-server.log`.
  `AndroidScreenCollector`, `android.screen`, starts after the companion
  and stops before it. It writes each packet to `android/screen.h264` as
  received, with its SHA-256 recorded as it is written, and records on
  `graphics.android.video` a `video-stream` event (server version, codec,
  bit rate, path), a `video-session` event for each capture session
  (width, height, byte offset) and a `video-packet` event for each packet
  (session, byte offset, size, phone time, configuration and key frame
  flags, and the time the recorder received it).
  - **Placing packets.** scrcpy sends the encoder's presentation time
    unchanged
    ([scrcpy 5.0.1, Streamer.java](https://github.com/Genymobile/scrcpy/blob/a60891aea193d92e7e5c3942700eca63f9d19a5f/server/src/main/java/com/genymobile/scrcpy/device/Streamer.java),
    `writePacket`). The encoder is fed by a surface, so that time is
    the frame's timestamp from Android's graphics system, not one scrcpy
    sets. That it is on CLOCK_MONOTONIC, the clock the companion
    samples, is an inference, not checked. The collector's status shows
    the median delay from the mapped phone time to the time the packet
    arrived, so a wrong clock shows as a delay far from tens of
    milliseconds, or below zero. Packets are mapped through the
    companion's clock
    fit; times are held non-decreasing, flagged `clock-fit-held` where
    one is held. Without the phone's clock (companion not reached within
    3 seconds of the start), each packet is placed by the time it
    arrived, with the `receipt-time-stamp` flag. Session and
    configuration packets have no presentation time and are placed at
    the next picture packet's time, with the `next-packet-time` flag.
  - **Size.** scrcpy rounds the picture's width and height down to a
    multiple of the phone's H.264 encoder's alignment
    ([scrcpy 5.0.1, SurfaceEncoder.java](https://github.com/Genymobile/scrcpy/blob/a60891aea193d92e7e5c3942700eca63f9d19a5f/server/src/main/java/com/genymobile/scrcpy/video/SurfaceEncoder.java)),
    not known for the Pixel, so the stream may be a few pixels smaller
    than 1080 by 2404. Each session records the size sent.
  - **Status.** "Phone screen:" with the number of packets, megabytes,
    the size, the number of capture sessions when there is more than
    one, and the median delay.
  - **Failures.** A server that cannot start, or a stream that ends
    during the recording, is recorded as `video-unavailable` and marks
    the collector Degraded; the recording goes on, as with the
    companion. A stream that closes is reported as "the phone was
    unplugged, adb stopped, or scrcpy's server on the phone stopped",
    as adb cannot tell them apart (test 19). A reason quotes up to three
    of the server's last warnings, errors or lines with no level, not
    its INFO lines.
  - **Storage.** Migration 0039 adds `android_video_streams`,
    `android_video_sessions`, `android_video_packets` and
    `android_video_unavailabilities`. The payload validator checks that
    a packet has no presentation time exactly when it is a configuration
    packet.

- **Built, step 3b (2026-11-09).** `PhoneFrames` makes the frames while
  the stream is recorded. The collector flushes each packet to
  `android/screen.h264` and passes its place to `PhoneFrames`, which
  reads the packet back from the file, so a decoder that falls behind
  holds only the packets' places in memory, never their bytes. At Stop
  the collector waits for the frames still to be made before the
  recording is completed.
  - **Decoding.** One thread decodes in order, as H.264 needs.
    `MediaFoundationH264Decoder` uses Windows' H.264 decoder in software,
    with NV12 out and low latency asked for, through its function
    tables, as the MP4 export calls Media Foundation. A configuration
    packet's bytes go to the decoder with the next picture packet. Each
    capture session gets a new decoder, as its size and parameter sets
    differ; the old one is drained first, so no picture is lost at a
    rotation. Each packet's tag goes in as the sample time, and each
    picture out is matched to its packet by it.
  - **Picture.** The decoder pads the picture to whole 16-pixel blocks
    (1088 by 2416 for 1080 by 2404), so each picture is cut to its
    session's size from the top left. It is compared with the picture
    before in every byte, and written only if it differs. It is
    converted to 8-bit RGB with the matrix and range the decoder reports
    from the stream; each 2 by 2 block of pixels takes its own colour
    sample, not one blended from its neighbours (`chromaUpsampling`
    `nearest`). If the decoder reports no matrix or range, BT.709
    limited range is used, the Pixel's, and the frame is flagged
    `colour-assumed`.
  - **PNG.** Written with the Up filter and zlib at its fastest level,
    which changes only the file's size, by up to four workers at once
    (half the processors), with the records given in order. Each file is
    written under a temporary name and renamed when complete, and its
    SHA-256 recorded.
  - **Record.** `device-frame` on `graphics.android.frames`, at its
    packet's session time, phone time, uncertainty and clock mapping,
    with the packet's flags and `compressed-on-phone`. The payload gives
    the path, size, session, the packet's byte offset and phone time,
    the PNG's length, pixel format, matrix, range, chroma upsampling,
    the decoder, the decoder's padded size, the number of pictures the
    same as the frame before it, and the session time the PNG was
    complete, so its delay behind the packet can be read.
  - **Failures.** If Windows' decoder cannot be made (an N edition of
    Windows without the Media Feature Pack) or fails, or a frame cannot
    be written, frames stop, `frames-unavailable` gives the reason once,
    the collector is Degraded with "The phone's frames stopped being
    made: <reason> The phone's screen is still recorded as a video
    stream.", and the stream goes on being recorded.
  - **Status.** The "Phone screen:" line adds the number of frames, and
    "(N packets waiting to be decoded)" while decoding is behind.
  - **Storage.** Migration 0040 adds `android_device_frames` and
    `android_frame_unavailabilities`.
  - **Checked on the development PC (2026-11-09).** A check program fed
    test 18's stream (237 packets, 13 portrait and 222 landscape
    pictures) through `PhoneFrames` with Windows' decoder. It made 235
    frames in 1.9 seconds, 24 processors and four workers; decoding took
    5.7 ms a picture (median, largest 22.3 ms). The decoder reported
    BT.709 and limited range, and padded the pictures to 1088 by 2416
    and 2416 by 1088. The first portrait and the hundredth frame are byte
    for byte the same files as those made by the same conversion from
    FFmpeg's decoding of the same pictures, so Windows' decoder gives
    FFmpeg's pixels exactly. Against FFmpeg's own conversion to RGB
    (BT.709, limited range, nearest), no value differs by more than 3.
  - **Repeats are not copies.** No picture of test 18 was the same as
    the one before, so none was left out. When the screen stops
    changing, the Pixel's encoder sends a picture every 100 ms for about
    a second. The first of these differ from the one before in tens of
    thousands of samples or more, the last ones in 768 to 1,784 samples
    by 1 level, which suggests each refines the last; that is an
    inference from the pixels, not documented. Each is kept, as a rule that ignored small
    differences could also miss a small change on the screen.
  - **Disk use.** Test 18's 235 frames take 146 MB, 0.62 MB a frame on
    average: 2.4 to 3.0 MB for a detailed portrait screen, 0.17 to 0.6
    MB for the landscape pictures. While the screen moves the phone
    sends up to 120 pictures a second (8 ms apart in test 18), so
    scrolling a detailed screen can use well over 100 MB a second, and
    the check made frames at about 125 a second. Both are measured again
    on the Pixel in tests 21 to 23 before any limit is considered.
  - **Measured on the Pixel, and decided (2026-11-09).** In test 21,
    3,080 frames from about 76 seconds of scrolling took 2,244 MB,
    against 26.5 MB for the stream, and decoding kept up. Decided by the
    owner: the frames stay as step 3b makes them, with no limit on how
    many and no harder compression. Disk space is cheap and plentiful,
    and temporary folders of 100 GB are not a problem. Not every picture
    of the stream gets a frame: a picture the same in every byte as the
    one before gets none, and the phone sends no pictures while its
    screen is still. On the Pixel, though, no picture has yet been the
    same as the one before, so in tests 18 and 21 every picture the
    phone sent got a frame. Evidence for analysis uses only what
    differs, as for the desktop.

- **Built, step 3c (2026-11-09).** The player and the MP4 export show
  the phone's frames.
  - **Which frames.** The playback index keeps the `device-frame`
    events, and its version is now 20, so a recording made with step 3b
    has its index derived again when it is opened; the player's status
    line then ends "The recording file's playback index is version 19".
    A recording with desktop frames shows them, as before. One with no
    desktop frames and some phone frames shows the phone's
    (`SessionFrameSource.Phone`). A recording has one or the other
    today, as a recording's target is the desktop or the phone; if one
    has both, the desktop's are shown.
  - **Player.** The phone's frame takes the desktop's place, drawn as
    large as fits the frame area with its shape kept, so a portrait
    picture fills the area's height and a landscape one its width. The
    Frames filter and the timeline's frame lane include the phone's
    frames. A screen reader hears the picture as "Recorded phone screen
    frame", with "Phone frame N of M" and its time. A recording with no
    frames of either kind says "This recording contains no desktop or
    phone frames."
  - **MP4 export: size.** The phone's pictures change shape when it is
    turned, so the export is as wide as the widest picture and as tall
    as the tallest, made smaller to fit within 4096 by 2304 with its
    shape kept. Each picture is drawn as large as fits, centred on black.
    A recording that stays portrait exports at 1034 by 2304; one turned
    on its side exports at 2304 by 2304, a portrait picture filling the
    height and a landscape one the width.
  - **MP4 export: rate.** 60 frames a second, fixed. The desktop's rate
    comes from its frames' usual spacing ([MP4 export](mp4-export.md),
    "Timing of the picture"), but the phone sends pictures only when its
    screen changes, up to 120 a second while it moves and none while it
    is still, so their spacing has no usual value. Each picture is shown
    from the video frame nearest its time, within 8.3 ms. Of pictures
    less than a sixtieth of a second apart, only the last before each
    video frame is shown; the recording's PNG files and stream keep them
    all, and the export window says so.
  - **Checked (2026-11-09).** In the Linux build environment the
    solution builds with no warnings and the tests pass, 4 of them new:
    the phone's frames replace the desktop's, the desktop's win when
    both are present, the export's size for both shapes, and the
    phone's frames kept by the playback index and shown. On the
    development PC a check program opened test 23's recording
    (`20261109-153003-26acc41259cf41d6a09c4f7f78e3990f`, 131 frames,
    18.7 seconds) with the recorder's own code: frames from the phone,
    tall from 1.590 seconds, wide from frame 14 at 6.076 seconds, tall
    again from frame 83 at 15.656 seconds. Of its 131 pictures, an
    export would show 69 at 30 frames a second, 89 at 60, 110 at 90 and
    128 at 120. Its pictures are 4.2 ms apart at the least and 8.6 ms
    at the median.
  - **Checked at 60 and 120 a second (2026-11-09).** The check program
    saved the recording at 2304 by 2304 with Windows' encoder, the
    graphics card's and the software one, at 60 and at 120 frames a
    second. All four were saved: 16.1 and 16.0 MB in 16 seconds at 60;
    14.6 and 21.5 MB in 25 to 26 seconds at 120. FFmpeg reads them as
    H.264 High profile, level 5.2 at 60 a second and level 6.0 at 120,
    with 1,125 and 2,249 frames, and decodes them without errors.
    Windows decoded six moments of each through `Windows.Media.Editing`
    on the development PC. Microsoft documents its decoder only up to
    level 5.1
    ([Microsoft, H.264 video decoder](https://learn.microsoft.com/en-us/windows/win32/medfound/h-264-video-decoder)),
    so both levels are past what is documented, and level 6.0 further
    past it; that other players and phones play level 6.0 is not
    checked. Decided by the owner on 2026-11-09: the phone's export
    stays at 60 frames a second, level 5.2, though 120 would show almost
    every picture.
  - **Tested on the Pixel (2026-11-09).** Tests 24 to 28 of the
    validation plan passed, with the installer built from `697f624`:
    test 23's recording shown upright in both shapes in the player and
    in its MP4, and a recording of three apps played back in order.
    Slice 3 is complete.

### Slice 4: the phone's sound

- **Steps.** The slice is built in three steps, each tested on the Pixel
  before the next: 4a stores the sound with its times, shows its level
  and the quiet message while recording, plays it on the PC, and adds the
  PC's microphone; 4b gives it a "Phone sound" lane and slider in the
  player and makes the MP4 export treat it as the system sound; 4c raises
  the phone's Accessibility volume as the recording starts. The owner
  chose on 2026-11-09 to build 4c before 4b. Playing the sound on the PC and recording
  the PC's microphone are in the design ([Recording an Android
  device](android-recording.md), "Device sound", and the owner's answer
  "phone plus PC's mic") but were missing from this slice until 4a.
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
- **Volume over adb.** Settled from Android's source, step 4c: adb's
  shell cannot set the Accessibility volume, but the companion app can.
  Android's shell has `media_session volume --stream <n> --get` and
  `--set`
  ([AOSP, VolumeCtrl.java](https://github.com/aosp-mirror/platform_frameworks_base/blob/1cdfff555f4a21f71ccc978290e2e212e2f8b168/services/core/java/com/android/server/media/VolumeCtrl.java))
  and `cmd audio set-volume`
  ([AOSP, AudioManagerShellCommand.java](https://github.com/aosp-mirror/platform_frameworks_base/blob/main/services/core/java/com/android/server/audio/AudioManagerShellCommand.java)).
  Both reach the audio service's `setStreamVolume`, which ignores a
  change to the Accessibility volume, with only a warning in the log,
  unless the caller holds `CHANGE_ACCESSIBILITY_VOLUME` or is an
  accessibility service that is on (`canChangeAccessibilityVolume`,
  [AOSP, AudioService.java](https://github.com/aosp-mirror/platform_frameworks_base/blob/main/services/core/java/com/android/server/audio/AudioService.java)).
  The shell does not hold that permission
  ([AOSP, Shell's AndroidManifest.xml](https://github.com/aosp-mirror/platform_frameworks_base/blob/main/packages/Shell/AndroidManifest.xml)).
  Every accessibility service that is on is given that right
  (`updateServicesLocked`,
  [AOSP, AccessibilityManagerService.java](https://github.com/aosp-mirror/platform_frameworks_base/blob/main/services/accessibility/java/com/android/server/accessibility/AccessibilityManagerService.java)),
  so the companion app sets it. That setting it during the capture
  changes the captured level, as the volume keys do
  ([scrcpy issue 3790](https://github.com/Genymobile/scrcpy/issues/3790#issuecomment-1472079314)),
  was seen in test 30: the level fell from 36% to 14% when the owner
  moved the volume. That Android accepts the companion's change on the
  Pixel was checked by tests 33 to 36 on 2026-11-10: it raised 5 of 15
  steps (33%) to 12 of 15 (80%).
- **Player and export.** The phone's sound takes the system sound's place,
  labelled "Phone sound", with its own volume slider. The MP4 export
  treats it as the system sound, as the design says.
- **Target test.** Record 20 seconds while swiping through the Home
  screen with TalkBack on, raising Accessibility volume to about 80%
  during the recording. The level shown in the recorder rises after each
  swipe, and the quiet message goes once the volume is raised. In the
  player the timeline has a "Phone sound" lane across the whole
  recording, and the volume panel has a "Phone sound" slider. Steps 4a
  and 4b split this test in two.
- **Built, step 4a (2026-11-09).**
  - **Server.** The server is started with `audio=true`,
    `audio_codec=raw` and `audio_source=output`. It connects the audio
    socket after the video socket
    ([scrcpy 5.0.1, Server.java](https://github.com/Genymobile/scrcpy/blob/a60891aea193d92e7e5c3942700eca63f9d19a5f/server/src/main/java/com/genymobile/scrcpy/Server.java)),
    so `ScrcpyLink` accepts two connections in that order, removes the
    reverse tunnel after both, and stops the server's adb shell only
    when both are closed. The audio socket starts with a codec id, sent
    once the phone has started capturing, or 0 if it could not capture,
    or 1 for another error, and then a 12-byte header and up to 4096
    bytes (1024 sample frames) per packet
    ([AudioRawRecorder.java](https://github.com/Genymobile/scrcpy/blob/a60891aea193d92e7e5c3942700eca63f9d19a5f/server/src/main/java/com/genymobile/scrcpy/audio/AudioRawRecorder.java),
    [Streamer.java](https://github.com/Genymobile/scrcpy/blob/a60891aea193d92e7e5c3942700eca63f9d19a5f/server/src/main/java/com/genymobile/scrcpy/device/Streamer.java)).
    When the recorder closes the audio socket, the server stops
    capturing the sound, the phone plays its own sound again, and the
    screen goes on: the server treats a closed audio socket as expected
    and not as an error that ends it (AudioRawRecorder.java).
  - **Collector.** `AndroidSoundCollector`, `android.sound`, starts
    after the screen collector and stops before it. It writes each
    packet to `audio/device.wav` as received and records on
    `audio.device` the desktop audio collector's events:
    `audio-stream-started` at the first sample's time, an
    `audio-buffer` per packet (byte offset, sample frames, placed time,
    estimated first sample time), and `audio-stream-stopped` with the
    totals and the loudest sample. Each buffer also keeps the phone time
    as sent (`presentationMicroseconds`) and the time the recorder read
    it (`receivedAtNanoseconds`), so its placement can be checked again
    later.
  - **Times.** The server times each packet by Android's audio timestamp
    on the monotonic clock
    ([AudioRecordReader.java](https://github.com/Genymobile/scrcpy/blob/a60891aea193d92e7e5c3942700eca63f9d19a5f/server/src/main/java/com/genymobile/scrcpy/audio/AudioRecordReader.java)),
    the clock the companion app samples, so a packet is placed through
    the companion's clock fit, flagged `phone-audio-time`. That the time
    is that of the packet's last sample is inference: the server reads
    the timestamp just after reading the packet. The status's median
    delay from phone to recorder is how this is checked on the Pixel.
    With no clock sample within 3 seconds of the codec id, packets are
    placed at the time they were read, flagged as such.
  - **Level and message.** `PhoneSoundLevel` keeps each packet's
    loudest sample for 10 seconds. The status line reads, for example,
    "Phone sound: 512 packets, 2.1 MB, loudest in the last 10 seconds
    32% of full scale, median delay from phone to recorder 15.2 ms,
    playing on Speakers (Realtek(R) Audio)." When sound is present but
    its loudest sample in the last 10 seconds is below 10% of full
    scale, the collector's health reason is the quiet message, which the
    app announces once; it goes when the level passes 10%. Exact silence
    gives "silent in the last 10 seconds" and no message, as TalkBack is
    silent between swipes. The 10% is a choice between check 1's 2.9%,
    unusable, and 32.0%, usable; it has not been tried at other levels.
    Below 10% the level is shown with one decimal.
  - **Played on the PC.** `audio_source=output` "disables playback on
    the device"
    ([scrcpy, audio.md](https://github.com/Genymobile/scrcpy/blob/master/doc/audio.md)),
    so each packet is also played on Windows' default playback device
    (`WasapiPhoneSoundOutput`, shared mode, 60 ms latency). When more
    than 250 ms is waiting to be played, as after a pause in the
    packets, it is dropped so what is heard does not drift behind the
    phone; the recording keeps every packet. If the PC cannot play it,
    the status ends "not played on this PC:" with the reason, and the
    sound is still recorded.
  - **PC microphone.** A recording of the phone also records the PC's
    default microphone, with the desktop's audio collector set to the
    microphone only, whether or not the microphone box is checked. As
    for the desktop, a PC with no working microphone cannot start the
    recording.
  - **Failures.** If the screen stream did not start, if the phone could
    not capture its sound, or if the sound's connection closes during the
    recording, the collector reports itself degraded with the reason,
    records an `audio-stream-error`, and the recording goes on without
    the sound.
  - **Player and export.** Built in step 4b, below.
- **Built, step 4c (2026-11-09).**
  - **Decision.** The owner decided on 2026-11-09 that the recorder sets
    the Accessibility volume itself, as check 1 found TalkBack usable at
    about 80% and every recording needs it, and that it is left at that
    level when the recording ends.
  - **Request.** Protocol 2 of the companion app adds a request,
    `{"type":"accessibility-volume","minimumPercent":80}`. The companion
    collector sends it once per recording, when the sound collector reads
    the first sound packet. The app answers with one
    `accessibility-volume` message: whether the volume is separate, the
    services that make it so, its largest step, the step before, the
    target step, the step after, and the outcome. The target is the
    smallest step at or above 80% of the largest.
  - **When.** Android keeps a volume for each output, and `setStreamVolume`
    sets the volume of the output the stream plays to at that moment
    (`getDeviceForStream`, AudioService.java). While the sound is
    captured, the phone plays to the capture (remote submix) and not to
    its speaker, so the volume is set only once the capture is running.
    This explains check 1, where raising the volume before the run
    changed nothing and raising it during the run did; that explanation
    is inference from the source. A consequence, also inference: the
    phone's own speaker volume is not changed, and the 80% stays with the
    capture, so the next recording should find it "already" set.
  - **Raised only.** The app raises the volume to the target if it is
    lower, and never lowers it ("already"). It reads the volume again
    after setting it, as Android ignores a change it does not allow
    without an error, and says "refused" if it did not change.
  - **Only when separate.** The Accessibility volume is a volume of its
    own only while a service that asks for one
    (`FLAG_ENABLE_ACCESSIBILITY_VOLUME`), such as TalkBack, is on
    (`isAccessibilityVolumeStreamActive`,
    [AOSP, AccessibilityManager.java](https://github.com/aosp-mirror/platform_frameworks_base/blob/main/core/java/android/view/accessibility/AccessibilityManager.java)).
    Otherwise Android treats it as the media volume (`updateA11yVolumeAlias`,
    AudioService.java), and setting it would raise the media volume. So
    with TalkBack off the app changes nothing and says "not-separate".
  - **During the recording.** On Android 13 and later the app listens
    for accessibility services being turned on and off. When the volume
    becomes separate, or stops being separate, as when TalkBack is
    turned on or off during a recording, it does the same again and
    sends another message, with the reason "services-changed".
  - **Recorded.** Each answer is an `accessibility-volume` event on
    `android.companion`, placed by the companion's clock fit, and the
    companion's status line ends with it, for example "Accessibility
    volume raised from 9 of 25 steps (36%) to 20 of 25 steps (80%)", or
    "Accessibility volume not changed: no service that uses it, such as
    TalkBack, is on, so it is the media volume". The quiet message of
    step 4a stays, for a volume lowered during the recording.
  - **Older companion.** A companion app of protocol 1 is refused with
    "speaks protocol 1; this recorder speaks 2", and the checks panel
    offers to install the recorder's own, as its version code is lower.
  - **Tested (2026-11-10).** Tests 33 to 36 passed on the Pixel: the new
    companion installed with protocol 2; a volume lowered to 5 of 15
    steps during one recording was raised to 12 of 15 (80%) at the start
    of the next, and the slider read 80%; with TalkBack off the volume
    was left alone; and TalkBack turned on during a recording was
    followed by "already 12 of 15 steps (80%)". A volume set during one
    capture was what the next capture found, which fits the volume
    staying with the capture; that the speaker's volume is kept apart is
    still inference. Step 4c is complete.
- **Built, step 4b (2026-11-10).**
  - **Which recordings.** A recording with no system sound track whose
    picture is the phone's, or which has a phone sound track, is a
    recording of a phone. A recording has the phone's sound or the
    system sound, never both, as a recording of a phone records the PC's
    microphone only.
  - **Lane.** The `audio.device` events are drawn on the system sound's
    lane, as the phone's frames are drawn on the desktop frames' lane.
    The timeline's lanes have no names of their own; the list of lanes
    at the right of the status line at the bottom of the window ends
    "Mic | Phone sound" while a recording of a phone is open.
  - **Filter.** The system sound's filter covers `audio.device` and is
    labelled "Phone sound", so the phone's packets are no longer counted
    under "Other".
  - **Slider.** The system sound's slider is labelled "Phone sound:",
    with Alt+H, and named "Phone sound playback gain" for screen
    readers. It sets the phone sound's playback level, which it already
    did unlabelled in step 4a. A recording of a phone has no system
    sound, so the slider is the phone sound's own.
  - **MP4 export.** The export window offers "Phone sound" in the system
    sound's place, mixed at the slider's level, and its own track
    follows the microphone's. The export reads the phone's packet
    records from the recording file, as it reads the desktop's, and
    places them from their session times (`AudioPlacement`), with no
    change for the phone time and read time the records also keep.
  - **Not changed.** The "Screen reader" slider and filter are still
    shown for a recording of a phone, which has no screen reader track,
    so they change nothing. TalkBack's speech is part of the phone's
    sound.
  - **Tested (2026-11-10).** Tests 37 to 41 passed on the development
    PC with a recording of the Pixel: the note, the green "Phone sound"
    band under the microphone's, the "Phone sound:" slider and the lane
    list; the filter hid 8,124 records, the phone sound's 8,122 packets
    and its start and stop; the export window offered "Phone sound,
    mixed at -12 dB" from the slider; and the MP4 had 3 sound tracks,
    "mixed, then microphone, phone sound". Step 4b is complete, and
    with it slice 4.

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
- **Fixtures.** Check 4's file holds only the touch screen's events and
  their times, and can be used as a test fixture.
- **Player.** Touches are drawn on the phone's picture as the pointer is
  drawn now: a circle for each finger while it is down, numbered when
  there is more than one, with its recent path.
- **Target test.** Record three taps at the top, middle and bottom of the
  screen, then a two-finger swipe. In the player a circle appears at each
  tap's place on the phone's picture, and two numbered circles move
  together during the swipe.
- **Steps (planned 2026-11-10).** As for slice 4, the slice is built in
  two steps, each with its own tests.
  - **Step 5a: read and store.** The touch collector, its three channels
    and their records, the touch screen chosen at the start, and the
    status line, with every line `getevent` printed kept. The player
    shows the records only in the inspector and on the timeline's
    "Other" lane.
  - **Step 5b: show.** The player draws the touches on the phone's
    picture, the touch channels get their own lane and filter, and the
    target test above is run. A test with the phone turned sideways
    checks the rotation.
- **Built, step 5a (2026-11-10).**
  - **Collector.** `AndroidTouchCollector`, `android.touch`, starts after
    the sound collector. At the start it runs
    `adb -s <serial> shell getevent -lp` and `adb -s <serial> shell wm size`,
    then `adb -s <serial> shell -tt getevent -lt` for the whole
    recording. Given no device, getevent reads every input device and
    begins each line with the device's path
    ([AOSP, getevent.c](https://android.googlesource.com/platform/system/core/+/refs/heads/main/toolbox/getevent.c)),
    so one command gives both the touches and the keys.
  - **Touch screen.** The touch screen is the first device with
    `ABS_MT_POSITION_X`, `ABS_MT_POSITION_Y` and `INPUT_PROP_DIRECT`, the
    property of a device whose positions are on the screen itself
    ([Linux, input event codes](https://www.kernel.org/doc/html/latest/input/event-codes.html));
    failing that, the first with both axes, as the feasibility script
    chose it. With none, the keys are still read and the collector is
    degraded: "The phone's touches could not be recorded: no touch screen
    was found in what "getevent -lp" listed. The recording goes on
    without them."
  - **Three channels.** `input.android.raw` keeps what `getevent -lp`
    printed (`input-devices`, with the touch screen, its ranges and the
    upright screen size), every later line (`getevent-lines`, one record
    for each device's report up to and including its `SYN_REPORT`, or one
    for a line that is not an event, such as a device added), and why
    input is missing (`input-unavailable`). `input.android.touch` has a
    `touch-report` for each report of the touch screen with a finger on
    it, or a finger lifted. `input.android.key` has a `hardware-key` for
    each `KEY_` press, release or repeat of any device, with the
    device's name from `getevent -lp`. The plan named two channels; the
    keys have a third so that the player can show and filter them apart
    from the touches.
  - **Fingers.** Only the multi-touch protocol B is read: `ABS_MT_SLOT`
    chooses a slot, a tracking id puts a finger in it and -1 takes it
    out, and each axis keeps its value until the panel sends a new one
    ([Linux, multi-touch protocol](https://www.kernel.org/doc/html/latest/input/multi-touch-protocol.html)).
    Each finger in a report has its slot, tracking id, action (down when
    its tracking id was set in the report, up when cleared, otherwise
    move), position in the panel's units, position on the upright
    screen, position on the screen as shown, and pressure and touch size
    when the panel sends them. A phone without protocol B has its lines
    kept and no touch reports; the collector says so in its
    capabilities (`multi-touch-protocol-b-only`).
  - **Pixels.** A position is scaled from its axis range,
    (value - min) x size / (max - min + 1), to the size `wm size` gives,
    or, without it, the display mode's size from the companion app; it
    is kept to a tenth of a pixel. It is then turned by the display's
    rotation from the companion app at the report's phone time: at 90
    degrees x and y swap and y is reversed, at 180 both are reversed,
    and at 270 they swap and x is reversed, as Android's input reader
    turns them (`rotateAndScale`,
    [AOSP, TouchInputMapper.cpp, Android 11](https://android.googlesource.com/platform/frameworks/native/+/refs/heads/android11-release/services/inputflinger/reader/mapper/TouchInputMapper.cpp)).
    That Android 17 still turns them this way, and that the input
    reader scales to the size `wm size` reports, are inference; step 5b
    tests the rotation on the Pixel. With no display state, the position
    is given upright and flagged `rotation-unknown`.
  - **Times.** getevent sets each device to CLOCK_MONOTONIC (getevent.c,
    `EVIOCSCLOCKID`;
    [Android, getevent](https://source.android.com/docs/core/interaction/input/getevent)),
    the clock the companion app samples, so each record is placed
    through the companion's clock fit, flagged `phone-input-time`, and
    keeps the phone time and the time the recorder read it. With no
    clock sample within 3 seconds of starting, records are placed at the
    time they were read, flagged `receipt-time-stamp`. Each channel's
    times are held so they never go backward (`clock-fit-held`).
  - **Terminal.** getevent unbuffers its output (getevent.c,
    `setbuf(stdout, NULL)`), yet check 4 found its lines held back when
    read through a pipe, so the terminal is kept. Stopping the recording
    stops adb; that this ends getevent on the phone, as its terminal
    closes, is inference: adb leaves a terminal's input open until the
    shell ends
    ([AOSP, shell_service.cpp](https://android.googlesource.com/platform/packages/modules/adb/+/refs/heads/main/daemon/shell_service.cpp)).
  - **Status.** The status line reads, for example, "Touches: 3 touches,
    25 reports, last up at x 613, y 2155 of 1080 by 2404, 1 hardware key
    press, median delay from phone to recorder 12.0 ms, touch screen
    /dev/input/event3 "fts_ts" with x 0 to 13439 and y 0 to 29919." A
    press is counted when a key goes down. The delay is measured for
    touch reports only. The ranges are given so that test 43 reads the
    Pixel's own from the screen; the numbers here are made up.
  - **Failures.** If the devices cannot be listed or getevent cannot
    start, the collector is degraded with "The phone's touches could not
    be recorded: ..."; if getevent ends during the recording, with "The
    phone's touches stopped being recorded during the recording. ...".
    The recording goes on in both.
  - **Not stored as evidence.** As in step 4a, nothing is added to the
    evidence tables, as those are not written for a recording with a
    recording file (session-database.md), so there is no migration.
  - **Fixture.** `tests/Recorder.Tests/TestData/android-touch-check4.txt`
    is check 4's file. The check did not save the axis ranges, so the
    tests use ranges that turn its three taps into the positions the
    check reported, to within half a pixel. Test 43 read the Pixel's own
    ranges on 2026-11-10, x 0 to 13439 and y 0 to 29919, the same as the
    tests use.
  - **Tested (2026-11-10).** Tests 42 to 44 passed on the Pixel: the
    note; three taps recorded at the top, middle and bottom with one
    volume key press, a median delay from phone to recorder of 5.4 ms,
    and the touch screen `/dev/input/event1` "synaptics_tcm_touch"; and
    two fingers held still followed together, "with 2 fingers". Test 43
    also recorded a touch before the taps, a short drag, which is
    inference to have been the tester's. Held fingers send over 100
    reports a second, each kept. Step 5a is complete.
- **Built, step 5b (2026-11-10).**
  - **Kept for playback.** The playback index, version 21, keeps each
    touch report in a compact form: the report's rotation and screen
    size, and each finger's slot, tracking id, action and position on
    the screen as shown (`PhoneTouchTimeline.Compact`). The positions in
    the panel's units and on the upright screen, and the pressure and
    sizes, stay in the recording file. An older index is derived again
    when a recording opens, so recordings made with step 5a draw their
    touches.
  - **Fingers at a time.** `PhoneTouchTimeline` gives the fingers down
    at a time: those down or moving in the last report at or before it.
    A finger lifted in that report is not drawn. Fingers are numbered
    from 1 in the order they went down, by tracking id, as the panel
    gives each new finger a larger one; a finger's slot can be reused.
    Each finger's path is its positions over the last second, oldest
    first, starting no earlier than the report it went down in.
  - **Drawn.** At every position of the playhead, as fingers move
    between the phone's frames, the player draws each finger over the
    frame: a circle about a fingertip across, 80 pixels on a screen
    1,080 wide (with a least size on the PC's screen, see "Built, step
    5b, larger circles"), with a yellow ring over a black one as the
    Magnifier outline is drawn, so it shows on light and dark content, and a
    dimmed middle. With more than one finger, each circle holds its
    number in yellow. The path is a yellow line over a black one, drawn
    under the circles. Positions are scaled from the screen size the
    report gives to the frame's size; a report made sideways has a
    sideways size, as does the frame. The touches are drawn by the
    player; the frames are not changed, and an MP4 export does not draw
    them. The picture's help text gains the fingers in words, for
    example "2 fingers on the screen, 1 at x 301, y 1459, 2 at x 702,
    y 1462".
  - **Lanes and filters.** A recording of a phone has no keyboard or
    mouse, so, as the phone's sound took the system sound's place in
    step 4b, the touches (`input.android.touch`) take the mouse's lane
    and filter, and the hardware keys (`input.android.key`) the
    keyboard's, in their blue and teal. While a recording of a phone is
    open, the filters read "Touches" and "Phone keys", and the list of
    lanes at the right of the status line reads "Phone keys | Touches |
    UIA | Window | Frames | Mic | Phone sound". "Phone keys" keeps the
    keyboard filter's access key, Alt+K; "Touches" has none, as every
    letter of it is taken in the window. The raw lines
    (`input.android.raw`) stay on the "Other" lane.
  - **Rotation.** The sideways test checks the turn step 5a took from
    Android 11's input reader on the Pixel: a tap on a control of a
    sideways app is drawn on that control.
- **Tested, step 5b (2026-11-10).** On the Pixel, tests 45 to 47 of the
  validation plan passed. Three taps were drawn where they were made,
  and a two-finger swipe as two numbered circles with their paths. The
  two fingers were not level, as the index and middle fingers seldom
  go down level, so test 46's check no longer asks for that. A tap on
  Chrome's menu button with the phone turned top to the left was drawn
  on the button, so the turn from Android 11's input reader holds on
  Android 17 for that turn; the other two turns were not tested. With
  the picture shown about 157 pixels wide, the circles were about 12
  pixels across on the PC's screen, and hard to see.
- **Built, step 5b, larger circles (2026-11-10).** A circle is still
  about a fingertip across on the phone's picture, but no smaller than
  24 device-independent pixels across on the PC's screen, with its
  rings and the paths at least 2 thick, however small the picture is
  shown. The player works out how large one of the frame's pixels is
  shown from the picture's width on the screen, and draws the touches
  again when that width changes. Only the drawing changed: the playback
  index stays at version 21, and recordings already made show the new
  size.

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
  TalkBack, explore by touch, magnification, color inversion, color
  correction, font scale, display size, and the text-to-speech rate.
  TalkBack is often off in a recording, as the owner tests magnification,
  visual layout and colour without it (2026-11-08), so these settings
  are as much the recording's subject as TalkBack is. After that, the companion app's content
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
| `adb install` avoids the restricted settings block | Checked for the recorder's way: Android 17 on the Pixel accepted `settings put` for the app installed with `adb install` (test 11) | slice 2 |
| Accessibility volume can be read or set over adb | Settled from Android's source: adb's shell cannot set it; the companion app can, as an accessibility service that is on. Checked on the Pixel by tests 34 to 36 | slice 4, step 4c |
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
