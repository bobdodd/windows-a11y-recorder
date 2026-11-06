# Windows installer and configurable local models

Proposed 2026-11-06. Not yet agreed by the owner. Nothing in this document
is built.

## Purpose

The recorder runs today only on the development PC, where it was built,
where the instrumented Chromium was compiled, and where llama.cpp and the
model files were placed by hand. The owner wants to run it on other, more
powerful PCs, first an Alienware gaming PC with an NVIDIA RTX 3090, and
there to try other local models to compare the quality of the reports.

Two features follow:

1. An installer that puts everything the recorder needs on a PC that has no
   development tools.
2. Model configuration inside the app, so that a model can be added,
   tuned for the PC's graphics card, and compared without editing
   `analysis-models.json` in Notepad or running a script.

## What the app needs at run time

Found in the code on `analysis-runner` at `39df8cb`:

| Part | Where the app looks for it | How it gets there today |
|---|---|---|
| The player, the capture host and the analysis worker (.NET 10, WPF) | One folder; the player starts `Recorder.AnalysisWorker.exe` from its own folder (`AnalysisPlayer.WorkerFileName`) | Built from source |
| PostgreSQL server | `pgsql\` beside the programs (`Recorder.App.csproj`, `RecorderDatabaseLocation`) | `scripts\Get-PostgresBinaries.ps1`, then copied by the build |
| The catalog's data | `%LOCALAPPDATA%\Windows A11y Recorder` | Created on first start |
| Instrumented Chromium | `browser\chrome.exe` beside the programs, else the development build under `%USERPROFILE%\chromium-dev` (`RecreationBrowser.DefaultExecutablePath`) | Compiled on the development PC (`chromium\setup-windows.ps1`, `out\A11yRecorder`, release, not a component build) |
| llama.cpp server | `localServer.executable` in `analysis-models.json` | Unzipped by hand (b11607, CUDA 12.4) |
| Model files (GGUF) and image add-ons (mmproj) | Each profile's `modelPath` and `imageAddOnPath` | Downloaded by hand or by `scripts\Add-Gemma4Profile.ps1` |
| Model profiles | `analysis-models.json` in `%LOCALAPPDATA%\Windows A11y Recorder` | Written by the app with the default profile; changed in Notepad (`ModelProfilesWindow`) |
| NVDA | Its own installation | Installed separately; not part of the recorder |

The bundled locations for PostgreSQL and Chromium already exist in the
code, so the installer mainly has to fill them, and add one for llama.cpp.

## Feature 1: the installer

### What it installs

- The three .NET programs, published self-contained for `win-x64`, so the
  PC needs no .NET runtime.
- `pgsql\`: the same PostgreSQL binaries the build uses now.
- `browser\`: the run-time files of the instrumented Chromium build, not
  the whole `out\A11yRecorder` folder, which also holds object files and
  symbols. The list of files is taken from Chromium's own archive list
  (`chrome\tools\build\win\FILES.cfg`) and checked by starting the copied
  browser with the recorder bridge before packaging.
- `llama\`: a pinned llama.cpp server build, with the CUDA run-time DLLs
  that llama.cpp publishes beside it.
- Licence notices for PostgreSQL, Chromium, llama.cpp, the CUDA run time
  and the NuGet packages.

Model files are not in the installer. They are tens of gigabytes, and the
point of the Alienware trial is to choose them there (Feature 2).

### How it behaves

- **Per user, no administrator rights.** The recorder's data is already
  per user, and the keyboard and mouse hooks do not need elevation.
  Installs to `%LOCALAPPDATA%\Programs\Windows A11y Recorder`, with a
  Start menu entry.
- **Upgrades in place.** A newer installer replaces the programs and keeps
  the catalog, the recordings, the reports and `analysis-models.json`.
- **Uninstall keeps the data.** Removing the app does not delete
  recordings or the catalog; the uninstaller says where they are.
- **Configuration without copying files.** On first start after an
  install or upgrade, the app points `localServer.executable` at the
  bundled `llama\llama-server.exe` if the setting is empty or points at a
  file that no longer exists. A path the owner set deliberately is kept.
- **Prerequisite check.** The installer checks for the Microsoft Visual
  C++ 2015-2022 x64 run time, and installs Microsoft's redistributable if
  it is missing. Whether the bundled PostgreSQL and llama.cpp builds need
  it is checked on a clean Windows installation, not assumed.
- **Not code-signed, for now.** Windows SmartScreen will warn on the first
  run ("More info", then "Run anyway"). Signing is a separate decision.

### Where it is built

The installer has to be built on the development PC, because that is the
only place the instrumented Chromium build exists. One script,
`scripts\Build-Installer.ps1`, does every step and stops at the first
failure:

1. Publishes the three programs self-contained.
2. Fetches or reuses the pinned PostgreSQL binaries.
3. Copies the Chromium run-time files and checks them.
4. Downloads the pinned llama.cpp server and CUDA run time, and checks
   their SHA-256 against values recorded in the script.
5. Builds `WindowsA11yRecorder-Setup-<commit>.exe` and writes its SHA-256
   beside it.

### Installer tool

Recommended: **Inno Setup**. It builds one `Setup.exe`, supports a
per-user install without administrator rights, copies folder trees
recursively without listing every file, and its licence allows use "for
any purpose, including commercial applications"
([Inno Setup licence](https://jrsoftware.org/files/is/license.txt)).

The alternative is **WiX**, which builds a standard MSI. MSI suits
managed company deployment, but each file must be authored or harvested,
and recent WiX releases ask organisations with revenue to pay the Open
Source Maintenance Fee
([Open Source Maintenance Fee](https://opensourcemaintenancefee.org/consumers/)).
MSIX is not proposed: its package isolation would need checking against
the hooks, the bundled PostgreSQL server and the Chromium processes.

### llama.cpp build

llama.cpp publishes Windows x64 server builds for CUDA 12.4 and CUDA 13
(b11607 has `llama-b11607-bin-win-cuda-12.4-x64.zip`, 252 MB, and
`cudart-llama-bin-win-cuda-12.4-x64.zip`, 373 MB, as listed on its GitHub
release on 2026-11-06). The proposal pins **b11607 with CUDA 12.4**,
because that is the build every trial so far used, and the RTX 2060 and
the RTX 3090 can both run it. Moving to a newer build is a separate,
measured change, since the worker relies on router mode and presets.

## Feature 2: model configuration in the app

The profile file format stays as it is (`analysis-models.json`, version 1);
the app gains a proper editor for it.

### Model profiles window

Replaces "edit in Notepad" with a form, keeping "Open the file" for
anything the form does not cover:

- **List of profiles**, with the default marked, and Add, Duplicate,
  Remove and Make default.
- **Model file**: choose a `.gguf` already on the PC, with an optional
  image add-on (`mmproj`). The app computes the SHA-256 and records the
  file size, so provenance stays complete.
- **Capability**: text, images, or both (both needs an image add-on).
- **Server settings** for the local llama.cpp preset (the profile's
  `arguments`): layers on the
  graphics card, expert layers kept on the processor (`-ncmoe`), flash
  attention, context size, and any other llama.cpp argument as free text.
- **Generation settings**: output limit, temperature, seed, and thinking
  on or off.
- **Network endpoint** instead of a local file: address, model name and
  API key, still limited to loopback and private network addresses
  (`EndpointPolicy`).

### Measured, not guessed

- **This PC's hardware**, shown in the window: each graphics card's name
  and memory, and the installed memory, as reported by Windows and by
  `nvidia-smi` when present.
- **Try the profile**: loads the model with the profile's settings,
  reports whether it loaded, the graphics memory used, and the prompt and
  generation speed from a short fixed test, and keeps the result with the
  profile. The window does not recommend settings from estimates; it shows
  what the test measured.

### Comparing quality

Each attempt already records the profile, model file hash and settings
it used, and finished results never change when a profile changes. To
compare models, the player gains **Run again with another profile** on a
finished task, which queues the same task with a chosen profile and keeps
both results for side-by-side reading.

### Model downloads

Not in the first version. Model files are added from the PC's disk. A
download list (Hugging Face address, expected size and SHA-256) can be
added later if choosing files by hand proves slow.

## Order of work

1. `Build-Installer.ps1` and the Inno Setup script; first-start pointing of
   `localServer.executable` at the bundled server.
2. Install on a clean Windows account on the development PC, then on the
   Alienware: record a short session with NVDA and the instrumented
   browser, and play it back.
3. Model profiles window, with hardware display and "Try the profile".
4. "Run again with another profile".

## Open questions for the owner

- Inno Setup or WiX (MSI).
- Whether the installer should include llama.cpp, or leave it to be
  installed separately.
- Whether models from other local servers (Ollama, LM Studio, vLLM) are in
  scope. Their OpenAI-compatible endpoints accept the worker's requests in
  principle, but the worker uses llama.cpp's router loading, grammar and
  thinking controls, which those servers do not all support. Untested.
- Which branch: this proposal assumes a new branch, `installer`, from
  `analysis-runner`, because the model profiles live there.
