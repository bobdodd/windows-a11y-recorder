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
its title, numbered headings, table of contents, header and footer with
page numbers, header rows, table captions, pictures with alternative
text, links and language, that the client's version does not mention
models, the model's input and statements in the summary, and a finding
seen on two visits to a screen given once under the screen, with a
zoomed picture and a reference for each visit. The rest
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

The button opens the document in the program Windows has for Word
documents, LibreOffice Writer on the development PC.

1. In the row from test 3, choose "Open in Word".
2. If asked whether to update the fields in the document, choose Yes.
3. Open the Navigator (LibreOffice: View, then Navigator) or the
   Navigation Pane (Word: View, then tick Navigation Pane).
4. Hold Ctrl and click the contents entry for "Evidence".
5. Take a screenshot showing the contents and the Navigator or
   Navigation Pane, then one after the click.

**On screen:** the first page has "Accessibility report:" and the
app's package name at its top, as the header and as the title, a
paragraph ending "The final report is manually inspected.", then
"Contents" with numbered entries, each with a dotted line to its page
number at the right margin, and "Page 1 of" in the footer. The click
moves to the "Evidence" heading, on the page its entry gave. The word "model" is not on the page. The Navigation
Pane lists numbered headings: "1 About this recording", "2 How to read
this report", "3 At a glance", "4 Findings across the app" if there are
any, then "Findings by screen" with "Screen 1:" and the screen's name
numbered under it, and "Limits" and "Evidence" at the end.

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
Item, Measured, Seen and Check. The Impact row reads High, Medium or
Low, with ", if confirmed" after it for a warning. After the pictures come "What the issue
is:", "Why it matters:", "Who it affects:" and "Suggested remediation
for Android:", each label in bold, then "Sources:" with links. For a
Flutter app, "Suggested remediation for Flutter:" follows Android's.

## Test 7: Word's accessibility checker

1. In Word, choose Review, then Check Accessibility.
2. Take a screenshot of the Accessibility pane.

**On screen:** the pane lists no errors. In particular, no picture is
listed as missing alternative text, no table as missing a header row,
and the document is not in compatibility mode. Warnings or tips may be
listed; send them as they are.

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

**On screen:** the first page's paragraph ends "AI is used in
generating this report, with a cloud model, and the final report is
manually inspected." for a Claude profile, or "with a local model" for
a local one.
After "How to read this report" comes a numbered heading "Overview".
Each screen with findings has "Summary of the screen" before its first
visit, with references such as [E3] in its statements. The findings
below are laid out as in the Android report. Each screen's first
paragraph ends with the screen's own reference, such as [E4]. There is
no heading "How the summaries were written", and no other mention of a
model.

If the task fails, choose "Why it failed" and take a screenshot of the
message.

## Test 9: a finding on more than one visit

Use a recording with a return visit to a screen, such as the client
recording of 2026-11-12, and the Android report from test 3, queued
again with this version.

1. Open the report in Word, with View, then Navigation Pane.
2. Under a "Screen" heading with two visits, choose "Findings on more
   than one visit".
3. Take a screenshot of the section's first finding, its table and
   its pictures.
4. Choose the first "Visit" heading under the same screen and take a
   screenshot of its picture and the paragraph after it.

**On screen:** the section comes before the screen's first visit. The
finding's Seen row begins "On 2 visits:", with each visit's times and
its own reference, such as [E7]. There is one "Picture:" paragraph
and zoomed picture for each visit. The visit's picture outlines the
item with the same number as the finding's heading, and the paragraph
after it says the finding is under "Findings on more than one visit".
In the Navigation Pane, the finding's heading appears once for the
screen, not under each visit.

## Test 10: three heading levels in the contents

Added 2026-11-15. Use the Android report from test 3, queued again with
this version.

1. Choose "Open in Word" in the report's row.
2. If asked whether to update the fields in the document, choose Yes.
3. Take a screenshot of the contents.
4. Hold Ctrl and click an entry with three numbers, such as "5.1.1".

**On screen:** under each "Screen" entry, such as "5.1 Screen 1:",
entries with three numbers, such as "5.1.1 Visit 1", indented further,
each with a dotted line to its page number at the right margin. No
entry has four numbers. The click moves to that visit's heading, on the
page its entry gave.

## Test 11: cloud or local in the summary

Added 2026-11-15. Use the summary from test 8, then queue it again with
a profile of the other kind, local or Claude, on the same recording.

1. In the new task's row, choose "Open in Word".
2. Take a screenshot of the first page's paragraph.

**On screen:** the paragraph ends "AI is used in generating this
report, with a local model, and the final report is manually
inspected." for a local profile, or "with a cloud model" for Claude:
the other kind from test 8's. No other sentence on the page mentions a
model.
