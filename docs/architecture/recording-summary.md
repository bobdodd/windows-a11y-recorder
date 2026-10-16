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
the recreated pages, its design revised by the owner the same day
(decision 7), built the same day and not yet tested on the target
machine. See "Stage 2 implementation status" and "Decisions
(2026-10-14)". On 2026-10-16 the vision stage was rebuilt as the layered
report, agreed by the owner on 2026-10-15 and 2026-10-16, which passed
on the target machine the same day with build 4199327, on the cnib.ca
recording; see "The layered report (built)".
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
  In step 4a it said that recreated pages were not yet used; step 4b
  replaced that (below).
- **Image profiles on this PC** are served with llama.cpp's multimodal
  projector, the image add-on, given with `--mmproj`
  ([llama.cpp multimodal](https://github.com/ggml-org/llama.cpp/blob/master/docs/multimodal.md)).
  The local server knows each profile by its model name, one section of
  its preset each, so the settings file reports two local profiles with
  the same model name as a problem.

### Step 4b

Built on 2026-10-14, as decision 7, after the check of the recreation
without a window passed on the target machine. The owner confirmed the
same day that the player's Inspect page at this frame still opened
normally after its code was moved to `Recorder.Recreation` for the check.
The task kind `recording-summary-vision` is version 2; the player now
queues each task for the version of its kind that the build has, as the
worker runs only that version. These decisions were made while building
it:

- **No limit on moments.** The moment limit is a setting that is null by
  default, for no limit, and the report says so. A limit, where set, keeps
  moments in the order of step 4a, with changes to live regions kept with
  NVDA's windows and popups.
- **Changes to live regions are moments,** read from the recorded DOM,
  since the recorder keeps no accessibility events for live regions. One
  more pass over the recording file's `browser.dom` records rebuilds each
  document's tree with the player's own `DomTreeRebuilder`. A live region
  is the nearest element, at or above the changed node, with an
  `aria-live` attribute or a live region role, alert, log, marquee,
  status, or timer
  ([WAI-ARIA 1.2, live region roles](https://www.w3.org/TR/wai-aria-1.2/#live_region_roles)),
  or an `output` element, whose role is status
  ([HTML-AAM, output](https://www.w3.org/TR/html-aam-1.0/#el-output)). Its
  politeness is its `aria-live` value, or its role's implicit value:
  assertive for alert, polite for log and status, and off for marquee and
  timer ([WAI-ARIA 1.2, aria-live](https://www.w3.org/TR/wai-aria-1.2/#aria-live)).
  Only polite and assertive regions count, and only the changes of
  `aria-relevant`'s default, additions and text: nodes inserted into a
  region with text, text changed in a region, and an alert inserted with
  its text. Removals, and content shown by changing an attribute such as
  `hidden` or a class, are not counted. The reason gives the region and
  the text verbatim, and says the page changed the region, never that
  NVDA announced it.
- **The page at each moment** is rebuilt at the moment's frame, as Inspect
  page at this frame rebuilds it, for the visit's own document, the page
  recorded at the frame whose key holds the visit's document token. It is
  opened in the instrumented Chromium found as the player finds it, with
  `--headless`, given 120 s to report itself built, measured, and pictured
  in tiles the height of its viewport from the top, the last cut at the
  page's foot, each at full size, through `Page.captureScreenshot` with a
  clip and `captureBeyondViewport`. The tiles are written to
  `analysis\<task>\pages\visit-0001-moment-01-tile-01.png` upward, and
  from 2026-10-15 each is then kept in the store of unique images,
  `analysis\images\<key>.png`, and removed from the task's folder (see
  "Unique images across the recording"), so that an auditor can open
  each picture from the evidence list. The recreation's own folder, under
  the worker's folder, is removed after each moment.
- **Where the page cannot be recreated,** as when Chromium is not found,
  the visit's document is not among the pages at the frame, or the
  recreation fails, the moment is described from its captured frame
  alone, and the report says why.
- **Tiles are grouped by their image tokens.** llama.cpp's image add-on
  counts one token for each 32 by 32 pixels of a Qwen-VL picture, a patch
  of 16 pixels merged two by two, and does not make a picture of up to
  4,096 tokens smaller
  ([llama.cpp clip.cpp](https://raw.githubusercontent.com/ggml-org/llama.cpp/master/tools/mtmd/clip.cpp):
  `set_limit_image_tokens(8, 4096)` and `n_merge` 2 for `QWEN3VL`). In
  step 4a a frame of 1920 by 1080 was about 2,040 image tokens, which
  agrees, so frames and tiles are not made smaller. Each request holds as
  many tiles as fit 4,096 image tokens, and at least one; this is a
  setting, chosen because the context on this PC is 16,384 tokens with
  answers of up to 8,192. A tile of 922 by 925 is 841 tokens, so four go
  in one request, and the cnib.ca page of 7,197 CSS pixels is 8 tiles in
  2 requests.
- **The steps of a moment** are `visit-0001-moment-01`, the captured
  frame as in step 4a; `visit-0001-moment-01-page-01` upward, one for each
  group of tiles; and `visit-0001-moment-01-described`, a text-only
  request that combines them. Each tile is an entry in the evidence list,
  giving its place on the page, whether any of it was on the screen, and
  its file, and saying that it is a recreation, not the page. A tile's
  request is given the reasons for the moment, the part of the page on the
  screen, from the recreation's scroll position and viewport, which are
  the recorded ones, the magnification, and what the recreation lacked:
  whether it was reported built, the address of each image not loaded,
  and how many requests were refused. Why an image is not loaded is still
  unknown.
- **The prompt templates** added are `recording-summary-vision-page`,
  version 1, then 2 (below), with one section, the page; and
  `recording-summary-vision-moment-combined`, version 1, then 2 and 3,
  with three: on the screen, the page as recreated, and what changed, in
  view or not.
  The visit's request is given each moment's combined description.
- **The first run on the target machine,** 2026-10-14, build 9759ceb,
  failed at `visit-0001-moment-01-described`. The page was rebuilt and
  pictured in 8 tiles of 922 by 925, about 3 s each, and the frame and
  both groups of tiles were accepted: the groups took 59 and 55 s to read
  their prompts of 4,999 and 4,830 tokens, and 65 and 56 s to write 1,756
  and 1,501 tokens. Each group restated the facts it was given before
  describing its tiles, 7 of its 25 statements and 9 of its 23, both
  beginning with the page's title and address. The combined description
  copied the groups' statements in order, so its statement 32, the
  second group's first, was the same as its first, and the answer was
  stopped as a repeat. It also cited "[E24]" with its brackets, which the
  reference check rejects. The owner agreed three changes the same day:
  - version 2 of the page template describes only what the tiles show,
    and does not restate the facts given;
  - version 2 of the combined template states each thing once, merging
    statements that say the same thing with all their references, and
    describes the page as an overview of its regions, not tile by tile;
    it, and version 2 of the vision visit and recording templates, say to
    cite a reference without its brackets, such as "E3". The summary's
    own templates are not changed;
  - a statement given to the combined description that is the same, word
    for word, as an earlier one in any of its descriptions is given once,
    with the references of both. This changes the input, not an answer.
- **The second run on the target machine,** 2026-10-14, build 043cbf1,
  accepted every step of moments 1 to 8 at the first attempt, 32 steps,
  and failed at `visit-0001-moment-09-described`. Its answer combined the
  descriptions as intended, the page as an overview of its regions from
  the header to the copyright line, but its third statement of the
  change, "It is unknown whether the mouse clicks affected the CNIB
  webpage or any other application, since the browser is not in view and
  no further context is provided.", was of kind claim, citing nothing,
  and was refused. Each moment took 4.5 to 6 minutes. The owner agreed the
  same day that version 3 of the combined template adds "A statement that
  says something is unknown, or that cites no reference, is of kind
  unknown; every claim cites at least one reference.", in the combined
  template only, so that the other steps' requests are unchanged and Run
  again can reuse them (the runner design's "Reusing steps when run
  again"). The answer is not relabelled in code: refused output is run
  again, not repaired.
- **The third run on the target machine,** 2026-10-14, build 8addabd, a
  run again of the second, reused moment 1's frame step and sent its two
  groups of tiles again, as each tile's file, in the task's own folder,
  was in the request. Its combined description failed in the same way:
  "What NVDA announced at this moment is not provided in the facts; only
  that it was running." was of kind claim, citing nothing. The owner
  agreed the same day to put a statement's fields in the order text,
  kind, evidence, in the answer's schema and in every template, raising
  every template's version by one (the moment template to 3, the page,
  visit, and recording templates to 3, the combined template to 4, and
  the summary's templates to 4), and to leave the tiles' files out of the
  requests (the runner design's decision 7, and its "Reusing steps when
  run again"). The same run's first group of tiles said the hero image of
  two people "is missing in the recreation due to a refused request",
  while describing it; the one image recorded as not loaded is "A Ball
  for All.jpg". It is noted, not changed.
- **The fourth run on the target machine,** 2026-10-14, build c790781,
  accepted every moment's steps, none with a statement of what is unknown
  labelled a claim, and failed at the visit step, `visit-0001`: its
  request, 20,927 tokens, was longer than the server's context of 16,384
  tokens. The owner agreed the same day to raise the image profile's
  context to 32,768 tokens and to restart where it failed, reusing the
  moments' steps (the runner design's "Reusing steps when run again").
- **Version 4 of the page template,** 2026-10-14: when several items are
  alike, such as cards that each end with the same button, each statement
  says which item it is about, by its heading, its label, or its place,
  so that no two statements are the same word for word. A description of
  moment 1's first tiles had been stopped at its second "Below the
  description is a yellow button labeled Read more." (agreed with the
  owner the same day). The other templates are unchanged.
- **The report** lists under each moment its recreated page's size,
  scroll position, and tiles, marking those on the screen, and what the
  recreation lacked, then the three parts. "How this was made" gives the
  templates, that there is no limit on moments, the image token budget,
  and that frames and tiles are sent at full size.

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
- each change the page makes to a live region, whether or not it is in
  view: an alert inserted, or text added to or changed in a status
  message or other live region (decision 7, from step 4b; read from the
  recorded DOM, as described under "Step 4b");
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
small toast is as legible in its tile as on the screen. The model's image
add-on does not reduce them either (see "Step 4b"). Every captured frame and every tile has its own
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

The recreation browser runs without a window, as the check below showed.

### The check of the recreation without a window

Built on 2026-10-14, as agreed with the owner, before step 4b. The
player's code that writes a page's recreation at a frame, for Inspect page
at this frame, was moved to the recreation library as
`RecordedPageAtFrame`, with no change to what it writes, so that the
worker uses the same code; the recreation library now refers to the
database library for the recording file's documents, and the analysis
library to the recreation library. The worker program, run as

`Recorder.AnalysisWorker.exe --recreation-check RECORDING TIME FOLDER [CHROMIUM]`

reads the recording file in the folder RECORDING, takes the last captured
frame at or before TIME, and rebuilds the first page the player lists at
it, its default. It opens the page in the instrumented Chromium with a
window, then again without one, with Chromium's `--headless`: Chrome's
headless and headful modes are now "unified", and since Chrome 132 the
old headless mode is a separate binary
([Chrome for Developers, headless](https://developer.chrome.com/docs/chromium/headless)).
Each time it waits until the builder reports the page built and painted
and its fonts ready, then takes a picture of the whole page through
`Page.captureScreenshot` with `captureBeyondViewport`, clipped to the
page's CSS content size from `Page.getLayoutMetrics`
([Chrome DevTools Protocol, Page domain](https://chromedevtools.github.io/devtools-protocol/tot/Page/)).
It writes both pictures to FOLDER with a report: the page used, each
picture's size and SHA-256, the page's images and how many were not
loaded or loaded lazily, its scroll position and viewport, how the window
was fitted to the recorded viewport, refused requests, timings, and
whether the pictures are the same. It does not use the catalog, and
writes nothing to the recording's folder. CHROMIUM defaults to the
player's default.

In the development sandbox, with a stock Chromium and a recreated page of
200 lines at viewports of 800 by 600 and of 1,600 by 925, both ways gave a
picture of the page's whole content size, and the window was fitted to
the viewport without emulation in both.

On the target machine on 2026-10-14, with build 3b94a0e and the
instrumented Chromium 156.0.8065.0, on the cnib.ca recording at
00:00:48.950, the check passed: the recreation runs without a window, so
step 4b runs it so.

- **Both ways** rebuilt https://www.cnib.ca/en, built and painted it with
  its fonts ready, in 9.2 s with a window and 6.7 s without, fitted the
  window's page area to the recorded viewport, 929 by 925, without
  emulation, at a device pixel ratio of 1, and gave a picture 922 by 7,197
  pixels, the page's whole CSS content size, scrolled to 6,017. Both
  refused 4 requests for which nothing was recorded.
- **The pictures differ in 2,902 of their 6,635,634 pixels.** 2,271 of
  them are Chromium's window size label, "929px × 925px", at the top right
  of the picture with a window, shown after the window is resized; the
  picture without a window has none. The other 631 differ by at most 11
  of 255, on the edges of curves.
- **Images:** the page held 15 images, 2 of them not loaded and 3 loaded
  lazily, the same both ways; the Blogs section's image is an empty box in
  both. Which images were not loaded, and why, is unknown; it is to be
  looked at in step 4b, as a picture of the page must say where it is not
  the recorded page.
- **The frame used** was at 00:00:48.745, not the frame shown at
  00:00:48.950: times are shown cut short to the millisecond, and the
  check took the last frame at or before the time's first nanosecond. The
  check now takes the last frame shown at or before the time. Moments are
  chosen from the recorded times, not from times as shown, so step 4a is
  not affected.

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
- **The page so far:** a text-only request is given the understanding of
  the page after the moment before, none at the first moment; the
  recorder's facts since the moment before; which tiles are the same
  file as at the moment before; and the moment's description, labelled
  inferred, and writes the understanding again, whole, as it stands after
  this moment (decisions of 2026-10-15).
- **Each visit:** a text-only request is given the visit's description from
  the summary, the page as understood after its last moment, and each
  moment's screen and change, labelled inferred, and writes what the page
  is for, what the participant did, and the journey.

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

The summary with vision adds, under each visit, the page as understood
through the visit and how it changed, the participant's journey through
its moments, and each moment's description, with the frames and tiles
used, and says which pictures are recreations.

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

## Decisions (2026-10-15)

### The page understood moment by moment

The owner's decisions, from the first run of the summary with vision to
finish on the target machine (task 01a13fd9 on the cnib.ca recording of
2026-10-13, every step accepted at its first attempt). Its visit's page
section held four paragraphs, the last three copied word for word as
its other three sections, and its session repeated the visit. The
recreated page was the same file at most moments: of its 8 tiles, 5 were
the same file at all 10 moments, one changed once, between moments 3 and
4, and two differed at moment 5 only. Yet each moment described the
whole page again, and the visit was given all 10 descriptions to
summarise. Two descriptions of tiles that were the same file also
disagreed: at four moments the description of tiles 5 to 8 was about 10
tokens, at the others 554 to 1,043.

1. **A visit's moments are of one page in time.** At the first moment
   nothing is known of it; each later moment adds to and corrects what
   is known, as content changes and moves and dialogs come and go
   (owner).
2. **Each moment's description stays as it was, clean and new.** The
   moment's frame, tiles, and description are not given what earlier
   moments found, so that each says what that moment's pictures show
   (owner).
3. **The page so far is a step of its own after each moment, text only.**
   It is given the understanding after the moment before, the recorder's
   facts since then, the recorder's comparison of the tiles, and the
   moment's description, and writes the understanding again, whole, in
   two sections: the page as understood after the moment, and what
   appeared, went, moved, or changed, and when. It is not given the
   pictures again (owner).
4. **The recorder compares the tiles, file by file.** Each tile is
   compared with the tile at the same place, of the same size, at the
   last moment before whose page was recreated: the same file, byte for
   byte, means that part of the recreated page did not change, so a
   different description of it is a difference between descriptions, not
   a change of the page. Where descriptions conflict, the understanding
   says so, citing both, and does not settle it.
5. **What the page itself repeats is not merged.** A page may be badly
   written; where it has the same link, heading, or text more than once,
   the understanding says so. Only descriptions of the same thing from
   different moments are merged, into one statement citing the
   references of all of them (owner).
6. **The visit summary uses the understanding.** The visit is given the
   understanding after its last moment and each moment's screen and
   change, not each moment's page again, and writes what the page is
   for, what the participant did, and the journey, each thing in the one
   section it belongs to. The report gives the understanding as the
   visit's page, then the visit's description, then each moment's
   description. The visit's request no longer grows by the whole page at
   each moment.
7. **No check refuses an answer for repeating itself across sections**
   (owner: the process is to be improved instead, and such a check may
   refuse what is right).
8. **The session is left as it is for now** (owner). It is given each
   visit's understanding with its description, as it was given the
   visit's page section before.

- **Templates:** `recording-summary-vision-page-so-far` version 1, and
  the visit template version 4. The frame, tile, and combined templates
  are unchanged, so a task run again reuses those steps.
- **Tests:** the page so far at the first moment, with no understanding
  and no comparison, and at the next, given the first's; the comparison
  of tiles the same file, a different file, and with no tile at the same
  place; a moment whose page was not recreated; the visit given the
  understanding and each moment's screen and change, not its page; the
  report giving the understanding as the visit's page; and a task run
  again reusing every moment's steps.

### The page so far at moment 10 (to be reworked, not settled)

Task 01a1407c, a fresh run of the summary with vision on the cnib.ca
recording on the target machine, 2026-10-15, build 3a11d5e, accepted 57
steps and failed at `visit-0001-moment-10-page-so-far`: "The output
stopped at the output limit of 8192 tokens." The page so far is written
again whole at each moment, and its answers grew at every moment: 2,773,
3,294, 3,729, 4,205, 4,998, 6,135, 6,209, 6,941, 7,534, and 8,192 output
tokens. Counted with the model's own tokenizer, which gives the cut
answer as exactly 8,192 tokens, the cut answer was:

- 2,636 tokens (32%) of line breaks and indentation: the answer was
  indented JSON over 905 lines, although the template asks for one line,
  as were the answers of every other step of the run;
- 2,333 tokens in 38 statements that repeat the recorder's own facts,
  such as focus moves, scrolls, animations, and the comparison of tiles;
- 3,223 tokens of the page's description with its evidence lists, which
  grow by one reference at each moment for each statement that holds,
  under decision 5 above.

The answer never reached its "changes" section: the facts of the visit
were being written into the "page" section instead. Ways offered were to
hold the answer to JSON on one line through the answer's grammar, not to
repeat the recorder's facts, and to have the model write only what is
added, corrected, or removed, applied by the recorder. The owner's
decision, the same day: "The change will be more radical, but it has to
wait until after our current optimization of images" (see "Unique images
across the recording").

### The layered report (built)

Proposed on 2026-10-15, from the owner's design of the same day, and
agreed by the owner the same day; built on 2026-10-16, as "As built"
below gives, and passed on the target machine the same day (see "On the
target machine" below). The trial of one step with
the answer's grammar comes first, since the rest is built on it. It replaces the page so far, whose answer is written
again whole at each moment and outgrew the output limit at moment 10
(see "The page so far at moment 10").

The owner's design: the report is built in layers. At its most basic it
is a review of the recorded data, which stage 1 gives. That is decorated
with what the captured frames show of the visual user experience, then,
where the browser is running, with what pictures of the browser's page
add that the frame does not show: interactions, and the modals, widgets,
and forms that appeared during the visit. "So the visual analysis is
supposed to expand on the existing knowledge, not start from scratch
again. But this is about user experience, not listing every element on
the page" (owner). As built, every vision step starts again: each
moment, each group of tiles, the combined step, the page so far, and the
visit step each write their whole description anew.

The owner's answers, the same day, to the questions raised with the
design:

1. **Each moment is still described alone, as recorded content.** The
   moment's description is kept "for potential use in further testing
   and documentation, so that we don't need to build it all again later
   ... So we can query the visual of any moment out of context. So it's
   indexed, recorded content" (owner). Decision 2 of "The page understood
   moment by moment" stands for these descriptions; the layers are built
   from them, not instead of them.
2. **An addition may elaborate or correct an earlier statement, or be a
   new statement** (owner: "the flexibility of both").
3. **The report has two chapters** (owner): a description of the pages,
   summarising what is known of each from all its visits, independent of
   the participant's journey; and a report of the user experience, which
   turns the layers into clean readable prose, with pictures, changes,
   tables, and graphs where they make sense. "We are telling a story"
   (owner).
4. **The visit and recording steps are replaced by the two chapters**
   (owner).

Proposed, to be agreed:

- **The moment records.** Each moment's captured frame is described
  alone, as now, and each group of its page's tiles as now, a group
  already described being given its earlier answer (see "Unique images
  across the recording"). These are the moment's record. An index of
  the records, `analysis\<task>\moments.json`, gives for each moment its
  visit, number, time, frame, the steps that describe it, and its tiles'
  keys, so that any moment's description can be found and read alone.
  The combined step is not needed for the layers and is dropped: the
  frame's and the tiles' descriptions are the record. The records are in
  the index only, not in the report (owner, 2026-10-15: "Index please.
  We may use it later but I wouldn't expect it in our report").
- **The layers, a step for each at each moment** (owner, 2026-10-15:
  "Layer 1 is only text for the recording data. Layer 2 gets the Frame
  (visual analysis of the frame), Layer 3 gets the Web page if it
  exists"). Layer 1 is the visit's stage 1 description, from the
  recorded data, text only. At each moment, the layer 2 step is given
  the visit's knowledge so far, each statement numbered by the recorder,
  and the visual analysis of the moment's frame, from its record; then,
  where the page was recreated, the layer 3 step is given the knowledge
  so far, now with layer 2's additions, and the analysis of the web
  page, the descriptions of only those groups of tiles not yet in the
  visit's knowledge. Neither is sent the pictures again: each is given
  the analysis in the moment's record. Each answers with additions only:
  layer 2 what the frame adds to the visual user experience, layer 3
  what the web page adds that the frame does not show. Each addition
  either names the numbered statement it elaborates or corrects, or is
  new. Where the moment disagrees with a statement, the addition says
  so, citing both, and does not settle it. A layer that adds nothing at
  a moment gives an empty answer. The visit's knowledge starts as layer
  1 and grows by each layer's additions, put in place by the recorder,
  not by the model, each labelled with its layer.
- **Why the output is bounded.** Each step's answer is what that moment
  adds, not the whole page again, so a moment that changes nothing costs
  a short answer, and the answer no longer grows with the visit. The
  request still grows with the visit's knowledge; a visit long enough to
  overflow the context would fail saying so, not be cut.
- **Chapter 1, the pages.** One step for each page, a page being a URL
  visited, as the report already treats a return to a URL ("What counts
  as a page visit"), given the knowledge of every visit to it, writes the
  page's description, independent of the journey: its regions, and the
  widgets, forms, dialogs, and messages it has or showed, and how it
  changed. When two visits show the same view is the separate unique view
  proposal.
- **Chapter 2, the user experience.** One step for each visit, given the
  visit's knowledge, its facts, and the story so far, writes that part
  of the story; then one step writes the story's opening, given the
  visits' parts. Its form is not set in advance (owner, 2026-10-15: "It
  will depend on the content within the recording. The report should
  take its cue from the ontologies used by the page content and
  structure. The model should have freedom of expression as far as
  possible"). So the instructions ask the model to take its terms and
  its structure from the pages' own, such as their headings, landmarks,
  and the names of their controls, and to choose for itself where prose,
  a list, a table, or a picture of a moment's frame tells the story
  best. Whatever form it chooses, each part cites the references it
  rests on, as every statement does now, so that the report stays
  checkable; the recorder puts in the pictures of the frames it chose.
- **Chapter 2's form, the owner's requirement** (2026-10-16): "a
  professional report for accessibility and usability engineers that is
  primarily prose with nested heading numbers as needed, the provides
  images (possibly partial) of referenced frames, or web pages when
  talking about specific layout patterns or content. When read within
  the app, it should still have citation links that move frames into
  view." The pictures are shown in the player's reading view (owner, the
  same day).
- **Chapter 2's form, as built (agreed, 2026-10-16).** Chapter 2's requests alone
  add a `form` to each statement: a heading, with its level,
  a paragraph's sentence, the default, or a picture, whose text is its
  caption and which cites the one captured frame or tile it shows. Other
  steps' requests are unchanged, so a run again still reuses them. The
  recorder, not the model, numbers the headings, 1, 1.1, 1.1.1, from
  their levels, and puts in each picture: in the Markdown file as an
  image of the frame's or the tile's file, and in the reading view as
  the picture scaled to the view's width, with its caption, whose
  references move the frames into view as every reference does. A tile
  is already part of a page, the height of the viewport. A part of a
  frame or a tile, such as one region or control, is to come after: the
  model works from the descriptions, not the pictures, so it cannot give
  a part's place in pixels, and the recorder would cut the part from the
  place the recording gives for what the model names, where the
  recording gives one, which is to be checked.
- **What is kept and what goes:** kept are the frame and tile steps, the
  store of unique images, and the evidence list; dropped are the
  combined step, the page so far, and the visit and recording steps of
  the vision stage. Stage 1 is unchanged.

- **Answers on one line, held by a grammar of the recorder's own (tried,
  not taken).** The
  instruction to write the JSON on one line has been ignored in every
  answer so far, and the request's own constraint allows it: each
  request sends the answer's JSON schema as `response_format`, and
  llama.cpp turns a schema into a grammar whose rule for the space
  between tokens is `| " " | "\n"{1,2} [ \t]{0,20}`, up to two line
  breaks and 20 spaces or tabs of indentation, with no option to change
  it ([json-schema-to-grammar.cpp](https://raw.githubusercontent.com/ggml-org/llama.cpp/master/common/json-schema-to-grammar.cpp)).
  So the indentation is within what the request allows. Proposed: the
  recorder writes the answer's grammar itself, in llama.cpp's grammar
  format ([llama.cpp grammars](https://github.com/ggml-org/llama.cpp/blob/master/grammars/README.md)),
  the same structure as the schema with no space between tokens, and
  sends it as `grammar` in place of the schema. The model could then not
  write a line break outside a string. In the cut answer at moment 10,
  that was 2,636 of its 8,192 tokens. Whether the chat endpoint of the
  server build in use takes `grammar` is unknown, as its documentation
  names only `response_format` for it
  ([llama.cpp server](https://raw.githubusercontent.com/ggml-org/llama.cpp/master/tools/server/README.md)),
  so it is to be tried on the target machine first, by a trial of one
  step. It changes every step's request, so no step already accepted
  would be reused after it. Built for trials only on 2026-10-15: a trial
  given `--one-line` sends the grammar as `grammar` in place of the
  schema, and its file says so (analysis-runner.md, "Trying a step with
  other sampling options"); a task's own steps still send the schema.
  Checked here the same day with llama.cpp b11607, the build in use, in
  its Linux build for the processor
  ([release b11607](https://github.com/ggml-org/llama.cpp/releases/tag/b11607)),
  with a tiny test model, stories260K
  ([ggml-org models](https://huggingface.co/ggml-org/models)): the chat
  endpoint took `grammar`, streamed as the worker streams, and the answer
  came back on one line in the answer's structure. A model that small
  says nothing of the answer's content; the trial on the target machine
  is still to come.

  The first trial on the target machine, the same day, build 1325b36, of
  step `visit-0001-moment-10-page-so-far` of task 01a1421c: the server
  took the grammar, and the answer was on one line, but empty,
  `{"page":[],"changes":[]}`, 10 output tokens, finish reason stop, and
  it passed the checks, which do not refuse an answer with no
  statements. Not measured, but likely: with no space or line break
  allowed after `{"page":[`, the tokens the model would have written
  next were refused, and at temperature 0 it took the most likely of
  those left, closing the list. So the grammar changes what is written,
  not only its layout. Agreed by the owner the same day: a second trial
  with a grammar that allows one space, or none, after a colon or a
  comma, and no line break, as JSON is usually written on one line; if
  that also fails, the schema stays, since the layered report's answers
  are what each moment adds and the indentation then costs far less.
  Checked here as before: the server took it, and the answer came back
  on one line in the answer's structure with single spaces.

  The second trial on the target machine, the same day, build b4a0191,
  of the same step: again empty, `{"page": [], "changes": []}`, 11
  output tokens, finish reason stop. So, as agreed, the schema stays:
  the task's steps send it as `response_format`, as before, and the
  layered report is built on it. The trial option `--one-line` is kept,
  for trying a grammar again later.

Questions to settle: none at present.

#### As built

Built on 2026-10-16. The task kind's version is 3. Its steps, in order:

1. **At each moment of each visit:** `visit-NNNN-moment-MM`, the frame
   alone, and `visit-NNNN-moment-MM-page-GG`, each group of the page's
   tiles, as before, a group already described in the recording given
   its earlier answer instead of being sent. Then the moment's record is
   added to `analysis/<task>/moments.json`: the visit, the moment, the
   frame's time and file, its reference and step, why the recorder chose
   it, and the page, recreated or not and why, its size and scroll, each
   tile's place, image key, file, reference, and whether it was on the
   screen, and each group's tiles and the step whose answer describes
   it, with the visit and moment it was first described at where it was
   not sent. The file is written again after each moment.
2. **Layer 2,** `visit-NNNN-moment-MM-layer-2`, template
   `recording-summary-vision-layer-2` version 1, text only: the
   participant's capabilities, the visit, its knowledge so far with each
   statement numbered `[K1]`, `[K2]`, and so on, and labelled with its
   layer, why the moment was chosen, the recorder's facts since the
   moment before, and the frame's description from its record.
3. **Layer 3,** `visit-NNNN-moment-MM-layer-3`, template
   `recording-summary-vision-layer-3` version 1, text only, where the
   page was recreated: the knowledge so far, now with layer 2's
   additions, the part of the page on the screen, what the recreation
   lacked, and the descriptions of the groups of tiles not yet in the
   visit's knowledge; a group already in it is named, not described
   again. Where every group is already in it, the step is not sent, the
   task's record of steps not sent says why, and the report lists it.
4. **Chapter 1,** `page-NN`, template
   `recording-summary-vision-page-chapter` version 1: one step for each
   URL visited, in the order first visited, given each visit's knowledge
   without the statements' numbers, with sections `page` and `changes`.
5. **Chapter 2,** `visit-NNNN-story`, template
   `recording-summary-vision-story` version 1, one for each visit, given
   the visit's facts, its knowledge, its moments with their frames' and
   tiles' references, the section of chapter 1 that describes its page,
   and the parts before it; then `story-opening`, template
   `recording-summary-vision-story-opening` version 1, given the visits,
   the pages, and the parts.

The layers' answers have three sections, `elaborations`, `corrections`,
and `new`. The recorder puts each statement in place: one in
elaborations or corrections is about the first statement of the
knowledge it cites; one there that cites no statement is taken as new.
The statements' numbers are taken out of its evidence, which keeps the
evidence list's references, and are never in the report. The knowledge
is given in reading order: layer 1 in its order, each statement followed
by those about it, indented, then the new statements. The steps reused
on a run again are those whose requests are the same; the frame and
tile steps of the earlier vision runs are reused, as their requests are
unchanged.

Chapter 2's statements each give a `form`, first in the statement, one
of `heading-1`, `heading-2`, `heading-3`, `sentence`, and `picture`; the
schema holds the model to these, in chapter 2's requests only. The form
is read back from the stored answer, as the claims are stored without it.
The recorder numbers the headings on from the opening through every
visit's part, 2.1, 2.1.1, 2.1.1.1, a heading with no heading above it
raised a level; joins a paragraph's sentences between headings and
pictures into one paragraph; and puts in each picture from the first
captured frame or tile it cites, as `![Figure k. caption](../../file)`
followed by the caption with its references. A picture citing neither
is given as its caption, saying that no picture is shown. Tables and
graphs are not among the forms; a part of a frame or tile is to come
after, as said above.

The report, `summary-with-vision.md`: its title, "Report of recording",
the model and profile, how the layers are built, the participant's
capabilities, "1. The pages" with each page's visits as the recorder
derived them, its description, and "How the page changed", "2. The user
experience", the other windows, the evidence list, and how it was made.
The player's reading view shows each picture from its file, found from
the report's folder, no larger than its own size, named by its caption
for assistive technology; where the file cannot be read, it says so in
the picture's place. The caption's references are buttons that move the
playhead, as every paragraph's are.

- **Tests:**
  - **Unit tests:** the visit's knowledge numbered, an addition
    elaborating, correcting, or new put in place, one citing no
    statement taken as new, and the knowledge given with and without
    numbers; a statement's reference; the schema with the form first in
    chapter 2's requests only; a statement's form read back from the
    answer; a picture line read by the reading view's document.
  - **Integration test:** the vision task on a recording built by the
    tests, with a stand-in endpoint: the steps in order, layer 3 not
    sent where its groups are known, each layer's request with the
    knowledge in place, chapter 1's and chapter 2's requests, the
    report's two chapters with numbered headings and pictures whose
    files exist, the index of moment records, a run again restarting at
    the step that failed, and a trial of a layer step.
  - **System test on the target machine:** a run of the summary with
    vision on the cnib.ca recording, checking on screen that it reaches
    its end, that the report has its two chapters, and that the reading
    view shows its pictures.

#### On the target machine

2026-10-16, build 4199327, a run again of the summary with vision on the
cnib.ca recording, task 01a1421c run again: the report was completed
successfully (owner). Still to be worked on, and left for later by the
owner the same day: chapter 2's prose, which still has "giant paragraphs
of text that are unreadable" (owner).

### The executive summary and the settings in use (built)

Asked for by the owner on 2026-10-16, before the other planned work
([outstanding work](analysis-outstanding-work.md), "Planned work for the
report"): "an executive summary for the report (a new chapter 1), and
then we need to address the accessibility and AT settings in use. Changes
to them should be identified in the story, but we need appendices for the
initial settings."

Proposed, to be agreed:

- **The chapters renumbered.** Chapter 1 is the executive summary,
  chapter 2 the pages, and chapter 3 the user experience, the story. The
  recorder numbers the headings, as now, so the story's headings become
  3.1, 3.1.1, and so on.
- **The executive summary, one step,** `executive-summary`, written last,
  after the story's opening, as it summarises the whole report. It is
  given the participant's capabilities, the assistive technology and
  settings at the start, the changes to them, the pages as chapter 2
  describes them, and the story, each statement with its references. It
  is text only, and its statements cite the references they rest on, as
  every statement does, so that it stays checkable.
- **The settings at the start, in appendices written by the recorder,**
  not by a model, from the same records the player's properties panel
  shows, as at the start of the recording: Appendix A, the Windows
  accessibility settings (`system.preferences`); Appendix B, the browser's
  settings and what it sent to the page (`browser.preferences`); Appendix
  C, the assistive technology running, with its version, and NVDA's
  settings as read from its configuration, and the Magnifier's level
  (`system.assistive-technology`, `graphics.magnifier`). Each is a table
  of setting, value, and when it was read, each row with its reference;
  a setting not recorded says so, and an older recording without the
  records says that they were not recorded.
- **The changes in the story.** The recorder finds each change to a
  setting or to the assistive technology during the recording, from the
  same records, as a derived fact with its time, its value before and
  after, and its reference. Each visit's story step is given the changes
  during its visit, and the opening those before the first visit, with
  the instruction to tell each where it happened and what it did to the
  experience, as far as the evidence shows. The layered knowledge, and so
  every step already accepted before the story, is unchanged, so a run
  again reuses them.

The owner's answers, 2026-10-16:

1. **What the executive summary holds.** Not findings, ranked or mapped to
   WCAG, "not yet. Currently we are recording the basic user journey the
   accessibility and usability analysis is not yet there. We should be
   talking about the user and the kind of assistive tech they are using
   and significant accessibility settings chosen by them."
2. **Which settings the appendices list.** "Probably all of them, but
   indicate which are default settings."
3. **An appendix of the changes.** "Yes, we should probably have an
   appendix for that too."
4. **The Magnifier's position.** "No, position is important. It says what
   the user was looking at, or at least what was actually on screen, but
   it doesn't need to be pixel by pixel. We should identify periods of
   panning and record their start and end locations and maximum extent
   left, right, top, bottom."

As agreed, to be built:

- **The executive summary** says who the participant is, from the
  capabilities as given; the assistive technology in use, from what was
  observed running; the accessibility settings not at their defaults, and
  their changes; and the site and the course of the session in brief. It
  gives no findings, and does not judge the accessibility or usability of
  the site, which the report does not yet analyse. Its template is
  `recording-summary-vision-executive-summary`, version 1, its one section
  `summary`, sentences only.
- **Four appendices,** each a table written by the recorder: A, the
  Windows settings at the start; B, the browser's settings at the start,
  and the values sent to the first page visited; C, the assistive
  technology at the start: each product's rows of the properties panel,
  NVDA's settings as read from its configuration, its profiles, profile
  triggers, and custom key commands, and the Magnifier's level, the part
  of the screen it showed, and its color effect, from the first captured
  frame; and D, every change during the recording.
- **Which settings are at their defaults.** Each row says whether its
  value is the default, is not the default, or that its default is
  unknown, or that it is not a setting (the values sent to the page, a
  product running, and the Magnifier's view, which are read, not chosen):
  - Windows: Microsoft documents a default for one of the 27 settings
    recorded, the caret width, "The default and minimum value is 1"
    ([SystemParametersInfoW](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-systemparametersinfow),
    `SPI_SETCARETWIDTH`); the others' defaults are unknown, and the
    appendix says so, as Microsoft does not document them (for example,
    [UISettings.TextScaleFactor](https://learn.microsoft.com/en-us/uwp/api/windows.ui.viewmanagement.uisettings.textscalefactor)
    gives only its range, "The value ranges from 1 to 2.25").
  - The browser: Chromium's own answer, recorded with each setting
    (`isDefault`, from `PrefService`'s `IsDefaultValue`), as the panel
    shows it.
  - NVDA: a setting not written in `nvda.ini` is NVDA's default; one
    written is compared with the default in NVDA 2026.2's configuration
    specification
    ([configSpec.py](https://raw.githubusercontent.com/nvaccess/nvda/release-2026.2/source/config/configSpec.py)).
- **The changes,** each found by the recorder with its time, its value
  before and after, and its reference: a Windows setting's; a browser
  setting's, and the default zoom's; a value sent to the page again while
  the same page is shown; a product starting or exiting, and NVDA's
  settings that differ when it starts again; and the Magnifier's level,
  its color effect, and its periods of panning.
- **Periods of panning.** A period of panning is the Magnifier's view
  moving at one level, each move less than 1 second after the one before;
  a level change in less than 1 second is one change, from the level
  before to the level after. Each period is given with its start and end
  times, the part of the screen shown at its start and at its end, and
  the furthest it reached left, right, top, and bottom, in the screen's
  pixels, from the Magnifier's readings with every frame of the period.
  The 1 second is the recorder's choice, stated in the report, to be
  changed if it does not suit.
- **Where the changes go.** Each change is given to the story's part for
  the visit it happens in, or to the opening when before the first visit,
  as recorder facts with their references, with the instruction to tell
  each where it happened and what followed it as far as the evidence
  shows; and all are in Appendix D, so that none is lost if the story
  leaves one out.
- **References.** Each record read is one reference, which every row read
  from it cites: the Windows settings at the start, the browser's, the
  values first sent to the page, NVDA's start and its settings record;
  the Magnifier's rows cite the first captured frame. They are added to
  the evidence after the pages' steps, so every step before the story is
  given the same input as before and a run again reuses it.
- **Templates.** The story and opening templates go to version 2, given
  the changes; the page chapter template goes to version 2 as well, its
  instructions naming the chapters by what they are rather than by
  number, so a run again sends each page's step once more.

As built, 2026-10-16:

- `SettingsReader` (`src/Recorder.Analysis/Summary/SettingsInUse.cs`)
  reads the rows and the changes from the archive's timelines, the same
  ones the player's properties panel shows, and the records' events from
  the playback index, which keeps the settings channels; the archive's own
  list of events is empty when it is read from the database or a
  recording file, so it is not used.
- The executive summary's step, `executive-summary`, is sent after the
  story's opening. It is given the capabilities, the settings at the
  start as Appendices A to C give them, each row with whether it is the
  default, the changes, the page visits and the pages' titles, and the story's
  statements with their references.
- The default zoom is at its default when no default is set or it is
  100 percent, the zoom map's starting level.
- NVDA's settings not read, as when its configuration folder could not be
  found, are given as "not read" with the reason recorded, their defaults
  unknown.
- A Magnifier frame's reference uses the same key as the moments'
  frames, `frame:` and its time, so one frame has one reference.
- The report's "How this was made" states the 1 second of the panning
  periods and where each kind of default comes from.
- Tested at the unit level (`SettingsReaderTests`) and through the task
  with a stand-in model (`RecordingSummaryVisionTaskTests`).

### The report's structure and its paragraphs (built)

Asked for by the owner on 2026-10-16, after the executive summary and the
appendices were built: "Appendices are always the last sections of a
document, we have them before Other Windows and Evidence. All sections
should have either a numbered heading or be an appendix, That probably
means that How this was made should be the last appendix." And: "We have
giant single paragraph sections that need to be properly structured for
humans to read. So a mix of clearer, more distinct paras and probably some
numbered lists/bullets."

The layered report as built, in order: the title and an unheaded
introduction; "The participant's capabilities", unnumbered; chapters 1
to 3; Appendices A to D; then "Other windows", "Evidence", and "How this
was made", all three unnumbered and after the appendices.

Where the long paragraphs come from:

- **The model's chapters.** Each section is a list of paragraphs, each a
  list of statements, and the report keeps the model's paragraphs as it
  gives them. The templates do not say what a paragraph is, so the model
  often gives a section as one paragraph of many statements. Nothing is
  lost or joined by the recorder: the story's paragraphs already end at
  each paragraph the model gives, and at each heading and picture.
- **The recorder's own text.** The introduction is one paragraph of
  eight sentences; each visit's derived facts under a page are one line;
  and "How this was made" is a list whose items are each a long
  paragraph.

Proposed, to be agreed:

- **Every section numbered or an appendix, the appendices last.** Under
  the title, only who inferred it and when. The introduction moves into
  "How this was made". The order becomes chapters 1 to 3 (where the
  participant's capabilities go is to be decided); Appendices A to D, the
  settings; Appendix E, other windows; Appendix F, the evidence; and
  Appendix G, how this was made, last.
- **Paragraphs said in the templates.** Each template that writes prose
  (the pages, the story and its opening, and the executive summary) says
  what a paragraph is: one topic, a new paragraph for each new topic. The
  report keeps the model's paragraphs as now; the recorder does not
  split or join them, nor check their length.
- **Lists as forms.** Two forms are added to every prose template:
  `bullet`, an item of a list whose order does not matter, and
  `numbered`, a step of a sequence, such as the participant's actions in
  order. The pages' and the executive summary's templates gain the forms
  the story's already has, so each of their statements has a form too.
  Consecutive items of one kind are one list; the recorder numbers the
  steps.
- **The recorder's own text as lists.** Each visit's derived facts under a
  page become a list, one item for each fact; "How this was made" becomes
  numbered subsections (G.1 the model, G.2 the prompt templates, each a
  list item, G.3 the settings, G.4 the limits, and so on).
- **What a run again sends.** The page, story, opening, and executive
  templates change version, so their steps are sent again; every moment's
  step is reused.

The owner's answers, 2026-10-16:

1. **The participant's capabilities:** a new chapter 2, "The
   participant", so that the chapters are 1, the executive summary; 2,
   the participant; 3, the pages; and 4, the user experience.
2. **A paragraph:** one topic each, a new paragraph for each new topic,
   with no limit on its sentences.
3. **Lists:** in the story, its opening, and the executive summary only;
   the pages stay prose, their template gaining the paragraph rule alone.
4. **The stage 1 report:** unchanged. "Stage 1, if we are generating it
   still, would only be as input to stage 2, so it would be structured to
   support the model's understanding. Do we really need it though? Even
   though we build by layer, I expect a single final report to the user,
   not ones for the intermediate layers. The UI will eventually not show
   the intermediate layers anyway. The user will select "Describe user
   journey" from a menu and the analysis and final report will run. What
   we have now is for development." Recorded in
   [outstanding work](analysis-outstanding-work.md), "Planned work for
   the report".

As agreed, to be built:

- **Under the title,** who inferred the report and when, only.
- **The chapters:** 1, the executive summary; 2, the participant, the
  capabilities as given and the assistive technology observed running;
  3, the pages, headed 3.N and 3.N.1, the visits to each page derived by
  the recorder as a list; 4, the user experience, headed 4.N.
- **The appendices, last:** A to D, the settings and their changes; E,
  other windows; F, the evidence; G, how this was made, in numbered
  subsections: G.1, how the report is built, the introduction as a list;
  G.2, the model; G.3, the prompt templates, one item each; G.4, the
  settings; G.5, the moments and their pictures; G.6, the limits, the
  steps reused, and the attempts. The reading view finds the evidence
  list under "Appendix F. Evidence".
- **The forms:** `bullet` and `numbered` added to the story's and the
  opening's forms; the executive summary given forms, `sentence`,
  `bullet`, and `numbered`. Consecutive items of one form are one list;
  the recorder numbers a numbered list from 1, and the reading view shows
  each numbered item with its number.
- **The templates' versions:** the page chapter 3, the story and its
  opening 3, and the executive summary 2.

As built, 2026-10-16:

- The paragraph rule is one sentence, `ParagraphRule`, in the page
  chapter's, the story's, the opening's, and the executive summary's
  templates; the list forms are `ListForms`, in the last three only.
- The executive summary is written as the story is, in its forms, without
  headings. A list ends at a statement of another form or at the end of
  the model's paragraph.
- Under the title, the line of who inferred the report and when ends
  "How the report was made, and what its statements rest on, is in
  Appendix G."
- Appendix C's introduction is a list of what it holds; the groups of
  tiles not sent, in G.5, are a list, one item for each group.
- The reading view shows a numbered item as its number and its text, and
  reads the evidence list under "Evidence" or a level 2 heading
  "Appendix" ending ". Evidence".
- Stage 1's report is unchanged; the shared parts take their heading as
  a parameter, stage 1's being the headings as before.

### The executive summary and the pages rewritten (built)

The owner's review of the run of 9b0956a, 2026-10-16: "That is not an
executive summary, it's a collection of bullet points. I expect the
results to be interpreted and explained like a real executive summary."
The owner gave an accessibility report's executive summary as the style to
follow, its content being different: "Prose summary bullets where it makes
sense." Of a page's description, given as one paragraph of 24 statements:
"I asked for multiple clear paras ... In what way is that multiple clear
simple paras?"

The style of the owner's example: numbered subsections; an overall
judgement stated first and then explained, with its reasons and its
caveats; prose that says what the facts mean and why they matter, not the
facts alone; and bullets only where each is a point explained in two or
three sentences, never a bare fact.

Why the built design gave what it did:

- **The executive summary** was given a list of what to say (who the
  participant is, the assistive technology, the settings not at their
  defaults, the changes, the site and the session), told to give no
  findings and not to judge, and given list forms with "use a list where
  the reader is helped by one". It did as it was asked: it listed facts.
- **The pages** were given one section, `page`, for everything about a
  page, and one sentence on what a paragraph is. The report keeps the
  model's paragraphs (each statement's paragraph is its index in the
  answer, `ClaimCheck`), so the model gave all 24 statements as one
  paragraph. One sentence among many instructions does not change how
  this model writes; a structure the recorder gives does.

Proposed, to be agreed:

- **The executive summary in subsections the recorder gives,** each its
  own section of the answer, headed by the recorder: 1.1, an overview,
  the overall picture in a few sentences; 1.2, the participant and their
  set-up, what the assistive technology and the settings chosen mean for
  how they work; 1.3, the journey, what they did and how it went; 1.4,
  what the evidence shows and does not show. Each is interpretive prose,
  with bullets only where each bullet is an explained point.
- **Each page in subsections the recorder gives:** 3.N.1, what the page
  is for; 3.N.2, its layout from top to bottom; 3.N.3, its content;
  3.N.4, its controls, forms, dialogs, menus, and messages; 3.N.5, how it
  changed, as now. Each is its own section of the answer, so no
  subsection can run into the next.
- **What a run again sends:** the page chapter and executive summary
  templates change version, so those steps are sent again; every moment's
  step is reused.

The owner's answers, 2026-10-16:

1. **Judgement in the executive summary:** yes. It interprets where the
   experience worked or got in the way, as the story tells it, still
   without grading the site, ranking findings, or mapping them to WCAG.
2. **The executive summary's subsections:** the four proposed.
3. **The pages' subsections:** the five proposed.
4. **The story:** it has the same problem, so it is to be given the same
   treatment, its structure proposed next.

As built, 2026-10-16:

- The executive summary's template, version 3, answers in four sections,
  `overview`, `participant`, `journey`, and `evidence`, which the recorder
  heads 1.1 to 1.4 (`ExecutiveParts`). It asks for a judgement first,
  then its reasons and caveats; for what the facts mean and why they
  matter, not the facts alone; for prose by default; and for a bullet only
  as a point explained in two or three sentences, "never a bare fact".
- The page chapter's template, version 4, answers in five sections,
  `purpose`, `layout`, `content`, `controls`, and `changes`, which the
  recorder heads 3.N.1 to 3.N.5 (`PageChapterParts`), each told to hold
  only its own topic.
- The story's and the opening's inputs are unchanged, so a run again
  reuses them as well as the moments; it sends the pages' steps and the
  executive summary's.

### The story in subsections (proposed)

The owner, 2026-10-16, of chapter 4, the user experience: it has the same
problem as the pages, paragraphs too long to read. As built, each visit's
part chooses its own headings and writes the sentences between them as
the model groups them, and its sentences ran into long paragraphs.

Proposed, to be agreed:

- **4.1, the opening, headed by the recorder "Introduction":** one or two
  paragraphs that set the story up, without repeating the executive
  summary or chapter 2.
- **Each visit a section the recorder heads,** 4.N, "Visit N: the page's
  title, from its start to its end", in three subsections, each its own
  section of the answer: 4.N.1, arriving, how the participant came to the
  page and what was on the screen; 4.N.2, what the participant did, the
  actions in order as numbered steps, each with how the page responded;
  4.N.3, what worked and what got in the way, interpreted, with why it
  matters.
- **Pictures** stay, in any subsection, put in by the recorder as now.
- **The model's own headings are dropped** from the story, as the
  recorder now heads every part; a heading below 4.N.k would be too deep
  to help.
- **The story's and the opening's templates** change version, so their
  steps are sent again, and the executive summary's, which is given
  them.

### The recreated page at the recorded viewport and scroll

The owner's decision, from a review of what the tiles are for.

1. **The tiles serve the model; the player's view is the live
   recreation.** Opened from the player, the recreation is a live page
   rebuilt from the recording, so fixed and sticky content behaves as it
   does in a browser as it is scrolled. The tiles are for the model's
   understanding of the page from a visual perspective: its layout in
   white space, the relations shown by relative position, and its use of
   imagery and colour.
2. **The page is laid out at the recorded viewport and scroll position,
   and pictured whole beyond it.** The viewport is not enlarged to the
   page's height: viewport-relative lengths such as `vh` are relative to
   the viewport's size
   ([CSS Values and Units Level 4, viewport-relative units](https://www.w3.org/TR/css-values-4/#viewport-relative-units)),
   and a fixed box with no ancestor that contains it is placed in the
   layout viewport
   ([CSS Positioned Layout Level 3, containing blocks](https://www.w3.org/TR/css-position-3/#containing-blocks-of-positioned-boxes)),
   so a viewport as high as the page would lay out a page the
   participant never had.
3. **Fixed and sticky content is drawn where it was in the participant's
   viewport, matching the captured frame.** A fixed box does not move
   when the document is scrolled, and a sticky box is offset in reference
   to its nearest scrollport
   ([CSS Positioned Layout Level 3, sticky positioning](https://www.w3.org/TR/css-position-3/#sticky-positioning)).
   So a dialog, toast, or banner fixed to the viewport is drawn in the
   part that was on the screen: the tiles show the whole page and also
   what was in the participant's viewport at that moment (owner).
4. **A fixed header drawn part way down the page is left as it is.** It
   covers what is under it in that tile, and a model could take it for
   part of the page's structure there. A fact from the recorder naming
   the fixed and sticky elements and their boxes is added only if a
   recording shows a model misreading one. In task 01a13fd9 it did not
   arise: at moment 10 the participant was scrolled into tiles 5 and 6,
   and both were the same files as at moment 1.

### Unique images across the recording (built)

Agreed by the owner on 2026-10-15 and built the same day, build
0633c96. On the target machine the same day, a Run again of task
01a1407c on the cnib.ca recording, which made task 01a1421c, finished,
and the owner's finding was that "the new image work seems effective".
It was first noted here as failing at
`visit-0001-moment-10-page-so-far`, as 01a1407c had; the failed task in
the player's list was 01a1407c. Task 01a1421c's report was written at
21:49 that day, its step `visit-0001-moment-10-page-so-far` was
accepted at its first attempt, as each trial of that step shows ("The
layered report"), and its "How this was made" lists 16 groups of tiles
not sent, as predicted. The recording's store held 11 unique images for
its 80 tiles. How it
was built is under "As built", after the decisions. Most of a visit's model
time is in describing tiles whose pictures are the same as tiles already
described, and in real use a participant goes back to the same page
more than once. In task 01a13fd9, the 20 requests for groups of tiles
took 35.2 of the 62 minutes of model time, and the recorder's comparison
of the tile files (decision 4 above) found:

| Group | Tiles | The same pictures as an earlier moment's group |
| --- | --- | --- |
| 1 | 1 to 4 | At moments 2 and 3, as at moment 1; at moments 6 to 10, as at moment 4. New pictures at moments 4 and 5. |
| 2 | 5 to 8 | At every moment from 2 to 10, as at moment 1. |

So 16 of the 20 requests sent pictures already described, and took 27.7
minutes. The four answers of about 10 tokens were all for group 2 at
moments where its pictures were those of moment 1.

1. **Each unique image is kept once, for the whole recording** (owner).
   The images are kept in one store in the recording's analysis folder,
   `analysis\images\<key>.png`, shared by every task on the recording,
   so that Run again and later tasks do not keep the same image again.
   A recreation that changes, from a change to the recorder, gives new
   pictures and so new keys. Captured frames are not kept in the store:
   they are in the recording already, and are rarely the same as each
   other, as the pointer and focus move.
2. **An image's key is its pixels** (owner). The key is the SHA-256 of
   its width, its height, its pixels as 8-bit red, green, blue, and
   alpha, and any colour profile it carries. Images the same in all of
   these are the same picture, so nothing in them, however small or far
   from the screen, goes undescribed. Two files that differ in their
   bytes but not in their pixels have the same key; two pictures that
   differ in one pixel, or in their colour profile, do not. The first
   file with a key is the one kept.
3. **An index builds over the recording** (owner). Each task writes, in
   its own folder, an index of every tile of every moment: the visit,
   the moment and its frame's time, the tile's number, its place and
   size (top, height, width, and pixel size), and its image's key. The
   evidence list's entry for a tile gives the file in the store.
4. **The model describes groups of tiles, as now, and a group is known
   by its tiles' keys** (owner). A group is the same as one already
   described when it has the same number of tiles, each at the same
   place and size with the same key. A group is matched against every
   moment before it in the recording, in any visit, not only the moment
   just before: at moment 6 above, group 1 is the same as at moment 4,
   after being different at moment 5, and moments 7 to 10 use moment 4's
   answer too. Describing each tile alone would match more, but would
   repeat the instructions and facts in every request and lose the
   regions that run across a tile's edge.
5. **A group the same as one already described is not sent again; its
   description is the earlier answer, unchanged.** The answer is not
   written again, edited, or cited anew: it stays the model's answer at
   the moment whose request was sent, citing that moment's tiles. The
   recorder adds a derived fact for the later moment: that its tiles are
   the same pictures as those of that moment, citing both.
6. **What depends on the moment comes from the recorder.** The tiles'
   instructions ask whether each message was in the part on the screen,
   which is true of the moment the answer was written for. The combined
   step for the later moment is told the visit and time at which the
   group's description was written, and is given the part on the screen
   at its own moment from the recording, as it is now. The tiles'
   template is unchanged, so its steps already accepted are still reused
   on Run again.
7. **Nothing else is skipped.** The captured frame, the combined step,
   and the page so far run at every moment, as the frame shows the
   screen at that moment, and what a moment brings that is new cannot be
   known without them.
8. **Content that has only moved is described again.** Where a banner
   closes at the top of the page and what is below moves up, the tiles
   below are different pictures, though their content is the same.
   Finding such a move would not be an exact match, so it is not done.
9. **The report says which groups were not sent.** In "How this was
   made", each group not sent is listed with the visit and moment whose
   answer it uses, and the attempts table has no row for it, as no
   request was made.

- **Templates:** the combined template's instructions say how to read a
  description written at an earlier moment, so its version goes from 4
  to 5, and each moment's combined step runs again once. The frame and
  tile templates are unchanged.
- **Estimated effect** on the cnib.ca recording, which has one visit: 4
  tile requests instead of 20, and about 28 fewer minutes of model time,
  an estimate from the times in task 01a13fd9. A recording that goes
  back to a page saves more.
- **Not settled:** removing images from the store that no task's index
  refers to any longer is left for later.
- **Tests:**
  - **Unit tests:** the key the same for two files of the same pixels
    written with different compression, and different for one pixel
    changed and for a different colour profile; the store keeping the
    first file with a key; the index's entries; a group the same as at
    the moment before, as at a moment before that with a different
    moment between, and as at a moment of an earlier visit; one tile of
    the group different; a group the same pictures at another place or
    size; the first moment of the recording; a moment whose page was not
    recreated; a page whose content moved down by a few pixels, matched
    for the tiles above the move only; the derived fact citing both
    moments' tiles; the combined request given the earlier visit and
    time and its own part on the screen; the report's list of groups not
    sent.
  - **Integration test:** the vision task on a recording built by the
    tests that visits the same unchanging page twice, with a stand-in
    endpoint that counts requests: tile requests at the first moment of
    the first visit only, one image in the store for each tile, and the
    report listing the rest as not sent. Run again on it keeps no new
    image.
  - **System test on the target machine:** Run again on the cnib.ca
    recording, checking on screen that the status line shows no tile
    step for group 2 after moment 1, and that "How this was made" in the
    report lists 16 groups not sent. The report is written only if the
    run reaches its end; task 01a1407c failed at moment 10's page so far
    ("The page so far at moment 10" above), so until that is reworked
    the store and the index are checked in the recording's folder
    instead.

As built:

- **The key** (`PngKey` in `ImageStore.cs`) reads the PNG itself, with
  no other library: its chunks, its compressed data inflated, and each
  row's filter undone, as the PNG specification gives them
  ([PNG, third edition](https://www.w3.org/TR/png-3/)). Colour types 0,
  2, 3, 4, and 6 are read, at every bit depth the specification allows
  for each, with the palette and the transparency chunk. A picture of 16
  bits a sample is keyed at 16 bits, so that no difference in the low
  bits is lost, and so never has the key of an 8-bit picture. The colour
  chunks counted are iCCP, by the profile decompressed, not its name or
  its compression, and sRGB, gAMA, cHRM, and cICP as written. A PNG the
  recorder cannot read, such as an interlaced one, is keyed by its bytes,
  with a different prefix, so that it can only match the same file; the
  index says which. The key was checked against PNGs written by another
  library, Pillow 12.3.0, in each colour type, 1-bit gray and 4-bit
  palette with transparency among them: each gave the key of the same
  pixels written as 8-bit red, green, blue, and alpha.
- **The store** is written to a temporary file and then moved into
  place, so that a stopped run never leaves part of a picture under a
  key. The tiles' bytes sent to the model are the stored file's, which
  for the same pixels may be another run's file.
- **The derived fact** that a tile is the same picture as an earlier
  moment's is written in that tile's own entry in the evidence list,
  citing the earlier tile, rather than as an entry of its own, so that
  the evidence list's numbering is as it was. A new entry would number
  every later entry one higher, so that every later step's request would
  change and none could be reused on Run again. The combined request
  says the same, naming both moments' tiles.
- **The index** is `analysis\<task>\images.json`, written again whole
  after each moment, so that it is there for a run that stops; a trial
  writes its own, with its own store, beside its pictures.
- **A trial** of a group's step that is not sent fails, saying which
  group's answer it uses, as there is no request of the task's to try.
- **The page so far** still compares tiles file by file (decision 4
  above). With the store, two tiles with the same key are the same file,
  so the comparison is that of the stored pictures. The page so far is to
  be changed, more radically, after this work (owner, 2026-10-15), so it
  was not changed here.

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
