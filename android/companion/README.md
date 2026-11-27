# Companion app

The Android app the recorder installs on the phone over USB. It is built by
`scripts/Build-CompanionApp.ps1`, which the installer build runs, straight
from Google's Android SDK tools: `javac`, `d8`, `aapt2`, `zipalign` and
`apksigner`. There is no Gradle project, so every file the build downloads is
pinned and checked by its SHA-256.

- `AndroidManifest.xml`: the app and its accessibility service.
- `res/`: the service configuration and the text the phone shows.
- `src/`: the Java source.

What the app does, and the protocol it speaks with the recorder, are in
`docs/architecture/android-implementation-plan.md`, slices 0, 2 and 7.
`src/Recorder.Collectors.Android/CompanionProtocol.cs` is the recorder's
side of the protocol and must agree with `RecorderLink.java`.

The protocol 10 navigation request is only for an explicitly started
TalkBack exploration. It requests a direction in the already active
app window, not a tap, key, node action, or arbitrary path. The swipe
acknowledgement reports dispatch, not a successful TalkBack focus move.
