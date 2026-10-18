# Next build fix list: summary with vision

Based on the target report of build 88464e7 (in commit 8ea8693) (recording 20261013-173155-6672eb45f4314526936fac89c814cffd, CNIB home page), reviewed 2026-10-18.

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
8. **Single-record scrolls.** Write "jumped from 0 to 2086 CSS pixels from the top at 00:00:40.661" rather than "from 00:00:40.661 to 00:00:40.661, 1 position recorded".
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
