# Companion app

The Android app the recorder installs on the phone over USB. It is built by
`scripts/Build-CompanionApp.ps1`, which the installer build runs, straight
from Google's Android SDK tools: `javac`, `d8`, `aapt2`, `zipalign` and
`apksigner`. There is no Gradle project, so every file the build downloads is
pinned and checked by its SHA-256.

- `AndroidManifest.xml`: the app and its accessibility service.
- `res/`: the service configuration and the text the phone shows.
- `src/`: the Java source.

See `docs/architecture/android-implementation-plan.md`, part 0.
