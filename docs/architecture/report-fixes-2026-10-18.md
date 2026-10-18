# Next build fix list: summary with vision

Based on the target report of build 88464e7 (in commit 8ea8693) (recording 20261013-173155-6672eb45f4314526936fac89c814cffd, CNIB home page), reviewed 2026-10-18.

## Progress

The first batch, built 2026-10-18 and not yet checked on the target
machine, makes these changes, each with a test:

- Item 1.5: a product running at the start counts as running from 0, as
  the summary's facts count it, so Appendix C and chapter 2 agree. Whether
  its module was in the browser is read at the first page visit, as the
  browser starts after the recording does.
- Item 1.6: "High Contrast Black, chosen but not in use, as the contrast
  theme is off" while the contrast theme is off.
- Item 1.7: the buttons fact gives their number, "Buttons, 3: ...". The
  garbled sentence in 3.1.4 was the model's; this makes the fact plainer.
- Item 1.8: a scroll of one record reads "scrolled from 0 to 2086 CSS
  pixels from the top at 00:00:40.661, 1 position recorded". "Scrolled",
  not "jumped", as a scroll from a position not recorded may not have
  been a jump.
- Item 1.9: inferred announcements are given to the model and listed with
  commas; the player keeps NVDA's two-space separator.
- Item 1.10: a registry value that does not exist is "not set"; page
  colors are named from `PageColors` in
  `chrome/browser/accessibility/page_colors_controller.h` of Chromium
  156.0.8065.0 ("no preference, as the Windows theme (0)"); NVDA's
  modifier keys are named ("7: Caps Lock, numpad Insert and extended
  Insert", default "6: numpad Insert and extended Insert").
- Item 1.11, in part: Appendix E gives times to the millisecond, so a
  window in front for under a second no longer reads "00:00:00 to
  00:00:00". Why the recorder coming to the front at about 00:00:54.9 has
  no front-window record is not yet known.
- Item 1.12: Appendix F says only cited evidence is listed.
- Item 1.13: alternative text names the picture, "Figure 1: The captured
  frame at 00:00:06.944, described in the caption below", and leaves the
  description to the caption.
- Items 3.1 and 3.2: G.2 gives the model once and the generation settings
  of the accepted answers once where every step shares them, otherwise
  each set with its steps; the number of images and the answer's sections
  are left out, as they belong to the request. G.1 no longer says "the
  pages" are both the model's and the recorder's.

Because the facts' wording changed, no step of an earlier run is reused.

The second batch, the model's side, also built 2026-10-18 and not yet
checked on the target machine:

- Item 1.2: the story is given the visit in time order, each input with
  what the recorder found after it and before the next: focus moves,
  NVDA's commands and inferred announcements, in-page jumps and the
  page's changes. Each key is an input at its time, a run of characters
  typed one input, so the Tab presses are no longer known only from a
  count. The story's numbered steps are asked to come from it, each an
  input and what followed, citing both, and to say where nothing
  followed. What follows an input is not said to be caused by it.
- Item 1.3: the templates say never "announced, said, read, or spoke",
  nor that speech stopped; what NVDA had to announce or would do,
  inferred. The recorder's check refuses such a statement (below).
- Item 1.4: the page's facts list every link and image by name, not their
  numbers alone, and the layer, page and story templates set what is seen
  beside what the page is built as, as the owner decided.
- Part 2, items 2.1 to 2.10: the rules of order, causes, counts, the
  recording's ending, sources, settings at their defaults, what the
  rebuilt page lacks, the layout section as the page alone, the
  experience interpreted, and what an engineer would want noticed.
- Item 1.1: the templates ask for references in a statement's evidence
  only, never its text. Where the model still writes in the text a
  reference the statement also cites, such as "[E2]", "(E29)" or "[E31]
  [E30]", the report takes it out of the text, and G.1 gives how many
  statements and references; a run of references any of which the
  statement does not cite is left as written. The owner accepted this on
  2026-10-18. A statement writing in its text a reference it does not
  cite fails the answer, as does one saying NVDA announced, said, or
  spoke, or that its speech stopped (item 1.3); the answer is then
  sampled at temperature 0.7, seeds 1 to 3, the first that passes kept,
  and the step fails where none passes. The owner, 2026-10-18: "yes,
  build as proposed". See analysis-runner.md, "Answers whose wording
  breaks the report's rules".

Templates: moments version 4, layers version 2, page chapter and story
version 6, executive summary version 4.

Item 1.14: the owner kept the model's choice of pictures (2026-10-18).

Part 4, the browser-only report scope, built 2026-10-18 at the owner's
request ("add it now please"), not yet checked on the target machine:

- **The setting:** Analysis menu, "Limit the report to the browser while
  it is in front", a checkable item, on by default, saved at once in the
  analysis settings file as `"reportBrowserOnly"`. A worker already
  running keeps the setting it started with; each report's G.1 says
  which was used.
- **When it applies:** at a moment whose window last recorded in front,
  other than NVDA's, is the browser's, with its bounds recorded and not
  minimized (`ReportScope.At`).
- **What is kept:** the browser's window at its bounds as recorded when
  it came to the front, and every NVDA window recorded in front earlier
  in the recording, at its bounds then, as whether it was closed is not
  recorded. Under full screen magnification, only the part in the
  magnified view, as the player draws it.
- **The cut:** the frame is cut to the bounding rectangle of those
  windows, and what lies in it outside every one of them is painted mid
  grey (`FrameCut`). The cut is a new PNG with the frame's colour
  chunks, kept in the store of unique images and keyed as its own; the
  frame is unchanged. The model gets the cut, and the report's figure
  shows it. A frame whose pixels cannot be read is sent whole, and G.1
  lists it.
- **What the models are told:** the frame's evidence says it was cut,
  with the pixels and the file it was cut from; with the setting on,
  the moment, layer 2, page chapter, story, opening, and executive
  summary inputs end with a note that other windows, the taskbar, and
  the desktop are left out and not to be described.
- **The report:** G.1 says whether the setting was on, how frames were
  cut, and the moments sent whole and why; Appendix E keeps every window
  that came to the front, saying they are left out of the pictures and
  prose while the browser was in front.
- **The recorder's own windows:** until 2026-10-18 the front window hook
  was set with WINEVENT_SKIPOWNPROCESS (`ForegroundWindowCollector`,
  since its first commit, 5659f53), so after the record at the start,
  the recorder's own windows were not recorded in front. This explains
  item 1.11, the recorder coming to the front at about 00:00:54.9 with
  no record. It was never the owner's decision, and contradicted the
  decision of 2026-10-14 that the recorder is a genuine window. The
  owner, 2026-10-18: "The recorder is a window like any other: ignored
  if the browser window has focus, otherwise reported on, I expect to
  test the recorder with people, I need it recorded". The hook now
  records the recorder's process like any other; its windows' titles are
  read with InternalGetWindowText, which sends no message to the
  recorder's own interface thread. With the setting on, a moment with the
  recorder in front is sent whole. The recording does not say which build
  made it, so Appendix E and G.1 say that one made before 2026-10-18
  leaves them out, and that there a moment with the recorder in front is
  taken with the window before it. The cnib.ca recording of 2026-10-13 is
  one such.
- **Limits:** a window's bounds are those recorded when it came to the
  front; a window moved later is cut where it was.

## Confirmed by the 88464e7 report

- Story, opening and executive summary are populated on the first attempt: 2,526, 463 and 1,452 output tokens, against 29, 12 and 42 before.
- No K-number working notes in chapter 3.
- No browser recreation fault occurred, so the one-retry recovery remains untested on target.

## 1. Recorder-side fixes

1. **Doubled citations.** Strip evidence references the model writes inside statement text (`[E2]`, `(E29)`) before the recorder appends the cited references. Apply the same rule to figure alt text and captions. Alternatively, reject statements with inline references and use the existing first-valid recovery.
2. **Actions paired with responses.** In "What the participant did", attach to each numbered action the responses that follow it before the next action: focus moves, inferred NVDA announcements, scrolls, popups and live-region changes. Example: H at 00:00:28.501 with [E18]; each Page Down with its announcement and scroll ([E19] with [E24], and so on).
3. **Inferred NVDA wording enforced.** Statements about NVDA output must keep "had to announce (inferred)" or equivalent. "Stops speech, inferred" must not become "which stopped NVDA speech". Reject statements that say NVDA announced, said or spoke something unless the cited evidence is a recorded speech source.
4. **Visual styling and semantics.** Vision describes what is visible; recorder facts keep the recorded role and name. Where they conflict, the page chapter and story describe the visual first and then the semantics it is built on, in one statement: "a red Donate Now button, built as a link"; "styled like buttons, built as level 3 headings with no recorded control". A visible label that differs from the accessible name is reported alongside it: "+ show preferences", accessible name "Show Display Preferences". Supply the recorded role and name for each vision-described element so the model can write the pairing rather than choose one.
5. **NVDA presence contradiction.** Chapter 2 and [E1] say NVDA 2026.2 was running at 00:00:00.000; Appendix C says "not running" and "its module not loaded" at the same time [E142], then reads NVDA's settings "when it was seen running" at 00:00:00.341. Use one presence state per time across chapter 2, Appendix C and the facts.
6. **Contrast theme labelling.** Label the row "Contrast theme name (selected, not in use)" when the contrast theme is off, so the name is not read as an active theme.
7. **Control list wording.** Write the chapter 3 control fact as "no form fields; 3 buttons: Reset, Show Display Preferences and Toggle navigation; 55 links; 14 images".
8. **Single-record scrolls.** Write "scrolled from 0 to 2086 CSS pixels from the top at 00:00:40.661" rather than "from 00:00:40.661 to 00:00:40.661, 1 position recorded".
9. **Announcement separators.** Render the double-space separators in inferred announcements as commas: "main landmark, Bright futures start here!, heading, level 2".
10. **Raw values.** "Color filter type: not read: The value FilterType does not exist" becomes "not set"; "Page colors: value 0" gets its meaning; NVDAModifierKeys 7 is decoded as "Caps Lock added to both Insert keys" (default 6, both Insert keys).
11. **Appendix E.** Fix the zero-length "00:00:00 to 00:00:00" range and include the recorder coming to the front at about 00:00:54.9 shown by [E117] and [E128].
12. **Appendix F.** State that only cited evidence is listed, so numbering gaps are expected.
13. **Figure alt text.** Alt text should not repeat the caption word for word; give a short alt and keep detail in the caption, without evidence references in either.
14. **Figures per moment.** Only one figure appears for ten moments. Confirm the intended rule for which moment frames and tiles the story pictures.

## 2. Prompt fixes

1. **Defaults are not choices.** A setting at its default (Chromium's answer, or not written in nvda.ini) shows no preference or need. Only non-default values may be described as configured, and even then not as evidence of the participant's capabilities when none were given.
2. **Chronology.** Statements must keep recorded order. Changes before the first participant input belong to arriving or page load, not to disruption of later actions. In this recording the 565-element change at 00:00:06.927 [E31] came a quarter of a second after the page settled [E30] and 18 seconds before the first Tab.
3. **No unsupported causes.** Do not infer intent or recovery from inputs ("an attempt to recover") without evidence.
4. **Ending the recording.** Inputs that bring the recorder to the front at the end are the session ending, not an obstruction of the journey.
5. **Count accurately.** One H press is one heading command, not "heading commands".
6. **Recreation details stay out of participant prose.** Missing images and refused requests in the recreation belong to the evidence limits in 1.4 and Appendix G, not to Arriving or What worked and what got in the way.
7. **Layout section.** "Layout from top to bottom" describes the page's regions in order; the screen and window arrangement does not belong there.
8. **Interpret, do not restate.** "What worked and what got in the way" explains what helped or hindered the journey, using facts as support, without repeating the scroll list or window layout.
9. **Source attribution.** Layout-change facts come from recorded layout data, not "the screen capture reveals".
10. **Observations the report should make.** Examples from this recording: the only level 1 heading is empty [E4]; Tab passed the skip link without it being activated; the click at (507, 77) has no recorded outcome; the visible "+ show preferences" label differs from the accessible name.

## 3. Appendix G and limits

1. **Settings per step type.** G.2 shows one presence penalty of 1.5 for the whole report, but 88464e7 applies it only to frame, tile and layer-2/3 moment descriptions. Show generation settings per step type, and for reused steps the settings of the task that wrote them.
2. **G.1 contradiction.** G.1 says the pages of chapter 3 are model inference and also that "the pages" are recorder-derived. Separate the recorder-derived visit list from the model-written page sections.
3. **Long output check.** visit-0001-moment-05-layer-3 produced 4,975 output tokens in 213 s, about six times its neighbours, and was accepted. Inspect it for repetition.
4. **Context budget.** This 55-second single visit already sends 17,261 prompt tokens to the story and 12,062 to moment 10 layer 2, against a 32,768-token context. Measure growth before the multipage run; overflow should still fail explicitly, not cut evidence.

## 4. New feature: browser-only report scope

**Setting:** "Limit the report to the browser while it is in front". On by default, saved with the recorder's analysis settings, decided per moment.

**In scope at a moment:**

- **Browser in front:** the browser window plus NVDA's own windows, such as its dialogs and Speech Viewer.
- **Anything else in front:** everything on screen, as now.

**How it works:**

- **Frames:** crop each captured frame to the in-scope windows before the frame and layer-2 steps. NVDA windows that are showing stay in the picture even outside the browser's rectangle.
- **Magnifier:** under full-screen magnification, crop to the part of the in-scope windows visible in the magnified view.
- **Tiles:** full-page tiles are unchanged, since they already show only the page.
- **Image reuse:** cropped frames are stored and hashed as their own pictures, so reuse stays exact to the pixel.
- **Prose:** the story, page chapter and executive summary do not describe out-of-scope windows or the desktop background.
- **Appendices:** A to D are unchanged. Appendix E keeps the full list of windows that came to the front and states they were left out of the prose.
- **Disclosure:** Appendix G states whether the setting was on and lists the moments described in full because the browser was not in front.

**Expected effect on this recording:** the browser was in front from the first moment until the recorder came forward at about 00:00:54.9, so most desktop and window prose goes, apart from what the NVDA Speech Viewer shows. Appendix G would list the last two moments as described in full.

## 5. Checks for the next target report

Each check covers one behaviour and is confirmed by reading the report.

1. No sentence in chapter 4 carries the same reference both inside and after its text.
2. Each numbered action in 4.2.2 lists its following responses with their references.
3. No sentence says NVDA announced, said or spoke something; inferred wording is kept.
4. Donate Now and Read more are described as styled like buttons, built as links; Volunteer Gardener and Breeder Carer as styled like buttons, built as headings.
5. Chapter 2 and Appendix C agree on whether NVDA was running at the start.
6. Section 1.2 does not describe High Contrast Black as in use, and does not describe default settings as preferences.
7. The 565-element change is described as before the first input.
8. Chapters 3 and 4 do not describe the recorder, desktop or other windows while the browser was in front; Appendix G discloses the setting and the last two moments.
9. Appendix G shows generation settings per step type.
