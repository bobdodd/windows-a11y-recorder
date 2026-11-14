# Validation plan: the Android report and the Android summary

These tests are for [the Android report](../architecture/android-report.md):
the Android report, with no model, and the Android summary, with a
model, each written as a Word document and Markdown, by screen, with
pictures of the screens and zoomed, outlined items.

What was checked before these tests, in the Linux build environment:
the solution builds with no warnings; the new tests pass
(`AndroidReportTests`), for the guidance file, the outlines, numbers,
zooms and edges of the pictures, the report's sections and each
finding's fields, the Word document against Office 2016's schema with
its headings, header rows, pictures with alternative text, links and
language, and the model's input and statements in the summary. The rest
of the tests pass, except the 10 browser tests that need Windows, as
before. A Word document made by the tests was converted to PDF with
LibreOffice and looked at page by page.

Not checked there: a real recording, Word itself, the player's buttons,
and a model's summary.

TalkBack is not needed for these tests; leave it off.

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

## Test 2: the Android items are only for phone recordings

1. Open a recording of the desktop only, with no phone.
2. Open the Analysis menu.
3. Take a screenshot of the open menu.

**On screen:** "Android report, with no model" and "Android summary,
with the chosen profile" are both shown and both greyed out.

## Test 3: queue the Android report

Keep the phone's picture in view throughout.

1. Open the recording from test 58 of the Android checks plan, or any
   phone recording with findings in its Checks tab.
2. Open the Analysis menu and choose "Android report, with no model".
3. Open the Analysis panel and wait until the new task's row says
   "Finished".
4. Take a screenshot of the whole recorder.

**On screen:** the status line said "Android report queued; it uses no
model." The task's row names "Android report", says "Finished", and
has an "Open in Word" button beside "Open result". Long text wraps
without a horizontal scrollbar.

## Test 4: open the report in Word

1. In the row from test 3, choose "Open in Word".
2. In Word, choose View, then tick Navigation Pane.
3. Take a screenshot of Word showing the first page and the Navigation
   Pane.

**On screen:** Word opens the document. The first page starts
"Android accessibility report:" with the recording's name, then "About
this recording" and its table. The Navigation Pane lists "How to read
this report", "At a glance", "Screen 1:" with the screen's name, and
"Limits" and "Evidence" at the end.

## Test 5: the pictures in the report

1. In the Navigation Pane from test 4, choose the first "Screen"
   heading that has a "Finding" under it.
2. Scroll down to the screen's picture, then to the first finding's
   picture below its table.
3. Take a screenshot of each picture.

**On screen:** the screen's picture shows the phone's screen with a
grey edge, and each finding's item outlined in cyan between thin black
lines, with its number in black on cyan by the outline. If the item is
small, the finding has a second, enlarged picture of the item, outlined
in the same way. The numbers match the findings' headings.

## Test 6: each finding's fields

1. Under the first finding from test 5, look at its table and the
   paragraphs after it.
2. Take a screenshot of the finding from its heading to its sources.

**On screen:** the table rows are Level, Impact, WCAG success criteria,
Item, Measured, Seen and Check. After the pictures come "What the issue
is:", "Why it matters:", "Who it affects:" and "Suggested remediation
for Android:", each label in bold, then "Sources:" with links. For a
Flutter app, "Suggested remediation for Flutter:" follows Android's.

## Test 7: Word's accessibility checker

1. In Word, choose Review, then Check Accessibility.
2. Take a screenshot of the Accessibility pane.

**On screen:** the pane lists no errors. In particular, no picture is
listed as missing alternative text and no table as missing a header
row. Warnings or tips may be listed; send them as they are.

## Test 8: queue the Android summary

Keep the phone's picture in view throughout.

Use a recording that is not of a client's app, as the summary sends the
recorder's findings to the profile's model.

1. Open the recording. In the Analysis panel, choose a profile under
   "Profile:".
2. Open the Analysis menu and choose "Android summary, with the chosen
   profile".
3. Wait until the new task's row says "Finished", then choose "Open in
   Word".
4. Take a screenshot of Word's first page, then of the first "Screen"
   section with its summary.

**On screen:** the first page starts "Android accessibility summary:",
with a heading "Overview, inferred by a model" after "How to read this
report". Each screen with findings has "Summary of the screen, inferred
by a model" before its first visit, with references such as [E3] in
its statements. The findings below are laid out as in the Android
report.
