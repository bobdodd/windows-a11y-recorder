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

A report can also cover every recording in a project, with a screen
seen in more than one recording given once; see
[projects](projects.md).

## Two task kinds

Both are queued from the player's Analysis menu or the Analysis panel,
for a recording with phone screens, and run in the analysis worker like
the recording summary (see [the analysis runner](analysis-runner.md)).

| Kind | Menu item | Model | Title |
| --- | --- | --- | --- |
| `android-report`, version 1 | Android report | None | Android accessibility report |
| `android-summary`, version 1 | Android summary | The project's model for report analysis | Android accessibility summary |

The Android report can be queued with no profile set up at all; the
status line then says it "uses no model". Until 2026-11-15 the menu items
were named "Android report, with no model" and "Android summary, with the
chosen profile", and the summary used the profile chosen in the Analysis
panel; since then it uses the project's model for report analysis
(`projects.md`, "The project's models").

Each writes to the task's folder, `analysis/<task>/` in the recording's
session folder:

- `android-report.docx` or `android-summary.docx`, the Word document,
  for the client's team;
- `android-report.md` or `android-summary.md`, the report for the
  auditor in Markdown, which the player shows in its reading view;
- `pictures/`, the PNG pictures both refer to.

## Two readers

The owner asked, on 2026-11-14, for the Word document to suit the
client's team rather than the auditor: "As it stands it's good for me
as the auditor, it's not ideal for the client's team. It needs a table
of contents and numbered heading levels using Word styles, page numbers
and a header/footer, it needs to be accessible. We don't need to
constantly talk about models, models are how testing is done and
reports generated, they have no information value to a client other
than mentioning that AI is used in the generation of the report, but
that the final report is manually inspected."

So each task writes the report twice, from the same findings, pictures
and evidence:

| | The auditor's Markdown | The client's Word document |
| --- | --- | --- |
| Title | "Android accessibility report:" or "Android accessibility summary:" and the recording's name | "Accessibility report:" and the app's package name |
| Opening paragraph | That the findings are the recorder's own checks, and that the summaries are a cloud or a local model's inferences | That the report gives the results of automated accessibility checks of a recording of the app; for the summary, that AI is used in generating it, with a cloud or a local model; and that the final report is manually inspected |
| Summary headings | "Overview, inferred by a model", "The app, inferred", "Summary of the screen, inferred by a model" | "Overview", "The app", "Summary of the screen" |
| "How the summaries were written" | The model, profile and attempts of each step | Left out |
| Guidance row | Version and SHA-256 | Version |
| Screens | Under the report's top level | Under "Findings by screen", after "Findings across the app" |
| Sources | Title and address | A link reading as the source's title |

The client's version has no other mention of models. Everything else,
the findings, their fields, the pictures and the evidence references,
is the same in both. A summary statement still ends with its evidence
references, as the client's team may want to find the moment in the
recording too.

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
contrast and images of text, are run in full here. After the checks
without a model, the task asks the project's model for vision-based
checks about the images of text the rules leave, reusing any answer
already kept for the same request (`android-vision-checks.md`, "Images
of text, as built"). With no model chosen for vision-based checks,
nothing is asked, those images stay warnings, and "Limits" says why. A
report with a finding inferred by a model says so in its introduction:
the client's, "AI is used in generating this report, with a cloud
model" (or a local one, or both); the auditor's findings mark each inferred level,
give the model's answer in its own row, and add ", if confirmed" to the
impact.

The report covers the whole recording, whatever part of it was chosen
when it was queued; it says so under "Limits".

## How the report is arranged

By screen, as the owner decided (decision 2):

1. The title, and a sentence saying whether a model wrote any of it.
2. "About this recording": the recording, its length, each app with its
   version and framework as recorded, the counts of screens and visits,
   the audit level, the evidence in the recording, whether OCR was used,
   the guidance file's version and SHA-256, and when it was written.
3. "How to read this report": the levels, the impact levels, the
   pictures, the frameworks, and the evidence references.
4. In the summary only, "Overview, inferred by a model".
5. "At a glance": the counts by WCAG success criterion and by screen,
   each with errors, warnings and the highest impact.
6. "Across the app": findings about the app as a whole, such as no live
   region anywhere in it.
7. "Screen n: name" for each screen, in the order first visited; in the
   summary, "Summary of the screen, inferred by a model"; then, if any,
   "Findings on more than one visit"; then each visit with its picture,
   and each finding of the visit as "Finding n: title: item".
8. "Limits".
9. In the summary only, "How the summaries were written", the model and
   attempts, as in the recording summary.
10. "Evidence": each visit and finding cited, with its time.

A finding belongs to the visit where it was first seen, unless it was
seen on more than one visit to its screen (see below). Findings are
numbered through the report: the app's, then each screen's in turn,
those on more than one visit first, then each visit's, each group by
impact, highest first, then by level, then in the guidance file's
order, then by first time.

### Findings on more than one visit

The owner asked, on 2026-11-14: "Where it is basically the same issue
on multiple visits to the same screen, I would like to try and group
the issues so we don't report the same issue multiple times". Asked
what makes two findings the same issue, where the grouped finding goes
and how its item is pictured, the owner chose:

- Same issue: "Same check, level and item", whatever was measured. The
  item is the finding's identity in the checks, the item's tree key or,
  for a finding about the whole screen, the screen.
- Placement: "Under the screen". A screen with such findings has a
  section "Findings on more than one visit" after its paragraph and
  summary and before its visits, with each finding given once.
- Zoom: "One per visit". The grouped finding has a zoomed picture of
  its item from each visit it was seen on, and each of those visits'
  pictures outlines the item with the finding's one number.

Before deciding, the latest client recording's report was measured:
69 findings on 5 screens in 7 visits, with 16 findings seen on 2
visits each, 32 findings in all, so 53 after grouping. Of the 16, 14
measured the same on both visits, and 2, both event checks, differed
only in their numbers. 6 more had the same check and item on
different screens; those are not grouped, as the request was for the
same screen.

A grouped finding's table gives what was measured once if it was the
same on each visit, or each visit's, "Visit 1: ...; Visit 7: ...", if
not, and its Seen row gives each visit's times with that visit's own
reference: "On 2 visits: visit 1, twice, at 00:00:01.500 [E7]; visit
3, once, at 00:00:08.500 [E8]". Each visit then says which of its
findings are under "Findings on more than one visit". The screen's
first paragraph says how many of its findings were seen on more than
one visit, and the counts at a glance count a grouped finding once.

This is the report only. The checks, the player's Checks tab, and the
findings' data stay per visit, as the owner decided on 2026-11-12, so
that a visit's findings are still each visit's in the player. The
summary's model is given a grouped finding once, with each visit's
reference, the visits it was on and its times in all; the templates
are version 3.

### Evidence references

References such as [E3] are numbered in this order: each app, with its
version and framework; the recorder's counts of findings by WCAG
success criterion; the app's findings; then for each screen the screen
itself, with how the recorder named it, then each finding on more than
one visit once for each visit it was on ("Finding 3, on visit 1: ..."),
then each visit and that visit's own findings. The "Evidence" list at the end gives each cited reference's
time in the recording, "Whole recording" for an app and for the counts,
and what it is, so that the player can go to it. A model's statements
in the summary cite the same references.

Every fact a model is given begins with its reference, so that a claim
about any of them has one to cite. On the target machine, on
2026-11-14, the summary failed: a screen's summary restated how the
recorder had named the screen, as a claim, and the line giving the
screen's name had no reference, so the claim cited nothing and was
refused three times. The screen, its app and the counts were given
references then, and the templates are version 2.

## Each finding

As the owner decided (decision 3), each finding has its heading, the "summary title", then a table:

| Field | Value |
| --- | --- |
| Level | Error; Warning, for a person to confirm; or Information, for a person to judge |
| Impact | High, medium or low, "if confirmed" for a warning or information |
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
  sources are listed only with its remediation. A finding in web
  content shown in the app, a web image or an item inside a WebView,
  is given "Suggested remediation for web content:" in place of these,
  with the web's sources in place of Android's, as it is fixed in the
  web page ([android-checks.md](android-checks.md), step 26, rule 4).
  Only the images-of-text checks give web remediation so far.
- "Sources:", each with its title and address.

Two snapshots can be shown by the same video frame, when the picture did
not change between them; "Seen" then says, for example, "Twice, in 1
picture, at 00:00:01.500".

## The guidance file

The fixed text of every check is in
`src/Recorder.Analysis/Android/android-check-guidance.json`, embedded in
the program. It holds, for each of the 43 checks: the title, the issue,
why it matters, who it affects, the impact level, the WCAG success
criteria and related ones, the basis, the remediation for Android,
Flutter and React Native, for a check that can find an issue in web
content the remediation for web content, and its sources. The sources and criteria are
listed once, by key, with their titles and addresses. A test checks
that every check the recorder has is in the file, that every key
resolves, and that the file holds no em dash.

The report gives the file's version and SHA-256, so a report can be
traced to the text it used.

The version goes up when the text changes, as the client's report gives
the version only. Version 3 (2026-11-16) added the checks of a field
shown only by a bottom line and of text shown as a heading, and brought
the bases of the non-text contrast, target-size and images-of-text
checks up to their rules; a basis says what is judged, and from what,
without naming the model, as the finding's level and its own row say
where a model's answer was used. Version 4 (2026-11-18) added the
remediation for web content to the images-of-text checks, at 1.4.5 and
1.4.9, with the WCAG techniques C22 (CSS to control the presentation of
text) and H37 (alt attributes on img elements) as its sources.

### The impact levels

The impact is high, medium or low, the levels the project's other tools
use. The owner asked for them on 2026-11-14: "I need high, medium, low
for impact to match other tools in the project". The level is the
recorder's default judgement of each kind of issue once confirmed; it
is not measured from the app, and the report says so. The guidance file
names each check's level by its word, and the program orders findings
by it, high first.

| Level | Meaning |
| --- | --- |
| High | Can stop the people it affects from completing a task, or make a task very hard for them or likely to fail. |
| Medium | Makes a task harder, slower or more confusing for the people it affects. |
| Low | An inconvenience; the task can still be done without much extra effort. |

The levels replace the four scores first proposed: critical and serious
became high, moderate became medium, and minor became low. High: an
item with nothing to say, a text field with no name, a clickable item
the keyboard may not reach, a change of context when TalkBack's focus
moved, touch targets under 24 by 24 dp, a text field with a content
description, conflicting reading order, text contrast below AA,
non-text contrast, a label not in the name, a field with no label, an
invalid field with no error, a status not announced, text gone by
itself, a change of context on focus or on input, and columns made with
spaces. Low: a redundant description, an image of text at AAA, a label
not at the start of the name, a first heading low on the screen, many
headings, and a line break in mid-sentence. Medium: the rest. These are
for the owner to change.

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
client's Markdown (see "Two readers"):

- the title in Word's Title style, then the opening paragraph, then
  "Contents", a table of contents over Heading 1 to 3, such as 1.5.2,
  each level indented further: each entry is a link to its heading, with
  the heading's number and text, then a dotted tab to its page number at
  the right margin. It listed Heading 1 and 2 until the owner asked, on
  2026-11-15: "we only show the first two levels of heading in the TOC
  e.g. 1.5, I would like 3 levels please";
- every other heading in Word's heading styles, Heading 1 to 6, a level
  below the Markdown's, numbered by the styles themselves, 1, 1.1, 1.1.1
  and so on, so that the numbers stay right if the document is edited;
  the first numbered heading starts a new page after the contents;
- the title in each page's header, and "Page 1 of 7" in its footer, from
  Word's PAGE and NUMPAGES fields;
- each page number in the contents a PAGEREF field to its heading's
  bookmark. LibreOffice works these out itself as it lays out the
  pages. Word is set to update the fields when it opens the document,
  after asking, and updates them before printing; until then they are
  empty, as only a layout gives a page number. The entries are not
  inside Word's TOC field: tried on 2026-11-14, LibreOffice showed the
  text stored in a TOC field as it was, so the page numbers it held
  could not be right, and the development PC opens Word documents in
  LibreOffice Writer;
- Word's current mode, not compatibility mode, so that its
  accessibility checker can check everything;
- tables with a repeated header row, rows that do not split across
  pages, and columns sized by the length of their text;
- bulleted lists, with Word's numbering;
- pictures inline, each with its alternative text, at most 3 inches wide
  for a tall picture and 6 for a wide one, and 5.5 high;
- links as real hyperlinks, reading as the source's title;
- each table with a caption, its heading's text, so that it is named in
  a screen reader's list of tables, and no empty paragraphs between
  blocks, as a screen reader reads each as blank;
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
and is never to be stated as a failure, that the impact levels are not
measured, and that the model is not to say whether the app meets WCAG.
Each statement is a claim citing its evidence, or says what is unknown
or that there is no evidence, as in the recording summary. The
auditor's Markdown labels each summary as inferred by a model; the
client's Word document says once, at its start, that AI is used in
generating the report, and whether with a cloud or a local model (see
"Two readers" and "Cloud or local").

## Cloud or local

The owner asked on 2026-11-15 for "a note in any report that uses it to
say that AI was used in the testing, and whether a cloud or local model
was used" (`android-vision-checks.md`, requirement 3). The summary's
opening sentence names the kind of model that wrote its accepted
answers:

| Accepted answers from | The client's sentence |
| --- | --- |
| A local model | "AI is used in generating this report, with a local model, and the final report is manually inspected." |
| A cloud model | "AI is used in generating this report, with a cloud model, and the final report is manually inspected." |
| Both | "AI is used in generating this report, with cloud and local models, and the final report is manually inspected." |
| No attempt recorded | "AI is used in generating this report, though whether its model was cloud or local was not recorded, and the final report is manually inspected." |

The auditor's Markdown says the same in its opening paragraph, such as
"a cloud model's inferences". A cloud model's attempt is one with no
model file, whose SHA-256 basis is the service's (`cloud-models.md`); a
reused answer counts as the model that wrote it, as its copy keeps that
model. Failed attempts do not count, as none of their text is in the
report. A summary with nothing written, as for a recording with no
findings, used no AI, so the client's document says nothing of it.

## Limits

- The findings are the recorder's checks of what was recorded; the
  report does not show that a screen meets WCAG.
- Warnings and information need a person to confirm them.
- The impact levels and remediation are fixed text for each kind of
  issue, not advice for the app.
- Colours measured on video frames are approximate.
- The framework is the libraries bundled in the app, not what drew each
  screen; not finding one does not mean the app is native Android.
- The report reads recording files only, not recordings whose events
  are only in the database.
- The report covers the whole recording.
