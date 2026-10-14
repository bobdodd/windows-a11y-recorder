# Recording Summary

## Status

Proposed on 2026-10-13, for the owner to agree before building. Not built.
The first task kind for the [analysis runner](analysis-runner.md), chosen
by the owner the same day: a summary of the recording that describes each
page visited and the site or application being tested. It is built from
the recorder's own collation of the recorded data and a model's text
analysis, then enhanced with vision analysis of the rendered pages. Each
stage writes a Markdown file. Listed in
[outstanding work](analysis-outstanding-work.md).

## Two task kinds

The Analysis menu has two items:

- **Summarise the recording** writes `summary.md`.
- **Add vision to the summary** writes `summary-with-vision.md`. It needs a
  finished summary of the same recording. Chosen before the summary has
  finished, it is queued to run after it.

Both files are written to the task's folder under the session folder's
`analysis` folder, as the runner design describes.

## What counts as a page visit

The recorder's own code divides the recording into visits. The result is
derived, and is labelled so in the report.

- A visit starts at each committed cross-document navigation of a tab's
  page, the primary main frame, and ends at the tab's next one or at the end
  of the recording. A committed record says whether Chromium treated the
  commit as cross-document or same-document
  ([navigation and document identity](navigation-document-identity-evidence-model.md)).
- A same-document commit that changes the URL's path or query, such as a
  route change in a single-page application, also starts a visit, labelled
  same document. A change of the fragment alone, the part after `#`, does
  not.
- Returning to a URL already visited is a new visit, noted as a return to
  the earlier visit by URL alone. Deciding when two visits show the same
  view is the separate unique view proposal, not this task.
- Windows of other programs are listed with their recorded titles and the
  times they were in front. Nothing more is known about them until the
  whole screen's accessibility tree is recorded, which is deferred.

The navigation evidence alone does not prove that a navigation produced a
distinct view the participant saw
([navigation and document identity](navigation-document-identity-evidence-model.md)),
so the report calls these page visits and does not claim more.

## Stage 1: the summary

### Collation by the recorder's code

For each visit, the recorder collects these facts, each with an evidence
reference:

- the URLs, the document titles over the visit, and the start and end
  times;
- how the visit began, as far as it is recorded: whether Chromium
  classified the navigation as started by the page's content, whether it
  ended in an error page, and the participant's last key or click before
  it. Chromium's own kind of navigation, such as a typed address, back or
  forward, or a reload, is not recorded, so the report does not state it;
- the page's structure from the recorded accessibility tree at the visit's
  last checkpoint: landmarks, headings with their levels, the names of form
  fields and buttons, and how many links and images there are;
- the main text: the opening text of the main landmark, or of the page
  where there is none, up to a set length;
- what the participant did: keys by kind, NVDA's inferred commands and
  modes, clicks and what they hit, and which fields text was typed into;
- what NVDA had to announce on arriving, and at each heading the
  participant moved to, labelled inferred as in the player;
- dialogs and popups that opened.

The values typed into password fields are not given to the model. The
summary says a value was typed into the field, without the value. Other
typed text is given verbatim, as the privacy policy permits for test data.

Each visit's facts are held to a set size, so a long page cannot overflow
the model's context. Where facts are cut, the collation says how many were
left out, for example "112 more headings", and the model is told.

### Text analysis by a model

There is one request for each visit, then one for the whole recording.

- **Each visit:** given that visit's facts, the model describes the page,
  what it is for, and what the participant did there.
- **The whole recording:** given each visit's URL, titles, and headings,
  with the model's visit descriptions labelled as inferred, it describes
  the site or application tested, who it appears to be for, and the course
  of the session.

The model is told to describe only what the facts support, to name
programs and windows only as the facts name them, and to say unknown where
the facts do not say. Every claim cites the evidence references it rests
on, and the runner's checks apply. The default profile needs text only;
on this PC, that is Qwen3.6-35B-A3B.

## Stage 2: the summary with vision

### Pictures of the rendered page

For each visit, the recorder chooses the visit's last captured frame, the
page as the participant left it, and rebuilds the page at that frame with
the same recreation the player's Inspect page at this frame uses
([page recreation](page-recreation.md)). It is opened at the recorded
viewport size. A picture of the whole page, beyond the viewport, is taken
through DevTools' `Page.captureScreenshot` with `captureBeyondViewport`
([Chrome DevTools Protocol, Page domain](https://chromedevtools.github.io/devtools-protocol/tot/Page/)).

The picture is cut into viewport-sized tiles, up to a set number for each
visit, with the number left out stated. Each tile has its own evidence
reference, giving the frame, the visit, and the tile's position on the
page. In the trial, one full-screen frame became about 2,000 image tokens
and took 45 seconds, so a visit of four tiles takes a few minutes.

A recreation is a view of recorded evidence, not the page itself
([page recreation](page-recreation.md)). Its geometry and theme checks do
not establish that it matches the captured pixels. The report says the
pictures are recreations. Where a recreation cannot be built, as with a
recording without Chromium evidence, the visit is described from its facts
alone and the report says so.

Whether the recreation browser can be run by the worker without showing a
window is to be checked when this is built.

### Vision analysis by a model

For each visit, the model is given the visit's facts, its description from
the summary, and the tiles. It is asked for what the facts cannot give:
layout, imagery, colour, emphasis, and what stands out on the page. Visual
claims cite the tile they rest on. The recording's overall description is
then written again with the enhanced visit descriptions. The default
profile needs images; on this PC, that is Qwen3.6-35B-A3B with its image
add-on.

## The report

Both files follow the same layout:

```markdown
# Summary of recording 20261013-173155

Inferred by Qwen3.6-35B-A3B (UD-Q4_K_M), profile "This PC", 2026-10-13.
Every statement below is a model's inference from the recorded evidence
cited after it. Page visits are derived by the recorder.

## The site tested
(prose, each claim followed by references such as [E3] [E17])

## Page visits

### 1. Home | CNIB, 00:00:04 to 00:01:12
(prose)

### 2. ...

## Other windows
(recorded titles and times)

## Evidence
| Ref | Time | Evidence |
|---|---|---|
| E3 | 00:00:04.210 | Navigation committed, cross-document, https://cnib.ca/en |
| ... | | |

## How this was made
(the profile, the model file and its SHA-256, the settings, the prompt
template versions, and each attempt's outcome)
```

The summary with vision adds, under each visit, the tiles it used, and
says the pictures are recreations.

## Required tests

- **Unit tests:** dividing visits, including same-document commits, a
  fragment-only change, a return to a URL, several tabs, and a recording
  with no navigations; the collation's facts and its size limit; password
  values withheld; choosing frames and cutting tiles; writing the report
  from checked output, with every reference in the evidence list.
- **Integration tests:** both task kinds on a recording built by the
  tests, with a stand-in endpoint that gives set answers,
  checking the files, the catalog rows, and the citation checks; the vision
  task queued before the summary has finished.
- **System test on the target machine:** both task kinds on the cnib.ca
  recording of 2026-10-13 with Qwen3.6-35B-A3B, checking on screen that
  both reports appear in the Analysis tab, open in the reading view, and
  move the playhead from a reference.

## Questions to settle

1. Pictures: the recreation alone, as proposed, or the captured screen
   frame as well, which shows exactly what was on screen but only the
   viewport.
2. Which moment of a visit: its last captured frame, as proposed, or also
   the first frame after the page finished loading.
3. Withholding password field values from the model, as proposed.
4. Whether a change of the URL's query alone should start a visit, as
   proposed, or only a change of path.
