# Windows installer and configurable local models

Proposed and agreed 2026-11-06. Built on the `installer` branch and merged
to `main`. Not yet run on a Windows PC; see
[the validation plan](../validation/windows-installer-plan.md).

## Purpose

Until now the recorder ran only on the development PC, where it was built,
where the instrumented Chromium was compiled, and where llama.cpp and the
model files were placed by hand. The owner wants to run it on other, more
powerful PCs, first an Alienware gaming PC with an NVIDIA RTX 3090, and
there to try other local models to compare the quality of the reports.

Two features follow:

1. An installer that puts everything the recorder needs on a PC that has no
   development tools.
2. Model setup inside the app: choosing, downloading and configuring models
   for the PC's graphics card, without editing `analysis-models.json` in
   Notepad or running a script.

## Decisions

The owner's answers to the proposal, 2026-11-06:

| Question | Decision |
|---|---|
| Installer tool | Inno Setup ("I guess we go with inno setup") |
| llama.cpp | Included in the installer. "I expect the installer to produce a fully working app that I can start. There maybe some config after install, but the install should be as comprehensive as possible including model selection and download either as part of the install or as config setup" |
| Other local servers (Ollama, LM Studio, vLLM) | Not now: "Stick with what we have for now (llama.cpp)" |
| Branch | "We need to commit and merge any existing branches and then the installer goes to main". `analysis-runner` was merged to `main` (a fast-forward), and the installer work was built on `installer` and merged to `main` after it. |

Following from the second answer, model downloads moved from "not in the
first version" in the proposal into the first version, as part of the
configuration after the install (see "Model setup").

## What the app needs at run time

| Part | Where the app looks for it | How the installer provides it |
|---|---|---|
| The player and the analysis worker (.NET 10, WPF) | One folder; the player starts `Recorder.AnalysisWorker.exe` from its own folder (`AnalysisPlayer.WorkerFileName`) | Published self-contained for `win-x64`, so the PC needs no .NET run time |
| PostgreSQL server | `pgsql\` beside the programs (`Recorder.App.csproj`, `RecorderDatabaseLocation`) | The binaries `scripts\Get-PostgresBinaries.ps1` fetches, copied by the publish |
| The catalog's data | `%LOCALAPPDATA%\Windows A11y Recorder` | Created on first start |
| Recordings | `Documents\Windows A11y Recorder` by default | Created on first recording |
| Instrumented Chromium | `browser\chrome.exe` beside the programs (`RecreationBrowser.DefaultExecutablePath`) | The run-time files of the development PC's `out\A11yRecorder` build |
| llama.cpp server | `localServer.executable` in `analysis-models.json` | `llama\llama-server.exe`, with the CUDA run time beside it; the app points the setting at it (see "First start") |
| Model files (GGUF) and image add-ons (mmproj) | Each profile's `modelPath` and `imageAddOnPath` | Downloaded by the app's model setup, or chosen from the PC's disk |
| Model profiles | `analysis-models.json` in `%LOCALAPPDATA%\Windows A11y Recorder` | Written by model setup and the model profiles window |
| Microsoft Visual C++ x64 run time | Windows | llama.cpp's programs import `vcruntime140.dll`, `vcruntime140_1.dll` and `msvcp140.dll`; the installer runs Microsoft's redistributable when it is missing |
| NVDA | Its own installation | Installed separately; not part of the recorder |

## The installer

### What it installs

Into `%LOCALAPPDATA%\Programs\Windows A11y Recorder`:

- The player and the analysis worker, with the .NET run time, in one
  folder. The command-line capture host (`Recorder.CaptureHost.exe`) is not
  installed: the player records by itself, and the capture host is used only
  by the development validation scripts. Published separately, it ships
  different copies of four DLLs from the player's
  (`Microsoft.Extensions.Logging.Abstractions.dll`,
  `Microsoft.Extensions.DependencyInjection.Abstractions.dll`,
  `Microsoft.VisualBasic.dll`, `System.Drawing.dll`), so it could not share
  the player's folder.
- `model-catalog.json`, the list of models model setup offers.
- `pgsql\`: the PostgreSQL server programs, libraries and licences.
- `browser\`: the instrumented Chromium's run-time files, not the whole
  `out\A11yRecorder` folder, which also holds object files and symbols.
  The list of files is Chromium's own archive list,
  `infra\archive_config\win-archive-rel.json` in the Chromium checkout (its
  first archive, without `setup.exe` and the test program
  `interactive_ui_tests.exe`).
- `llama\`: llama.cpp b11607 for Windows x64 with CUDA 12.4, and the CUDA
  12.4 run-time files llama.cpp publishes beside it
  ([llama.cpp release b11607](https://github.com/ggml-org/llama.cpp/releases/tag/b11607)).
- `android\`: the scrcpy server and the Android companion app, for
  recording an Android phone (see "Android files").
- `THIRD-PARTY-NOTICES.txt`, naming each included component and where its
  licence is, and the licences themselves beside the components.

Model files are not in the installer. They are 15 to 35 GB each, and the
point of the Alienware trial is to choose them there.

### How it behaves

- **Per user, no administrator rights** (`PrivilegesRequired=lowest`). The
  recorder's data is already per user, and the keyboard and mouse hooks do
  not need elevation. Start menu entries for the recorder and for the
  third-party notices; a desktop shortcut is optional.
- **64-bit only, Windows 10 version 2004 or later**: allowed only on x64
  Windows, installing in 64-bit mode (`x64os`), with
  `MinVersion=10.0.19041`, the Windows version the programs target.
- **Upgrades in place.** A newer installer closes the running recorder,
  removes the old `browser`, `llama` and `pgsql` folders and the programs'
  files, and installs the new ones, so no file from an older build is left
  behind. The catalog, the recordings, the reports, the models and
  `analysis-models.json` are outside the program folder and are kept.
- **Uninstall keeps the data**, and says where it is: the catalog, model
  settings and downloaded models in `%LOCALAPPDATA%\Windows A11y Recorder`,
  and the recordings in `Documents\Windows A11y Recorder`.
- **Visual C++ run time.** The installer reads
  `HKLM\SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64`, value
  `Installed`. When it is not 1, the Ready page says so, and after the files
  are copied the installer runs Microsoft's `vc_redist.x64.exe /install
  /quiet /norestart`, which Windows asks permission for, because it installs
  for every user. If it is still missing afterwards, the installer says so
  and where to get it; if it asks for a restart (result 3010), the installer
  says to restart before running an analysis.
- **The browser's sandbox can read its folder.** Chromium starts some of
  its processes, the network service among them, in an AppContainer, and
  first checks that the container can read and run `chrome.exe`; when it
  cannot, the browser stops as it starts, logging "Sandbox cannot access
  executable"
  ([Chromium, sandbox_win.cc](https://raw.githubusercontent.com/chromium/chromium/main/sandbox/policy/win/sandbox_win.cc)).
  Chromium's build grants this on its output folder
  ([Chromium, set_lpac_acls](https://raw.githubusercontent.com/chromium/chromium/main/testing/scripts/common.py)),
  but a folder under `%LOCALAPPDATA%\Programs` does not have it. After the
  files are copied, the installer runs `icacls` on `browser\`, granting
  read and run, inherited by everything in it, to ALL APPLICATION PACKAGES
  (`S-1-15-2-1`) and ALL RESTRICTED APPLICATION PACKAGES (`S-1-15-2-2`). If
  that fails, it says so and gives the command to run. Found on the
  development PC on 2026-11-07, when "Inspect page at this frame" failed
  with the installed browser.
- **NVIDIA check.** When `nvidia-smi.exe` is not in the Windows system
  folder, the Ready page warns that the models will be very slow or fail
  without an NVIDIA graphics card. The install continues.
- **Last page**: "Start the recorder and set up the analysis models",
  checked, which starts the recorder with `--setup-models`.
- **Not code-signed, for now.** Windows SmartScreen warns when the installer
  is first run ("More info", then "Run anyway"). Signing is a separate
  decision.

### First start

At every start, before the analysis worker starts, the app checks
`localServer.executable` in `analysis-models.json`
(`MainWindow.UseInstalledModelServer`, `ModelSetup.WithBundledServer`):

- If `llama\llama-server.exe` is installed beside the programs, and the
  setting is empty or names a file that does not exist, the setting is
  pointed at the installed server, and the port is set to 8091 if none is
  set. The file is written only when something changes.
- A server the owner chose that exists is kept. On the development PC, the
  hand-installed b11607 server stays in use.

After the window opens, model setup opens by itself when the app was
started with `--setup-models` (the installer's last page). Otherwise, when
there is no model profile and the settings file has no problems, the app
asks whether to set up a model now, and says where to find it later
(Analysis menu, "Set up models...").

### Where it is built

The installer has to be built on the development PC, because that is the
only place the instrumented Chromium build exists.
`scripts\Build-Installer.ps1` does every step and stops at the first
failure:

1. Finds the .NET SDK (in PATH or `%USERPROFILE%\.dotnet`) and Inno Setup
   6.3 or later (in PATH, where Windows records it was installed, or an
   "Inno Setup" folder in Program Files or `%LOCALAPPDATA%\Programs`).
2. Fetches or reuses the pinned PostgreSQL binaries.
3. Publishes the player, which brings the analysis worker beside it,
   self-contained for `win-x64`, and checks PostgreSQL and the model list
   are beside them.
4. Copies the Chromium run-time files and runs
   `scripts\Test-BrowserProtocolQuery.ps1` on the copy, which checks the
   copied browser reports the protocol version the recorder speaks. The
   query starts no sandboxed process, so it does not show the sandbox can
   read the installed folder; validation test 6b does.
5. Downloads the llama.cpp b11607 CUDA 12.4 archive and the CUDA run-time
   archive, checks their sizes and SHA-256 against the values in the
   script, unpacks them to `llama\`, and runs `llama-server.exe --version`.
6. Downloads the scrcpy 5.0.1 server and its licence, checks their sizes
   and SHA-256, and copies them to `android\`; then builds the Android
   companion app with `scripts\Build-CompanionApp.ps1` and copies it, with
   its description `companion-app.txt`, to `android\`. The first build
   asks the owner to accept the Android SDK licence, which the app's build
   tools are used under; `-AcceptAndroidSdkLicense` accepts it without
   asking. See "Android files" below.
7. Downloads `vc_redist.x64.exe` from Microsoft
   (`https://aka.ms/vs/17/release/vc_redist.x64.exe`) and checks it has a
   valid Microsoft signature. The address always gives the latest version,
   so it is checked by signature rather than a fixed hash.
8. Writes `THIRD-PARTY-NOTICES.txt`, including each NuGet package with the
   licence its `.nuspec` declares.
9. Compiles `installer\WindowsA11yRecorder.iss` into
   `artifacts\installer\output\WindowsA11yRecorder-Setup-<revision>.exe`,
   and writes its SHA-256 beside it.

The revision is the commit's short hash, with `-modified` added if the
checkout has uncommitted changes. Downloads are kept in
`artifacts\installer\downloads`, so a second build does not repeat them.

### Android files

Part 0 of the
[Android implementation plan](android-implementation-plan.md) adds an
`android\` folder to the installation:

- `scrcpy-server-v5.0.1`, which records the phone's screen and sound
  (Apache License 2.0), with `LICENSE-scrcpy.txt`.
- `a11yrecorder-companion.apk`, the companion app, and
  `companion-app.txt`, which gives its package, versions, SHA-256 and
  signing certificate. The recorder checks the app against that SHA-256.

adb is not in the installation. The Android SDK licence does not allow it
to be redistributed
([Android, SDK terms 3.4](https://developer.android.com/studio/terms)), so
the recorder downloads it from Google in its Android setup window (Android
menu, "Set up Android recording..."), after the person accepts the licence,
into `%LOCALAPPDATA%\Windows A11y Recorder\android\platform-tools`.

The companion app is built straight from Google's SDK tools, without
Gradle: `aapt2` compiles and links the resources, `javac` compiles the
Java source against the platform's `android.jar`, `d8` converts it to
Android byte code, and `zipalign` and `apksigner` finish it. Gradle and the
Android Gradle plugin fetch many further files from Maven repositories on
each new machine; without them the build downloads exactly three files,
each pinned and checked by SHA-256:

| File | From | Size (bytes) |
| --- | --- | --- |
| Eclipse Temurin JDK 21.0.13+9 | [Adoptium](https://adoptium.net/) | 205,421,281 (Windows), 207,858,352 (Linux) |
| Android SDK Build-Tools 37.0.0 | [Google's repository list](https://dl.google.com/android/repository/repository2-3.xml) | 61,077,164 (Windows), 66,135,704 (Linux) |
| Android SDK platform 37.0, revision 2 | the same list | 67,281,901 |

The SHA-1 values in Google's list matched the downloads on 2026-11-08,
when the SHA-256 values in the script were computed. The JDK's SHA-256
values are the ones the Adoptium API gives.

The app is signed with a key made on the first build in
`%LOCALAPPDATA%\Windows A11y Recorder Build\companion-signing`, outside the
repository, with a random password kept beside it. Android only lets an
app be replaced by one signed with the same key, so that folder should be
kept. Its version code is the number of commits on the branch, which only
grows, because Android refuses to replace an app with one of a lower code.

The same script runs in the Linux build environment under PowerShell 7,
with the Linux JDK and Build-Tools, so changes to the app are built and
checked there before they reach the development PC.

### Installer tool

Inno Setup builds one `Setup.exe`, supports a per-user install without
administrator rights, copies folder trees recursively without listing every
file, and its licence allows use "for any purpose, including commercial
applications" ([Inno Setup licence](https://jrsoftware.org/files/is/license.txt)).

The script works with Inno Setup 6.3 and later, including 7. On
2026-11-06 winget's `JRSoftware.InnoSetup` installed 6.7.3 on the
development PC, not 7. The script therefore does not use Inno Setup 7's
64-bit Setup program (`SetupArchitecture=x64`,
[Inno Setup help](https://jrsoftware.org/ishelp/topic_setup_setuparchitecture.htm)):
Setup itself is 32-bit, and it installs the 64-bit programs in 64-bit mode.

The setup script was compiled with Inno Setup 6.7.3 and 7.1.1, and builds
from placeholder files were installed and removed under Wine, each in
64-bit install mode; that shows the script compiles and its code runs, not
that it behaves correctly on Windows.

### llama.cpp build

llama.cpp publishes Windows x64 server builds for CUDA 12.4 and CUDA 13
(b11607 has `llama-b11607-bin-win-cuda-12.4-x64.zip`, 252 MB, and
`cudart-llama-bin-win-cuda-12.4-x64.zip`, 373 MB, on its
[GitHub release](https://github.com/ggml-org/llama.cpp/releases/tag/b11607)).
The installer pins **b11607 with CUDA 12.4**, because that is the build
every trial so far used, and the RTX 2060 and the RTX 3090 can both run
it. Moving to a newer build is a separate, measured change, since the
worker relies on router mode and presets.

## Model setup

A window (`ModelSetupWindow`), opened from the installer's last page, from
the first-start question, from Analysis, "Set up models...", and from the
model profiles window.

### What it shows

- **This PC**: each graphics card's name and memory, from `nvidia-smi`
  for NVIDIA cards and, for any other display adapter, as Windows records
  it in the registry (`HardwareInformation.qwMemorySize`); the installed
  memory; and the free space in the models folder.
- **Models folder**: where downloads go, by default
  `%LOCALAPPDATA%\Windows A11y Recorder\models`. Changeable, and kept in
  `analysis-models.json` as `modelsFolder`. Files are saved under
  `<folder>\<owner>\<repository>\<file>`, as Hugging Face names them.
- **The list of models** from `model-catalog.json`: Qwen3.6 35B A3B (four
  quantizations), Qwen3.6 27B (three), Gemma 4 26B A4B (two) and Gemma 4
  31B (two), each with its size, licence, source and how it compares with
  this PC's memory (below), and an "also for images" choice that adds its
  image add-on. When there are no profiles yet, the first model, Qwen3.6
  35B A3B UD-Q4_K_M, the one the development PC uses, is chosen.

### How a model is compared with the PC

`FitComparison` compares the file's size with the memory, and says only
what follows from the sizes:

- larger than the graphics memory and the PC's memory together, less 4 GB
  for Windows: it cannot be loaded here;
- no NVIDIA driver: it would run on the processor only, many times slower;
- at most the graphics memory less 2 GB: it can all go on the card, with
  room for the context;
- otherwise: part of it runs from the PC's memory, which is slower.

The window does not estimate speed or recommend settings. The speed and
quality of a model on a PC are measured by analysing a recording with it.

### Downloads

- From the pinned commit of each Hugging Face repository, so a file cannot
  change under the same address.
- Resumed after a stop or a lost connection: the file is written as
  `<name>.partial` and continued with an HTTP range request. Up to five
  retries, with a growing wait, after a lost connection, a server error or
  "too many requests"; any other refusal is not retried.
- Checked against the size and SHA-256 in the list before the file is
  renamed into place. The verified hash is written beside the file as
  `<name>.sha256` in `sha256sum` format, so a file already downloaded is
  not hashed again.
- Refused before starting when the folder's drive would have less than
  1 GB left.
- Progress shown as a bar and text, and announced to screen readers as a
  notification at each tenth.

### Adding a model not in the list

- **Hugging Face address**: a file's address on Hugging Face (a `blob` or
  `resolve` link). The app asks Hugging Face for the file's size, SHA-256
  and current commit, then downloads it from that commit and checks it like
  a listed model. An image add-on address can be given with it.
- **File on this PC**: a `.gguf` already on the disk, with an optional
  image add-on. The app computes its SHA-256 so the profile records it.

### What a model becomes

Each model becomes a profile for text and, with its image add-on, a second
profile for images ("<model>, with images", capability text and image,
`presence_penalty` 1.5). Where there is no default profile for text, or for
images, the first one added becomes it. Existing profiles and defaults are
not changed.

### Server arguments

New profiles use `-fa on -c 32768`: flash attention on and a 32,768-token
context. They leave the split between the graphics card and the processor
to llama.cpp, whose server at b11607 adjusts arguments that are not set
to fit in the devices' memory (`--fit`, default on, in the
[server README at b11607](https://github.com/ggml-org/llama.cpp/blob/b11607/tools/server/README.md)).
How well that suits the RTX 3090 is not yet measured. The development PC's
own profile keeps its measured `-ngl 99 -ncmoe 36`. Fixed layer counts can
be set per profile in the model profiles window and compared there.

## Model profiles window

Replaces "edit in Notepad" with an editor, keeping "Open the file in
Notepad" and "Read the file again" for anything it does not cover:

- the list of profiles, with what each takes and where it runs;
- for the chosen profile: the server arguments (its llama.cpp preset), the
  output limit, temperature, seed, and thinking on or off; "Save the
  profile's settings" and "Remove the profile";
- the default profile for text and for images, and "Save the defaults";
- "Set up models...", which opens model setup.

Each change is read with the app's own settings reader before it is
written, and refused if the profile it changed could not be read back.

A Claude profile, which sends to Anthropic, has its own settings in
place of the server arguments, temperature, seed and thinking, and the
window has "Add a Claude profile..." and "Check the profile"; see
[cloud models](cloud-models.md), "As built: step 2".

## Deferred

- **Try the profile**: loading the model with a profile's settings and
  measuring the graphics memory used and the prompt and generation speed.
  Deferred: an analysis of a short recording measures the same things with
  the real requests.
- **Run again with another profile**: the player already asks which
  profile to use when a task is queued, so comparing two models is two
  queued tasks.
- **Network endpoints, other servers, code signing, a newer llama.cpp.**
  Each is a separate decision.
