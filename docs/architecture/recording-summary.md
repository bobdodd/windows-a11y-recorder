# Recording Summary

## Status

Proposed on 2026-10-13 and agreed by the owner the same day. Stage 1 was
built the same day, as step 2 of "Build order" in
[analysis runner](analysis-runner.md); see "Implementation status". The
player queues it from step 3, built on 2026-10-14. Stage 1 passed its
system test on the target machine on 2026-10-14, on the cnib.ca recording,
and Run again gave the same report; see
[the validation](../validation/analysis-runner-2026-10-14.md). Stage 2
is built in two steps, agreed by the owner on 2026-10-14: 4a, the moments
and their captured frames, built the same day, which passed on the
target machine on 2026-10-14 with build f1b2229, on the cnib.ca
recording, all 12 of its steps accepted at the first attempt; and 4b,
the recreated pages, not built, its design revised by the owner the same
day (decision 7). See "Stage 2 implementation status" and "Decisions
(2026-10-14)".
The first task kind for the [analysis runner](analysis-runner.md), chosen
by the owner the same day: a summary of the recording that describes each
page visited and the site or application being tested. It is built from
the recorder's own collation of the recorded data and a model's text
analysis, then enhanced with vision analysis of the rendered pages and of
the captured frames. Each stage writes a Markdown file. The owner answered
the four questions raised with it the same day; see "Decisions
(2026-10-13)". Listed in
[outstanding work](analysis-outstanding-work.md).

## Implementation status

Stage 1 built on 2026-10-13, in `Recorder.Analysis/Summary`, as the task
kind `recording-summary`, version 1. Migration 0035 adds the task's
session folder and the capability descriptions. These decisions were made
while building it:

- **The session folder is held on the task.** The catalog holds a
  recording by its session key, not its folder, so the player gives the
  folder when it queues the task (`analysis_tasks.session_directory`). A
  task without one fails, saying so.
- **Only recordings with a recording file.** The summary reads the
  recording file through the player's own readers. A recording whose events
  are in the database's evidence tables fails, saying that the summary
  reads recording files only.
- **Roles and names are Chromium's.** The recorder starts Chromium with
  `--force-renderer-accessibility`, so a page's accessibility records give
  each node's role and computed name, with Chromium's role names such as
  `main`, `heading`, `link`, `textField`, `comboBoxSelect`, `dialog`, and
  `rootWebArea`
  ([ax_enum_util.cc, Chromium](https://source.chromium.org/chromium/chromium/src/+/main:ui/accessibility/ax_enum_util.cc)).
  A page with no accessibility records is read from its tags, and the
  facts say so. Text is the static text Chromium exposed, so text that was
  not rendered is left out; without records, it is the text nodes outside
  `script`, `style`, `noscript`, `template`, and `head`.
- **The title** is the root web area's name, or else the `title`
  element's text. Only the title at the visit's end is given, not each
  title over the visit.
- **The structure is read from the page at the visit's end**, as recorded
  then. Shadow trees are not read.
- **The fragment's place** is found by HTML's rule for the indicated part
  of the document: an empty fragment is the top; then the first element
  with that ID, or the first `a` element with that name; then the same
  after percent-decoding; then `top` in any case is the top
  ([HTML Standard](https://html.spec.whatwg.org/multipage/browsing-the-web.html#scroll-to-the-fragment-identifier)).
- **The link that caused a jump** is the link focused when the change
  committed, or else the only link in the page pointing there. NVDA's
  activation requests are not yet used. Where neither finds one, the step
  is an "in-page jump" whose cause is not recorded.
- **"Main content replaced"** means the main landmark's children, or the
  body's where there is no main landmark, at the change and just before
  the tab's next commit have no node in common.
- **A return** names the most recent earlier visit to the same URL.
- **Typed text** is each text control whose value at the visit's end
  differs from its value at the start, given verbatim.
- **Observed assistive technology** is the screen readers running, with
  their versions, and full screen magnification above 100% from the
  frames. Magnifier's lens and docked views are not included.
- **What the participant did** gives keys by kind with what happened to
  each, NVDA's kept commands, mouse button presses with NVDA's inferred
  mouse outcome, focus moves, and every line of what NVDA had to announce
  during the visit, up to the limits. NVDA's modes and the element a click
  hit are not yet given.
- **Other windows** are the front window records whose process is not the
  browser's.
- **The prompt templates** are `recording-summary-visit` and
  `recording-summary-recording`, version 3 since 2026-10-14. Version 2
  tells the model that each section holds only its own paragraphs, to
  summarise the activity with repeated actions grouped rather than one
  statement for each fact, and to write the JSON on one line; see
  "Decisions (2026-10-14)" in [analysis runner](analysis-runner.md).
  Version 3 names what goes in each of the whole recording's sections, and
  tells the model not to repeat a visit's description there: in its second
  run on the target machine, the whole recording's "site" held the visit's
  page, purpose, and activity, and "session" repeated the activity. Each
  visit is a step,
  `visit-0001` upward, and the whole recording the step `recording`. A
  recording with no page visits asks no model and says so.
- **Observed running** at the start of the recording, and at each visit's
  start, is a fact with an evidence reference at that time, like every
  other fact. Without one, the whole recording's step failed on the target
  machine on 2026-10-14: "NVDA 2026.2 was observed running at the start of
  the recording" was a true claim with nothing to cite.
- **The report** is `analysis\<task>\summary.md` in the session folder.
  The evidence list gives only the references the report cites. The
  auditor's description is listed as not in the recording.
- **The capability item** on the Analysis menu, Describe the participant's
  capabilities..., was built with the player in step 3, on 2026-10-14.

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
- Every same-document commit that changes the URL also starts a visit,
  labelled same document, so that route changes in single-page
  applications are covered. That includes a change of the query alone.
- A change of the fragment alone, the part after `#`, is judged from the
  page's structure, because skip links, back to top links, and tables of
  contents change only the fragment, while some single-page applications
  route by it. See "Fragment changes".
- Returning to a URL already visited is a new visit, noted as a return to
  the earlier visit by URL alone. Deciding when two visits show the same
  view is the separate unique view proposal, not this task.
- Windows of other programs are listed with their recorded titles and the
  times they were in front. Nothing more is known about them until the
  whole screen's accessibility tree is recorded, which is deferred.

### Fragment changes

A fragment-only change is an in-page jump, listed as a step within the
page's visit, when the recorded document has the place it points to.
HTML's own rule decides that: an empty fragment, or `top` in any case
where no element has that ID, means the top of the document; otherwise
it is the first element whose ID equals the fragment, or the first `a`
element whose `name` does, tried before and after percent-decoding
([HTML, selecting the indicated part](https://html.spec.whatwg.org/multipage/browsing-the-web.html#scroll-to-the-fragment-identifier)).
The recorder applies the rule to the recorded document at that moment.

The step is named from the link that caused it, where the recording shows
one: the link clicked or activated just before the change, and its `href`
in the recorded document. From the page's structure:

- **Skip link:** the link comes before the main landmark, or before the
  page's first heading where there is no main landmark, and points to the
  main landmark, or to an element at its start or before its first
  heading.
- **Back to top link:** the link comes after the start of the main
  content, and points to the top of the document or to an element before
  the main content.
- **In-page link:** any other link to a place on the page, such as a table
  of contents.

These names are derived, and say which rule gave them. A fragment change
is a visit of its own, labelled fragment route, when the recorded document
has no place it points to, or when the page's main content is replaced
after it, as in a single-page application that routes by the fragment.

A skip link or back to top link that moves focus or scrolls with a script,
without changing the URL, does not appear here. It appears as a moment of
the visit, a click and a scroll, and the summary describes it from those.

The navigation evidence alone does not prove that a navigation produced a
distinct view the participant saw
([navigation and document identity](navigation-document-identity-evidence-model.md)),
so the report calls these page visits and does not claim more.

## The participant's capabilities

The participant's capabilities are an input to the analysis. What a
participant does with a page, and what the journey means, depends on what
they are able to do: whether they work by sight, by hearing through a
screen reader, by magnification, by voice, by switch, or by head pointer
or eye tracking.

The capability work from the owner's PhD will be the model for this
later. It separates what a person is able to do from what they would
prefer: "A capability model says what a person is able to do and says
nothing about what they would rather"
([Adaptation, a11ybob.com](https://a11ybob.com/adaptation),
[Describing people to computers](https://a11ybob.com/adaptation/describing-people-to-computers)).
Adopting that model is for later, and gets its own design.

Until then:

- A session can hold a short description of the participant's
  capabilities, written by the auditor, through an item on the Analysis
  menu. It is stored in the catalog with when it was written. A change
  keeps the earlier text, and each report records the text it used.
- The description goes to the model labelled reported: a person's
  statement, not an observation.
- The assistive technology the recorder detected, such as NVDA or
  Magnifier running, goes in as observed. It shows what was running, not
  what the participant is able to do, and the model is told so.
- With no description, the participant's capabilities are unknown. The
  journey then says what was on the screen and what was announced, and
  does not say what the participant saw or heard.

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

Typed text is given verbatim, password fields included. Entering a
password is part of what is tested, and the recorder is used with
short-lived test accounts in a test environment, ideally sandboxed, as the
privacy policy describes.

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

## Stage 2 implementation status

Step 4a built on 2026-10-14, in `Recorder.Analysis/Summary`, as the task
kind `recording-summary-vision`, version 1, shown in the player as
"Summary with vision". These decisions were made while building it:

- **It builds on a summary.** Add vision to the summary, on the Analysis
  menu, queues the task after the recording's newest summary that is
  queued, running, or finished, so the worker runs it once that summary
  has finished, and fails it if the summary does not finish. Run again
  does the same. Where there is no summary, the player says to summarise
  the recording first. A task naming no summary fails, saying so.
- **The summary must have been written from the same facts.** The vision
  task collates the recording again, with the summary's limits, and
  compares each visit's references with those given to the summary's
  accepted step for that visit. Where any differ, it fails, saying to
  summarise the recording again. A recording with no captured frames in
  its visits fails, saying there is nothing to add vision from.
- **All of it runs on the image profile**, its text requests too, so that
  the model is not changed between requests (decision 2 below). The menu
  item is available only when there is a default profile for images.
- **The moments, as built:**
  - **Settled:** the first layout change set of the page's document,
    within 5 s of the visit's start, followed by 1 s without another; the
    moment is 1 s after it. If none is, the moment is 5 s after the visit
    starts. A visit with no layout change recorded is taken as settled
    1 s after it starts, and the moment says so.
  - **NVDA's windows:** each of NVDA's own windows coming to the front and
    leaving it, from the player's NVDA window spans.
  - **The page's popups:** each opening and closing.
  - **Animations:** the start, middle, and end of each animation recorded
    on the compositor, with its end paired by the compositor animation's
    ID. Animations run on the main thread are not yet used. An animation
    with no recorded end gives its start only.
  - **A large change:** a layout change set, after the settled moment,
    of at least 50 elements, from its `recordedNodeCount`. The value 50 is
    a starting value set while building, not one the owner agreed; it is
    a setting.
  - **Clicks:** each mouse button press.
  - **Scrolls:** a scroller's vertical position moving by at least the
    viewport's height from where it was at its last such moment, or from
    the top, each scroller separately. Where the viewport's height is not
    recorded, scrolls give no moments.
  - **Typing:** the keys received by the page while one text control had
    the focus, at the last of them.
  - **The last frame:** the last captured frame before the visit ends.

  Reasons less than 2 s apart are one moment, at the first frame at or
  after the latest of them. Reasons that fall on the same frame are one
  moment. Over the limit of 10, the settled and last moments are kept
  first, then NVDA's windows and popups, typing, clicks, large changes,
  animations, and scrolls, earliest first within each, and the number
  left out is stated.
- **Layout changes, scrolls, and compositor animations are read from the
  recording file** in one more pass, since the playback index does not
  keep them.
- **Each moment is a step**, `visit-0001-moment-01` upward, with a request
  of its own: the capabilities, the visit's address and title, the reasons
  for the moment, the visit's facts since the moment before, and the
  captured frame, sent whole. The frame and each reason are entries in the
  evidence list with references of their own. Then each visit's enhanced
  description is the step `visit-0001`, given the visit's facts, its
  description from the summary, and its moments' descriptions, both
  labelled inferred, with a fourth section, the participant's journey.
  Then the whole recording is the step `recording`.
- **The prompt templates** are `recording-summary-vision-moment`,
  version 2 since 2026-10-14, and `recording-summary-vision-visit` and
  `recording-summary-vision-recording`, version 1. Version 2 of the moment
  template adds "Summarise what is on the screen as an overview; do not
  list each control, label, or time shown." (decision 6 below).
- **The report** is `analysis\<task>\summary-with-vision.md`, laid out
  as the summary, titled "Summary with vision", with each visit's moments
  under "The moments" and the moment limits under "How this was made".
  It says that recreated pages are not yet used.
- **Image profiles on this PC** are served with llama.cpp's multimodal
  projector, the image add-on, given with `--mmproj`
  ([llama.cpp multimodal](https://github.com/ggml-org/llama.cpp/blob/master/docs/multimodal.md)).
  The local server knows each profile by its model name, one section of
  its preset each, so the settings file reports two local profiles with
  the same model name as a problem.

## Stage 2: the summary with vision

### The moments of a visit

A page is more than its state when the participant left it: dialogs open,
animations run, content changes, and the participant scrolls. The
recorder's code therefore chooses the moments of each visit from the
recorded evidence:

- the first moment the page is settled: the first captured frame after
  the page's recorded layout stops changing for a set time, or a set time
  after the visit starts if it does not;
- each dialog or popup opening and closing, from the page's popups and
  NVDA's dialog episodes;
- each animation that runs, at its start, its middle, and its end, from the
  recorded animations;
- each large change in the page's content or layout, from the layout
  checkpoints;
- each click, each scroll to a new part of the page, and each field the
  participant typed into;
- each announcement the page makes to assistive technology, whether or not
  it is in view: an alert, a status message, or a live region appearing or
  changing (decision 7, from step 4b; what the recorded accessibility
  events give for these is to be checked before it is built);
- the last captured frame before the visit ends.

Each moment is labelled derived, with what chose it. Moments close
together are merged. From step 4b there is no limit on the number of
moments in a visit (decision 7); step 4a has a limit of 10.

### Two kinds of picture

- **The captured frame** at each moment. It shows exactly what was on the
  screen, including other windows, but only the part of the page in the
  window. It describes the participant's journey: where they were on the
  page, what was open, and what changed.
- **The recreated page at every moment** (decision 7), as it was at that
  moment's time. The page is rebuilt with the same recreation the
  player's Inspect page at this frame uses
  ([page recreation](page-recreation.md)), at the recorded viewport size.
  A picture of the whole page, beyond the viewport, is taken through
  DevTools' `Page.captureScreenshot` with `captureBeyondViewport`
  ([Chrome DevTools Protocol, Page domain](https://chromedevtools.github.io/devtools-protocol/tot/Page/)).
  It describes the page itself at that moment, including what is out of
  the participant's view: the frame may be far down the page or under
  high magnification, and a toast or status message may appear anywhere
  on the page, small on a large and busy one.

The recreated picture is cut into viewport-sized tiles, all of them, with
no limit on their number, and the tiles are not scaled down, so that a
small toast is as legible in its tile as on the screen. Whether the
model's image add-on reduces a tile's resolution itself is to be checked
when this is built. Every captured frame and every tile has its own
evidence reference, giving the frame, the moment, and for a tile its
position on the page. Each tile is labelled with its position, and the
model is told which part of the page was in view in the captured frame,
and, where Magnifier was on, what was magnified, from the recorded
Magnifier evidence, so that it can say what changed out of view.

In the trial, one full-screen frame became about 2,000 image tokens and
took 45 seconds. The cnib.ca page scrolled to at least 6,017 CSS pixels
from the top with a viewport 925 high, so it is at least 8 tiles; with 10
moments, a visit is around 90 picture requests at about 45 to 70 seconds
each, one to two hours, which the queue can absorb. Making this faster is
left until it is shown to work.

A recreation is a view of recorded evidence, not the page itself
([page recreation](page-recreation.md)). Its geometry and theme checks do
not establish that it matches the captured pixels. The report says which
pictures are recreations. Where a recreation cannot be built, as with a
recording without Chromium evidence, the visit is described from its
facts and its captured frames, and the report says so.

A captured frame shows what was on the screen, not what the participant
saw. The journey says the participant saw something only where the
participant's capabilities say they work by sight; otherwise, whether
they saw it is unknown. See "The participant's capabilities".

Whether the recreation browser can be run by the worker without showing a
window is to be checked when this is built.

### Vision analysis by a model

From step 4b, each moment is several requests, then one for each visit
(decision 7):

- **The moment's frame:** the model is given the captured frame, the facts
  around that moment, and why the moment was chosen, and describes what
  was on the screen and what changed, as in step 4a.
- **The moment's recreated page:** the model is given the tiles, in groups
  that fit one request on the profile's server, with their positions,
  the part in view, the moment's facts, and why it was chosen, and
  describes what the facts cannot give: layout, imagery, colour,
  emphasis, what stands out, and any toast or message, in view or not.
- **The moment's description:** a text-only request is given the frame's
  and the tiles' descriptions, labelled inferred, and writes the moment's
  description, each statement citing the frame or tile it rests on.
- **Each visit:** a text-only request is given the visit's description from
  the summary and its moments' descriptions, labelled inferred, and writes
  the visit's enhanced description and its part of the journey.

Visual claims cite the frame or tile they rest on. The recording's overall description is
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

The summary with vision adds, under each visit, the participant's
journey through its moments, with the frames and tiles used, and says
which pictures are recreations.

## Required tests

- **Unit tests:** dividing visits, including same-document commits, a
  fragment-only change, a return to a URL, several tabs, and a recording
  with no navigations; fragment changes judged by HTML's rule, including
  an empty fragment, `top`, a name anchor, and a percent-encoded ID; skip,
  back to top, and in-page links named from the page's structure; a
  fragment route with no target or with the main content replaced; the
  capability description, its earlier versions, and its absence;
  the collation's facts and its size limit; choosing and merging moments,
  and the limit on them; cutting tiles; writing the report
  from checked output, with every reference in the evidence list.
- **Integration tests:** both task kinds on a recording built by the
  tests, with a stand-in endpoint that gives set answers,
  checking the files, the catalog rows, and the citation checks; the vision
  task queued before the summary has finished.
- **System test on the target machine:** both task kinds on the cnib.ca
  recording of 2026-10-13 with Qwen3.6-35B-A3B, checking on screen that
  both reports appear in the Analysis tab, open in the reading view, and
  move the playhead from a reference.

## Decisions (2026-10-14)

1. **The recorder's own windows stay in "Other windows"**, and in the
   facts given to the model. The owner's decision, from the first report
   on the target machine, which gave "Recorder.App, \"Windows
   Accessibility Recorder\"" there and in the session: the recorder is a
   genuine window in the recording.

The owner's answers, the same day, to the questions raised before
building stage 2:

2. **Stage 2 is built in two steps.** Step 4a, the moments and their
   captured frames, is built first and tested on the target machine; then
   step 4b, the recreated pages, after a check that the recreation
   browser can run without a window.
3. **A second profile, "This PC, with images"**: the same model with its
   image add-on, the default for images. All of stage 2, its text
   requests too, runs on it.
4. **The limits, all settings:** 10 moments for each visit; moments less
   than 2 s apart merged; settled after 1 s without a layout change, or
   else 5 s after the visit starts; 6 tiles. The limits of 10 moments and
   6 tiles are replaced from step 4b by decision 7.
5. **In step 4b, one request for each tile**, with the tiles' descriptions
   combined by the visit's text request. Replaced by decision 7.

From the first run of step 4a on the target machine, the same day:

6. **A moment's screen is summarised, with no limit on the answer.** The
   run, with "This PC, with images", failed at `visit-0001-moment-09`:
   "The output stopped at the output limit of 8192 tokens." Moments 1 to
   8 had used 613 to 1,233 output tokens each. Moment 9's frame showed
   the recorder's own player, and the model gave a statement for each of
   its controls and then for each time shown on its timeline, 128
   statements, for example "The timeline view shows a '00:00:54.926'
   timestamp in the top-right corner of", where the output stopped. A
   limit on the number of statements was proposed, which llama.cpp would
   enforce from the answer's schema
   ([llama.cpp grammars](https://github.com/ggml-org/llama.cpp/blob/master/grammars/README.md)).
   The owner declined it: "I don't like artificial limits unless
   absolutely forced by the platform and resources", since a page, such
   as a digital map, may hold a very large number of elements, "so I do
   agree that we need, in the first instance to summarize a page, even
   though we may ask for much more detail of particular areas later". So
   the moment prompt, version 2, asks for an overview, and the output
   limit stays at 8,192 tokens. Every answer of that run was indented
   JSON over many lines, although the prompt asks for one line.

From the review of the report of step 4a, which passed on the target
machine the same day:

7. **Step 4b recreates the page at every moment, whole.** It had been
   designed to recreate the page once for each visit, at its first settled
   moment and after a large change, cut into at most 6 tiles. The owner
   disagreed: "The frame may be well down the page, or in high
   magnification and we need to capture toasts and system messages
   generally which may be off-screen at any time. Toast can seem small on
   a large and busy page. You are falling into the trap of making highly
   efficient designs that don't work rather than optimizing those that
   do". Agreed with the owner:
   - each moment has the recreated page as it was at that moment, as well
     as its captured frame;
   - the whole page is cut into viewport-sized tiles with no limit on
     their number, not scaled down, each labelled with its position, and
     the model is told what was in view and what Magnifier showed;
   - each moment is several requests, the frame, the tiles in groups that
     fit one request, and a text request combining them, so a visit takes
     about one to two hours on this PC;
   - each announcement the page makes to assistive technology, an alert,
     a status message, or a live region appearing or changing, is a
     moment, whether or not it is in view;
   - there is no limit on the number of moments in a visit.

   Windows notifications and other system messages are not part of the
   page, so the recreation cannot show them; they are in a captured frame
   only when on the screen. Whether the recording holds other evidence of
   them is unknown; the whole-screen UI Automation tree, in
   [outstanding work](analysis-outstanding-work.md), is where it would
   come from.

## Decisions (2026-10-13)

The owner's answers to the questions raised with the design:

1. **Captured frames are used as well as recreations.** They help scope
   the participant's activity and describe the journey, assuming the
   participant is sighted.
2. **A visit is described through its moments, not only its last frame.**
   Dialogs that open and animations that run are part of the page's
   description and of the journey. See "The moments of a visit".
3. **Typed text goes to the model verbatim, passwords included.**
   Password entry is a valid part of an accessibility test, and the tool
   is used in a test environment with short-lived test accounts, ideally
   sandboxed. There is no personal information to consider.
4. **Every URL change is a new visit**, to cover single-page
   applications, including a change of the query alone. A fragment-only
   change is listed as a step within the page's visit, as proposed under
   "What counts as a page visit".

Two further answers, the same day:

5. **Fragment changes are judged from the page's structure**, so that skip
   links and back to top links are recognised. See "Fragment changes".
6. **The participant's capabilities are an input to the analysis.** The
   capability work from the owner's PhD is to be the model later. See
   "The participant's capabilities".

## Questions to settle

None at present.
