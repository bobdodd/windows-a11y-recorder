# Recording an Android device: validation plan

Written 2026-11-08 for slice 0 of
[the Android implementation plan](../architecture/android-implementation-plan.md):
the tools. Later slices add their tests here. Each test checks one
behaviour in at most five steps, with what to see on the screen.

What was checked before this plan, in the Linux build environment:

- The solution builds with no warnings, and the 9 tests of the Android
  tools pass: Platform-Tools unpacked, an earlier install replaced, another
  revision refused with the earlier install kept, files outside the
  archive's `platform-tools` folder refused, an archive without `adb.exe`
  refused, and the status of Platform-Tools, the scrcpy server and the
  companion app.
- `scripts/Build-CompanionApp.ps1` built the companion app under
  PowerShell 7 with the Linux JDK and Build-Tools. `aapt2 dump badging`
  shows package `io.github.bobdodd.a11yrecorder.companion`, minimum SDK 30,
  target SDK 37 and an accessibility service; `apksigner verify` and
  `zipalign -c` passed. Answering anything but `yes` at the licence
  question stopped the build.
- The real `platform-tools_r37.0.1-win.zip` matches the SHA-256 in the
  recorder, has all 15 entries inside `platform-tools/`, has the four files
  the installer requires, and says `Pkg.Revision=37.0.1`.

None of that shows the build under Windows PowerShell 5.1, the Android
setup window, adb running on Windows, or the app installing on the
Pixel. Tests 1 to 4 do.

## Before the tests

On the development PC, as for every installer build: the instrumented
Chromium build, the .NET 10 SDK and Inno Setup. Every PowerShell window is
a normal one, not "Run as administrator". The Pixel is needed only for
test 4.

## Test 1: build the installer (development PC)

1. Open PowerShell and allow scripts in this window:

   ```powershell
   Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass -Force
   ```

2. Get `main`:

   ```powershell
   $repo = Join-Path $env:USERPROFILE "windows-a11y-recorder"
   git -C $repo checkout main
   git -C $repo pull --ff-only
   ```

3. Build the installer (the Android files add about 340 MB of downloads
   the first time):

   ```powershell
   & (Join-Path $repo "scripts\Build-Installer.ps1")
   ```

4. When it asks "Type yes to accept it and continue", read the licence at
   the address it gives, then type `yes` and press Enter.
5. Check the end of the output.

**On screen:** under `== Android files`, the line `Bundled scrcpy server
5.0.1.`, then `COMPANION APP BUILT` with the app's path, `1.1.0, version
code` and a number, its SHA-256, and `Signing certificate SHA-256` with a
64-character value. The first build also shows `Making the companion app's
signing key in` with a folder ending `Windows A11y Recorder
Build\companion-signing`. The script finishes with `INSTALLER BUILT`. Any
red error text means it failed; send the whole output.

## Test 2: what the installer brings for Android

1. Run the new installer from `artifacts\installer\output` and go through
   its pages to Finish. Installing over the earlier install keeps the
   recordings, settings and models.
2. In the recorder, open the "Android" menu (Alt, then the arrow keys to
   it).
3. Choose "Set up Android recording...".
4. Read the window.

**On screen:** the window "Set up Android recording". Under "Android SDK
Platform-Tools": "Platform-Tools is not installed. It will be installed in
C:\Users\User\AppData\Local\Windows A11y Recorder\android\platform-tools."
The "Download and install Platform-Tools 37.0.1" button is unavailable.
Under "Included with the recorder": "The scrcpy server 5.0.1, for the
phone's screen and sound, is in place and checked:" and "The companion app
1.1.0, version code ..., for the phone's accessibility evidence, is in
place and checked:", each with a path in
`...\AppData\Local\Programs\Windows A11y Recorder\android\`.

## Test 3: download Platform-Tools

In the window from test 2:

1. Choose "Open the licence in the browser", and read it.
2. Check "I have read and accept the Android Software Development Kit
   License Agreement".
3. Choose "Download and install Platform-Tools 37.0.1".
4. Wait for the status line to stop changing.
5. Read the window.

**On screen:** in step 1, the browser opens
developer.android.com/studio/terms. After step 2 the download button is
available. At the end, the status line and the Platform-Tools line both
read "Platform-Tools 37.0.1 installed in
C:\Users\User\AppData\Local\Windows A11y Recorder\android\platform-tools.",
and the line under it begins "adb runs on this PC and reports: Android
Debug Bridge version". Any other status line is a failure; send it.

## Test 4: the companion app installs on the Pixel

The recorder installs the app itself from slice 2. This test only shows
the app the build makes is one Android accepts.

1. Connect the Pixel by USB, unlocked, with USB debugging on.
2. In PowerShell, install the app with the downloaded adb:

   ```powershell
   Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass -Force
   $adb = Join-Path $env:LOCALAPPDATA "Windows A11y Recorder\android\platform-tools\adb.exe"
   $apk = Join-Path $env:LOCALAPPDATA "Programs\Windows A11y Recorder\android\a11yrecorder-companion.apk"
   & $adb devices
   & $adb install -r $apk
   ```

3. On the Pixel, open Settings, then Accessibility.
4. Find "A11y Recorder companion" in the list of apps, and open it. Do
   not turn it on.

**On screen:** `adb devices` lists `58191FDCQ00ABZ` followed by `device`,
and `adb install` ends with `Success`. If the Pixel asks to allow USB
debugging from this PC, allow it and run the two `& $adb` lines again. On
the Pixel, the companion's page shows the description "Records
accessibility evidence for Windows A11y Recorder while a recording runs
over USB. It does nothing on the phone and sends nothing over the
internet." with its switch off.

## Results

- 2026-11-08, development PC: test 1 passed (installer
  `WindowsA11yRecorder-Setup-2f1f913.exe` built, with the `android` folder
  holding the companion app, its description, the scrcpy server and its
  licence). Tests 2 and 3 passed: the owner found the Android menu and
  Platform-Tools downloaded and installed. Test 4 passed: the companion
  app installed on the Pixel and "A11y Recorder companion" appeared in
  Settings, Accessibility, left off.
- Found during test 2: the menu bar is easy to miss. The owner looked for
  the Android menu in the side panel's sections before finding the thin
  strip at the top of the window. Making the menu bar much more visible is
  a later change, not part of the Android work. The menu's access key,
  Alt+D, is also used by "Desktop frames"; it gets a letter of its own in
  slice 1.
