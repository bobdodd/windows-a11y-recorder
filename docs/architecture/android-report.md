# The Android Report and the Android Summary

## Status

Built 2026-11-14, not yet checked on the development PC. The tests in
the Linux build environment pass (`AndroidReportTests`), including a
Word document checked against Office 2016's schema; see
[the validation plan](../validation/android-report-plan.md) for the
tests on the development PC.

## The request and the owner's decisions (2026-11-14)

The owner asked: "Let's build an Android Summary that generates a docx
report that incudes screenshots of the screens and zoomed screenshots
(with highlighting) as needed for the check results".

Asked three questions, the owner answered:

1. Whether a model writes any of it: "two reports, one basic non AI,
   one using the model that can summarize etc".
2. How the findings are arranged: "By screen".
3. What each finding holds: "Summary title; what the issue is; why it
   is important; who it affects in terms of accessibility; the impact on
   the user as a score; relevant WCAG SC where appropriate; suggested
   remediation".

The project's standing rule applies: testing and analysis are without
AI as far as possible, for cost and repeatability, with AI used for
summary reports and vision. So the findings, their pictures and their
text are the same in both reports, and only the summaries in the second
are a model's.

## Two task kinds

Both are queued from the player's Analysis menu or the Analysis panel,
for a recording with phone screens, and run in the analysis worker like
the recording summary (see [the analysis runner](analysis-runner.md)).

| Kind | Menu item | Model | Title |
| --- | --- | --- | --- |
| `android-report`, version 1 | Android report, with no model | None | Android accessibility report |
| `android-summary`, version 1 | Android summary, with the chosen profile | The profile chosen in the Analysis panel | Android accessibility summary |

The Android report can be queued with no profile set up at all; the
status line then says it "uses no model".

Each writes to the task's folder, `analysis/<task>/` in the recording's
session folder:

- `android-report.docx` or `android-summary.docx`, the Word document;
- `android-report.md` or `android-summary.md`, the same report in
  Markdown, which the player shows in its reading view;
- `pictures/`, the PNG pictures both refer to.

## Opening the report

A finished task has an "Open in Word" button beside "Open result",
which opens the Word document with the program Windows has for it.
"Open result" shows the Markdown in the player's reading view.

Running a task again writes its pictures afresh: the old `pictures`
folder is removed first.

## The audit

The task opens the recording file, and runs the same checks as the
player's Checks tab, with the settings in the phone checks settings file
(`PhoneCheckSettings.DefaultPath`): the audit level, AA or AAA, and the
time the phone must be held turned. Text recognition uses Windows' own
OCR, as in the player. Checks the player runs on demand, such as text
contrast and images of text, are run in full here.

The report covers the whole recording, whatever part of it was chosen
when it was queued; it says so under "Limits".

## How the report is arranged

By screen, as the owner decided (decision 2):

1. The title, and a sentence saying whether a model wrote any of it.
2. "About this recording": the recording, its length, each app with its
   version and framework as recorded, the counts of screens and visits,
   the audit level, the evidence in the recording, whether OCR was used,
   the guidance file's version and SHA-256, and when it was written.
3. "How to read this report": the levels, the impact scale, the
   pictures, the frameworks, and the evidence references.
4. In the summary only, "Overview, inferred by a model".
5. "At a glance": the counts by WCAG success criterion and by screen,
   each with errors, warnings and the highest impact.
6. "Across the app": findings about the app as a whole, such as no live
   region anywhere in it.
7. "Screen n: name" for each screen, in the order first visited; in the
   summary, "Summary of the screen, inferred by a model"; then each visit
   with its picture, and each finding of the visit as "Finding n: title:
   item".
8. "Limits".
9. In the summary only, "How the summaries were written", the model and
   attempts, as in the recording summary.
10. "Evidence": each visit and finding cited, with its time.

A finding belongs to the visit where it was first seen. Findings are
numbered through the report: the app's, then each screen's in turn,
within a visit by impact, highest first, then by level, then in the
guidance file's order, then by first time.

### Evidence references

References such as [E3] are numbered in the same order: the app's
findings, then for each screen each visit, then that visit's findings.
The "Evidence" list at the end gives each cited reference's time in the
recording and what it is, so that the player can go to it. A model's
statements in the summary cite the same references.

## Each finding

As the owner decided (decision 3), each finding has its heading, the "summary title", then a table:

| Field | Value |
| --- | --- |
| Level | Error; Warning, for a person to confirm; or Information, for a person to judge |
| Impact | The score from 1 to 4 and its word, "if confirmed" for a warning or information |
| WCAG success criteria | Each criterion with its name and level, or "None: best practice", with any related criterion |
| Item | The item as the checks name it |
| Measured | What the check measured |
| Seen | How many times, at which times, with its evidence reference |
| Check | The check's name and its basis |

Then the finding's picture (see "Pictures"), and these paragraphs:

- "What the issue is:"
- "Why it matters:"
- "Who it affects:"
- "Suggested remediation for Android:" for Views and Jetpack Compose;
  "Suggested remediation for Flutter:" and "for React Native:" only when
  the recorder found that framework's library in the app (see
  [framework metadata](android-framework-metadata.md)). A framework's
  sources are listed only with its remediation.
- "Sources:", each with its title and address.

Two snapshots can be shown by the same video frame, when the picture did
not change between them; "Seen" then says, for example, "Twice, in 1
picture, at 00:00:01.500".

## The guidance file

The fixed text of every check is in
`src/Recorder.Analysis/Android/android-check-guidance.json`, embedded in
the program. It holds, for each of the 41 checks: the title, the issue,
why it matters, who it affects, the impact score, the WCAG success
criteria and related ones, the basis, the remediation for Android,
Flutter and React Native, and its sources. The sources and criteria are
listed once, by key, with their titles and addresses. A test checks
that every check the recorder has is in the file, that every key
resolves, and that the file holds no em dash.

The report gives the file's version and SHA-256, so a report can be
traced to the text it used.

### The impact scale (proposed, for the owner to review)

The score is the recorder's default judgement of each kind of issue
once confirmed; it is not measured from the app, and the report says
so.

| Score | Word | Meaning |
| --- | --- | --- |
| 4 | critical | Can stop the people it affects from completing a task or using the screen at all. |
| 3 | serious | Makes a task very hard for the people it affects, or likely to fail. |
| 2 | moderate | Makes a task harder, slower or more confusing for the people it affects. |
| 1 | minor | An inconvenience; the task can still be done without much extra effort. |

The proposed scores: 4 for an item with nothing to say, a text field
with no name, a clickable item the keyboard may not reach, and a change
of context when TalkBack's focus moved; 3 for touch targets under 24 by
24 dp, a text field with a content description, conflicting reading
order, text contrast below AA, non-text contrast, a label not in the
name, a field with no label, an invalid field with no error, a status
not announced, text gone by itself, a change of context on focus or on
input, and columns made with spaces; 1 for a redundant description, an
image of text at AAA, a label not at the start of the name, a first
heading low on the screen, many headings, and a line break in
mid-sentence; 2 for the rest. These are for the owner to change.

## Pictures

Each visit with findings has a picture of the screen for each picture
its findings were first seen in, usually one: the companion app's
screenshot of the settled snapshot when there is one, otherwise the
video frame showing it. Each is shown at about 540 pixels
wide, halved by whole pixels, with each finding's item outlined and
numbered. A visit with no findings has its picture only when it is the
screen's first visit.

- The outline is cyan, `#00E5FF`, between two black bands, a band of one
  unit, cyan of two and a band of one, outside a gap of one unit round
  the item, so that it shows on light and dark screens alike. The unit
  is a three-hundredth of the picture's shorter side, rounded, at least
  1 pixel.
- The finding's number is drawn in black on cyan above the outline's
  top left corner, or below it when there is no room above.
- An item of 90% or more of the screen is not outlined: the finding is
  about the whole screen, and its picture says so.
- An item of less than a quarter of the screen's area is also shown
  zoomed, cut from the picture at full size with a margin of half its
  size or 24 pixels, whichever is more, and enlarged by whole times, 1
  to 4, to about 720 pixels wide or 1,200 high, then outlined.
- Every picture has a grey edge, `#595959`, of 4 pixels outside it, so
  that a light screen can be told from the white page.
- Each picture has alternative text naming the screen, the time, the
  picture's source and the findings outlined, or, for a zoom, the
  finding, the item, the screen and the enlargement.

The pictures are drawn by the recorder, without a model.

## The Word document

Written with the Open XML SDK (`DocumentFormat.OpenXml` 3.5.1) from the
same Markdown, so the two say the same:

- real heading styles, Heading 1 to 6, for navigation;
- tables with a repeated header row, rows that do not split across
  pages, and columns sized by the length of their text;
- bulleted lists, with Word's numbering;
- pictures inline, each with its alternative text, at most 3 inches wide
  for a tall picture and 6 for a wide one, and 5.5 high;
- links as real hyperlinks;
- "What the issue is:" and the other labels in bold;
- US Letter with one-inch margins, and the document's title and
  language, English (Canada), set.

## The Android summary

As the owner decided (decision 1), the summary is the report with two kinds of model step, through the
analysis runner, with its reuse, attempts and fixed-seed retries:

- `screen-0001` upward, for each screen with findings: template
  `android-summary-screen`, version 1, sections "screen" and "issues".
  The input is the screen, its visits and its findings, each with its
  evidence reference, level, impact, criteria, title, item, measurement,
  times seen and who it affects.
- `recording`, when there are findings: template
  `android-summary-recording`, version 1, sections "app", "barriers" and
  "priorities". The input is the apps and their frameworks, the counts by
  criterion, the app's own findings, and each screen's summary, labelled
  inferred.

The instructions say that a warning is a likely issue not yet confirmed
and is never to be stated as a failure, that the impact scores are not
measured, and that the model is not to say whether the app meets WCAG.
Each statement is a claim citing its evidence, or says what is unknown
or that there is no evidence, as in the recording summary. The report
labels each summary as inferred by a model.

## Limits

- The findings are the recorder's checks of what was recorded; the
  report does not show that a screen meets WCAG.
- Warnings and information need a person to confirm them.
- The impact scores and remediation are fixed text for each kind of
  issue, not advice for the app.
- Colours measured on video frames are approximate.
- The framework is the libraries bundled in the app, not what drew each
  screen; not finding one does not mean the app is native Android.
- The report reads recording files only, not recordings whose events
  are only in the database.
- The report covers the whole recording.
