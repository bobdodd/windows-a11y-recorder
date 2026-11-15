# Validation plan: evidence numbers, chapters and touches in an MP4

These tests are for "Evidence numbers" and "Touches" in
[the MP4 export design](../architecture/mp4-export.md): a report's
evidence numbers drawn at the bottom left of the picture for 4 seconds
each, a chapter for each in the MP4, and a phone's touches drawn as the
player draws them.

What was checked before these tests, in the Linux build environment: the
solution builds with no warnings, and the new tests pass
(`Mp4AnnotationTests`, and the kinds in `AndroidReportTests` and
`RecordingSummaryTests`). The rest of the tests pass, except the 10
browser tests that need Windows, as before. The chapter writer was
applied to MP4s made by FFmpeg, and VLC 3.0.23 listed their chapters with
the right titles and times, with 3 chapters and with 300. See "Checks
made" in the design.

Not checked there: the export window, the numbers and touches as drawn
by the app, and VLC on Windows.

TalkBack is not needed for these tests; leave it off. VLC must be
installed on the development PC.

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

## Test 2: an older report has no evidence list

1. Open a phone recording that has an Android report made before this
   version.
2. Choose "Save as MP4..." (Alt+4).
3. In "Evidence numbers from", choose the item ending ", no evidence
   list".
4. Take a screenshot of the window.

**On screen:** a heading "Evidence numbers" above the list. The text
under the list reads "This report has no evidence list for MP4s, as it
was written before reports had one. Run it again from the Analysis tab
to use its numbers, or choose another." The text wraps, with no
horizontal scrollbar.

## Test 3: a new report's numbers are offered

Keep the phone's picture in view throughout.

1. Close the window. Open the Analysis menu and choose "Android report,
   with no model".
2. Wait until the new task's row in the Analysis panel says "Finished".
3. Choose "Save as MP4..." (Alt+4).
4. Take a screenshot of the window.

**On screen:** "Evidence numbers from" shows "Android report, finished"
and today's date and time, with no ", no evidence list". The text under
it reads "... evidence numbers of this recording are shown, with a
chapter each." and then "... are about the whole recording or project,
with no time, so they are left out."

## Test 4: save the MP4 with numbers

In the window from test 3:

1. Choose "Save MP4".
2. Wait for the status line to stop changing.
3. Take a screenshot of the window.

**On screen:** the status line begins "Saved" and the file's path, and a
line reads "... evidence numbers are shown on the picture, with a chapter
each." with the same number as test 3. Any line beginning "The MP4 could
not be saved" is a failure; send it.

## Test 5: the chapters in VLC

1. Open the MP4 from test 4 in VLC.
2. Open VLC's Playback menu, then Chapter.
3. Take a screenshot of the open menu.

**On screen:** one chapter for each number, named by number and kind,
such as "E3, visit 1 to" and the screen's name, or "E7, finding 2".
They are in time order.

## Test 6: a number at the bottom left

1. In VLC, choose a chapter from Playback, Chapter, such as a visit's.
2. Pause at once.
3. Take a screenshot of VLC.

**On screen:** at the bottom left of the picture, white numbers on black
boxes, the chosen chapter's number among them, in number order from the
left. Where there are many, they wrap upwards in rows, and leave at
least the right part of the picture clear.

## Test 7: a number lasts 4 seconds

1. In VLC, choose a chapter whose number is shown alone, or with few
   others.
2. Note the time VLC shows, then play.
3. Pause as soon as that number is gone, and note the time.

**On screen:** the number is gone about 4 seconds after the chapter's
time, and not before.

## Test 8: touches in the MP4

Keep the phone's picture in view throughout.

1. In the recorder, find a moment where the phone was touched, with the
   circle on the picture. Note the time.
2. In VLC, move to the same time and pause.
3. Take a screenshot of each.

**On screen:** the MP4 shows the same circle, at the same place, with
its path, as the player. The focus outline and the finding outline are
not in the MP4.

## Test 9: no numbers

1. In the recorder, choose "Save as MP4..." again.
2. Choose "None" in "Evidence numbers from", and give a new file name.
3. Choose "Save MP4", then open the new file in VLC.
4. Take a screenshot of VLC's Playback menu.

**On screen:** no numbers on the picture, and Chapter is greyed out or
missing in the Playback menu. The touches are still drawn.

## Not covered

- A recording summary's numbers on a desktop recording: it needs a
  model run again. Its evidence list is written the same way.
- Windows' own players' chapters.
