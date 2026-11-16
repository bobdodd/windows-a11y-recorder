# Validation plan: projects of several recordings

These tests are for [projects](../architecture/projects.md): choosing
the project new recordings go into, moving recordings between projects,
and the Android report of every recording in a project, with a screen
seen in more than one recording given once.

What was checked before these tests, in the Linux build environment:
the solution builds with no warnings; the new tests pass
(`ProjectCatalogTests`, and the project tests in `AndroidReportTests`),
for making, renaming and listing projects, moving recordings with their
tasks, a project's task and its recordings in order, the database's
rule that a task is of a recording or a project, two recordings' Settings
screen given as one screen with its three visits, the touch target
finding given once with each recording's visit and the app's version,
the recordings table, picture names by recording, the evidence by
recording and time, the left-out list, and the client's Word document
against Office 2016's schema; and that a recording's own report is the
same as before. The rest of the tests pass, except the 10 browser tests
that need Windows, as before. A project's Word document was converted to
PDF with LibreOffice and looked at page by page.

Not checked there: real recordings, Word itself, the player's project
controls and window, and the worker running a project's task.

TalkBack is not needed for these tests; leave it off.

The tests use two or more phone recordings of the same app made before
this change, for example the phone recordings of 2026-11-09
to 2026-11-14. These are all in "Default project".

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

## Test 2: the Project group in the side panel

1. Look at the side panel, above the recording options.
2. Take a screenshot of the side panel.

**On screen:** a "Project" group with "Project for new recordings:",
a list showing "Default project", and the buttons "New project..." and
"Projects and recordings...". The menu bar has a "Project" menu.

## Test 3: make a project

1. Choose "New project...".
2. Type `Project test` in "Project name:" and choose OK.
3. Take a screenshot of the side panel and the status line.

**On screen:** the list shows "Project test", and the status line says
"The project Project test was made. New recordings go into it."

## Test 4: a name in use is refused

1. Choose "New project..." again.
2. Type `Project test` and choose OK.
3. Take a screenshot of the message.

**On screen:** a message says "A project named Project test already
exists." and asks to choose it from the list or give another name. After
OK, the list still shows "Project test" once.

## Test 5: the project chosen is remembered

1. Close the recorder.
2. Start it again from the Start menu.
3. Take a screenshot of the side panel.

**On screen:** the Project list shows "Project test".

## Test 6: a new recording goes into the chosen project

1. With "Project test" chosen, make a recording of the desktop only, of
   about ten seconds, with no phone connected.
2. Open the recording and the Analysis panel.
3. Take a screenshot of the Analysis panel.

**On screen:** the panel says "This recording is in the project Project
test, with 1 recording. The project's tasks are listed with this
recording's." The text wraps without a horizontal scrollbar.

## Test 7: the projects window

1. Choose "Projects and recordings..." in the side panel.
2. In "Projects:", select "Default project".
3. Take a screenshot of the window.

**On screen:** "Projects:" lists "Default project" and "Project test,
1 recording", each with its number of recordings. "Recordings in the
selected project" lists the earlier recordings, each with its session
key, start, length and "completed", in the order they were made.

## Test 8: move recordings to the project

1. In the window from test 7, select two or more phone recordings of
   the same app, holding Ctrl to select more than one.
2. In "Project to move the selected recordings to", choose "Project
   test".
3. Choose "Move".
4. Take a screenshot of the window.

**On screen:** the status says, for two, "2 recordings were moved to
Project test, with their analysis." The moved recordings are no longer
listed under "Default project", and "Project test" now lists 3
recordings.

## Test 9: a rename to a name in use is refused

1. In the window, select "Project test" and choose "Rename project...".
2. Type `Default project` and choose OK.
3. Take a screenshot of the window, then close it.

**On screen:** the status says "A project named Default project already
exists, so Project test was not renamed." The list still shows "Project
test".

## Test 10: queue the project's report

Keep the phone's picture in view throughout.

1. Open one of the phone recordings moved in test 8.
2. Open the Analysis menu and choose "Android report for the project".
3. Wait until the new task's row in the Analysis panel says "Finished".
4. Take a screenshot of the whole recorder.

**On screen:** the status line said "Android report for the project
queued; it uses no model. It reads the project's recordings from" and
the recordings' folder. The task's row names "Android report for the
project", says "Finished", and has "Open in Word". Long text wraps
without a horizontal scrollbar.

## Test 11: the project's report in Word

1. In the row from test 10, choose "Open in Word"; if asked whether to
   update the fields, choose Yes.
2. Scroll to "About this project" and "The recordings".
3. Take a screenshot of both.

**On screen:** "About this project" gives "Project test", "2
recordings, and 1 recording left out" (or as many as were moved), each
app's version in each recording, and how many screens were seen in more
than one recording. "The recordings" numbers the phone recordings 1, 2
and so on, in the order they were made. No word in either table is
split across two lines.

## Test 12: a screen seen in more than one recording

1. In the Navigation Pane, choose a "Screen" heading whose introduction
   says "Seen in recordings 1 and 2".
2. Scroll through its visits' headings and its first "Findings on more
   than one visit".
3. Take a screenshot of the finding's table and of a visit heading.

**On screen:** the visits' headings read "Recording 1, visit 1," then
the times, then "Recording 2, visit" and its number. The finding's
"Seen" row lists each visit as "recording 1, visit 1 (version ...)" and
"recording 2, visit ... (version ...)", with a reference for each. If
no screen says "Seen in recordings", say so and send the "By screen"
table.

## Test 13: the recording left out

1. In the Navigation Pane, choose "Limits".
2. Take a screenshot of the list.

**On screen:** a line "Left out:" names the desktop recording from test
6, "as it has no phone screens."

## Test 14: a recording's own report is unchanged

1. With the same recording open, choose "Android report"
   in the Analysis menu and wait until it says "Finished".
2. Choose "Open in Word" in its row.
3. Take a screenshot of the contents and the first visit heading.

**On screen:** the contents start "1 About this recording", with no
"The recordings" entry, and the visits' headings start "Visit 1", with
no "Recording 1,".

## Test 15: Word's accessibility checker on the project's report

1. In Word, with the project's report from test 11 open, choose Review,
   then Check Accessibility.
2. Take a screenshot of the Accessibility pane.

**On screen:** the pane lists no errors. Warnings or tips may be
listed; send them as they are.
