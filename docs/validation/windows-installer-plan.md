# Windows installer: validation plan

Written 2026-11-06 for the installer described in
[the installer decisions](../architecture/windows-installer.md). Not yet
run. Each test checks one behaviour in at most five steps, with what to see
on the screen.

What was checked before this plan, in the Linux build environment:

- The solution builds with no warnings, and the 37 tests of model setup
  pass (the catalogue, the hardware comparison, the downloader's resume,
  retries and hash check, Hugging Face addresses, and the profile writer).
- The player publishes for `win-x64` with the analysis worker and
  `model-catalog.json` beside it.
- The setup script compiles with Inno Setup 6.7.3 and 7.1.1, and
  installers built from placeholder files installed and uninstalled under
  Wine.

None of that shows the installer works on Windows. Tests 1 to 8, with
6b, do.

## Before the tests

- On the development PC: the instrumented Chromium build in
  `%USERPROFILE%\chromium-dev\chromium\src\out\A11yRecorder`, and the .NET
  10 SDK, as for every earlier build.
- On the Alienware: about 60 GB free for the program and two models, and
  the NVIDIA driver installed (GeForce Experience or the NVIDIA App
  installs it). Nothing else.

Every PowerShell window used below is a normal one, not "Run as
administrator". The first command in each allows the scripts to run in
that window only.

## Test 1: build the installer (development PC)

1. Open PowerShell and allow scripts in this window:

   ```powershell
   Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass -Force
   ```

2. Install Inno Setup, if it is not installed (6.3 or later; winget
   installs 6.7.3, and Windows may ask for permission):

   ```powershell
   winget install --id JRSoftware.InnoSetup -e --accept-package-agreements --accept-source-agreements
   ```

3. Get `main`, cloning it if there is no clone in your user folder:

   ```powershell
   $repo = Join-Path $env:USERPROFILE "windows-a11y-recorder"
   if (-not (Test-Path -LiteralPath $repo)) { git clone https://github.com/bobdodd/windows-a11y-recorder.git $repo }
   git -C $repo checkout main
   git -C $repo pull --ff-only
   ```

4. Build the installer (the first build downloads about 700 MB):

   ```powershell
   & (Join-Path $repo "scripts\Build-Installer.ps1")
   ```

5. Check the end of the output.

**On screen:** the script finishes with `INSTALLER BUILT`, the installer's
path in `artifacts\installer\output` and its SHA-256. Above it:
`PROTOCOL VERSION QUERY VERIFIED`, and a line beginning `Bundled llama.cpp
server:` with a version. Any red error text means it failed; send the
whole output.

## Test 2: install on the Alienware

1. Copy `WindowsA11yRecorder-Setup-<revision>.exe` and its `.sha256` file
   from the development PC's `artifacts\installer\output` to the
   Alienware's Downloads folder (a USB drive or a shared folder).
2. On the Alienware, open PowerShell and compare the hash:

   ```powershell
   Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass -Force
   $setup = Get-ChildItem "$env:USERPROFILE\Downloads\WindowsA11yRecorder-Setup-*.exe" | Sort-Object LastWriteTime | Select-Object -Last 1
   (Get-FileHash -LiteralPath $setup.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
   Get-Content -LiteralPath "$($setup.FullName).sha256"
   ```

3. Run the installer from Downloads. If Windows SmartScreen shows "Windows
   protected your PC", choose "More info", then "Run anyway".
4. Go through the pages to "Ready to Install", read it, then Install.
5. On the last page leave "Start the recorder and set up the analysis
   models" checked, and choose Finish.

**On screen:** in step 2, the two hashes are the same. In step 4, the Ready
page lists the five included parts, and has no warning that no NVIDIA
driver was found; if the Visual C++ run time was missing, it says its
installer runs next, and Windows asks permission during the install. After
step 5, the recorder opens and the "Set up analysis models" window opens
over it.

## Test 3: the Alienware's hardware in model setup

In the "Set up analysis models" window from test 2:

1. Read the "This PC" group.
2. Read the "Model server" line.
3. In "Models to download", read the text under "Qwen3.6 35B A3B,
   UD-Q4_K_M".

**On screen:** "Graphics card: NVIDIA GeForce RTX 3090, 24.0 GB" with its
driver, the PC's memory, and "Model server:" followed by
`...\AppData\Local\Programs\Windows A11y Recorder\llama\llama-server.exe`,
with no "which is not there". Under the model: "20.6 GB", "Licence:
apache-2.0", and that it is smaller than the graphics card's 24.0 GB, so it
can all go on the card.

## Test 4: download and add a model

1. In "Models to download", leave "Qwen3.6 35B A3B, UD-Q4_K_M" and its
   "Also download its image add-on" checked, and nothing else.
2. Choose "Download and add the chosen models".
3. Wait for the download (about 21 GB). To check resuming, choose "Stop the
   download" once part-way, then "Download and add the chosen models"
   again.
4. Read the status line at the end.

**On screen:** the progress bar and text advance; after Stop, the status
says the download continues from where it stopped, and after starting it
again the progress resumes from where it was, not from zero. At the end the
status says it added the profiles "Qwen3.6 35B A3B, UD-Q4_K_M" and
"Qwen3.6 35B A3B, UD-Q4_K_M, with images", and names the defaults for text
and images.

## Test 5: the profiles in the model profiles window

1. Close the "Set up analysis models" window.
2. Open Analysis, "Model profiles...".
3. Choose each of the two profiles in the list.
4. Choose "Open the file in Notepad" and find `"localServer"`.

**On screen:** both profiles are in the list; the server arguments show
`-fa on -c 32768`; the defaults show the text profile for text and the
"with images" profile for images. In Notepad, `"executable"` is the
installed `llama-server.exe` and `"port"` is 8091.

## Test 6: record a short session

1. In the recorder, set "Starting website" to `https://www.w3.org/WAI/`.
2. Choose "Start recording". In the browser that opens, follow two links.
3. Choose "Stop recording".
4. If the recording is not already open in the player, choose "Open
   recording..." and choose it.

**On screen:** the instrumented browser opens at the W3C page, the
recording stops without an error, and the player shows the desktop video
with browser events in the timeline.

## Test 6b: inspect the page at a frame

Added 2026-11-07, after the installed browser stopped as it started on the
development PC: its sandbox could not read `chrome.exe` in the installed
folder, which the installer now allows.

1. With the recording from test 6 open, move the playhead to a moment
   after the first link was followed.
2. Choose "Inspect page at this frame".
3. In the "Inspect page at this frame" window, choose the page, then
   Open.

**On screen:** a browser window opens showing the recorded page, with
DevTools open beside it, and no message that the page could not be
recreated.

## Test 7: summarise with the downloaded model

1. With the recording from test 6 open, open Analysis, "Summarise this
   recording".
2. On the Analysis tab, check the task's row while it runs.
3. When it shows "Finished, unread", choose "Open result".

**On screen:** the row shows the profile "Qwen3.6 35B A3B, UD-Q4_K_M",
then "Running", then "Finished, unread", and the report opens. Note how
long it took, for comparison with the development PC.

## Test 8: uninstall keeps the data

1. Open Windows Settings, Apps, Installed apps.
2. Find "Windows A11y Recorder", choose "...", then Uninstall, and confirm.
3. Read the message at the end.
4. Open File Explorer at `Documents\Windows A11y Recorder`.

**On screen:** the message says the recordings, the catalog, the model
settings and the downloaded models were kept, and names
`AppData\Local\Windows A11y Recorder` and `Documents\Windows A11y
Recorder`. In step 4 the recording from test 6 is still there. Installing
again from test 2 finds the two profiles and the downloaded model.

## Afterwards

- Comparing models on the Alienware: in "Set up models...", choose another
  model, for example "Gemma 4 26B A4B, UD-Q4_K_M", download it, then queue
  the same recording's summary with its profile chosen in "Profile:" on the
  Analysis tab.
- Report the results of each test, with the whole output or a screenshot
  of anything that does not match "On screen".
