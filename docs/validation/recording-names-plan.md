# Validation plan: recording names

These tests are for [recording names](../architecture/recording-names.md):
naming a recording when it starts, renaming it later, names unique within
a project, and a named recording shown as its name and then its session
key.

What was checked before these tests, in the Linux build environment: the
solution builds with no warnings; the new tests pass, for a name given
when a recording is made, saved trimmed and listed with its session key;
the same name refused in a project and allowed in another; renaming,
renaming to a name the project has, and removing a name; moving
recordings, leaving one whose name the target project has; the catalog's
check that a name is never empty or padded; and an Android report's
heading and Recording row for a named recording. The rest of the tests
pass, except the 10 browser tests that need Windows, as before.

Not checked there: the dialogs, the Projects window, the Project menu,
the window's title, and reports run by the worker.

TalkBack is not needed for these tests; leave it off. The phone does not
need to be connected: the recordings are of the desktop, except in
Test 10, which uses an earlier phone recording.

## Test 1: build and install the new version (development PC)

Run only after the commit has been pushed.

1. Close the recorder. Run this in PowerShell to build and launch the
   newest installer:

   ```powershell
   Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass -Force
   $ErrorActionPreference = "Stop"
   $repo = Join-Path $env:USERPROFILE "windows-a11y-recorder"
   git -C $repo pull --ff-only
   if ($LASTEXITCODE -ne 0) { throw "Git pull failed." }
   & (Join-Path $repo "scripts\Build-Installer.ps1")
   $installer = Get-ChildItem (Join-Path $repo "artifacts\installer\output") -File -Filter "*.exe" |
       Sort-Object LastWriteTime -Descending | Select-Object -First 1
   if (-not $installer) { throw "No installer was found." }
   Start-Process -FilePath $installer.FullName
   ```

2. Complete setup with model setup unchecked.
3. Start the recorder from the Start menu.

**On screen:** the build reports `INSTALLER BUILT`, the installer opens,
and the recorder opens after setup.

## Test 2: the name is asked for on Start, and Cancel does not start

1. In the side panel, choose Desktop as the recording target and
   "Default project" as the project.
2. Choose Start recording.
3. Look at the dialog, then choose Cancel.

**On screen:** a dialog titled "Name this recording" says
"The recording goes into the project Default project.", that the name
can be left empty, and that no two recordings in a project can have the
same name. The name
box has the focus. After Cancel, the side panel still says
"Ready to record." and Start recording is still available.

## Test 3: a recording started with a name

1. Choose Start recording, type `Name test 1`, and choose
   Start recording in the dialog.
2. After about five seconds, choose Stop recording.
3. Choose Projects and recordings, and select Default project.

**On screen:** the last recording in the list reads
"Name test 1 (" then its session key, then ", started" and today's date
and time.

## Test 4: a recording started with no name

1. Close Projects and recordings. Choose Start recording, leave the name
   empty, and choose Start recording in the dialog.
2. After about five seconds, choose Stop recording.
3. Choose Projects and recordings, and select Default project.

**On screen:** the last recording in the list starts with its session
key alone, as earlier recordings do, with no name in front of it.

## Test 5: a name the project already has is refused

1. Close Projects and recordings. Choose Start recording, type
   `Name test 1`, and choose Start recording in the dialog.
2. Look at the dialog, then choose Cancel.

**On screen:** the dialog stays open, with
"A recording named Name test 1 is already in this project." under the
name box, and the name selected. After Cancel, no recording has started.

## Test 6: renaming in Projects and recordings

1. Choose Projects and recordings, and select Default project.
2. Select the recording "Name test 1", and choose Rename recording....
3. Change the name to `Name test 2`, and choose Save.

**On screen:** the dialog's name box held "Name test 1". The list now
reads "Name test 2 (" then the same session key; that recording is still
selected; and the status line says it was renamed Name test 2.

## Test 7: removing a name

1. In Projects and recordings, select the recording "Name test 2", and
   choose Rename recording....
2. Delete the name, and choose Save.

**On screen:** the recording is listed by its session key alone, and the
status line says it has no name now and is known by its session key.

## Test 8: renaming the open recording

1. Close Projects and recordings. Choose Open recording..., and open the
   folder of the recording made in Test 4.
2. When it has loaded, open the Project menu, and choose
   Rename the open recording....
3. Type `Name test 3`, and choose Save.

**On screen:** before the rename, the window's title is the session key,
then ", Windows Accessibility Recorder". After it, the title starts with
"Name test 3 (" and the session key, and the Analysis tab's line says
"This recording, Name test 3 (" and the session key, is in the project
Default project.

## Test 9: moving a recording whose name the other project has

1. In the side panel, choose New project..., name it `Name test move`,
   and choose Save.
2. Choose Start recording, type `Name test 3`, choose Start recording in
   the dialog, and stop after about five seconds.
3. Choose Projects and recordings, select Default project, and select
   the recording "Name test 3".
4. Choose `Name test move` under "Move the selected recordings to", and
   choose Move.

**On screen:** the status line says 0 recordings were moved, and that
"Name test 3 (" and its session key was not moved, as Name test move
already has a recording of the same name. The recording is still listed
in Default project.

## Test 10: an Android report of a named recording

1. Choose Projects and recordings, select Default project, select the
   phone recording 20261113-200327, rename it `Name test report`, and
   close the window.
2. Choose Open recording..., and open that recording's folder.
3. From the Android menu, choose "Android report, with no model", and
   wait in the Analysis tab until its row says "Finished".
4. In its row, choose "Open result", then "Open in Word".

**On screen:** the result's heading reads
"Android accessibility report: Name test report (20261113-200327-" then
the rest of the session key. In the Word document, the
"About this recording" table's Recording row reads "Name test report ("
and the session key.

## Cleaning up

The test recordings can be left, or moved to a project of their own. To
give 20261113-200327 its session key alone again, rename it with an
empty name as in Test 7.
