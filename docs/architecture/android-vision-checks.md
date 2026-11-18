# Android Checks With a Vision Model: Plan, and Images of Text as Built

## Status

Recorded 2026-11-15 at the owner's request, "so it doesn't get lost".
This is a preliminary plan: the checks below are options, not decisions,
until the owner chooses them, apart from the decisions recorded under
"Owner's decisions" and the rules for images of text chosen on
2026-11-15. The switch that turns AI analysis off (decisions 5
and 6, step 1 of the order in decision 7) was built on 2026-11-15, and
replaced the same day by each project's models, chosen in its settings
(decision 8; `projects.md`, "The project's models"). The first check
with a model, images of text, was built on 2026-11-16 (see "Images of
text, as built"), and those marked "built" in the tables since; the
others are not built. Each is to be measured on
the recordings to hand before its rules are set, as the earlier checks
were (see `android-checks.md` and `android-checks-candidates.md`).

The owner's standing rule was first stated as: "As far as possible
testing should be AI-free both for cost and repeatability, I expect to
use AI to help with summary reports and vision-centric tests only". On
2026-11-15 the owner clarified it: "The rule is really AI only when
non-AI cannot do the test. The biggest group is vision AI." Almost
everything here is vision-centric. The few that ask the model for
text-based checks are marked "text only", and are wanted as well; they
read the screen's OCR text as well as what a screen reader says, and
the picture where the screen is mostly images (requirement 5).

The guidance in "Keeping it repeatable" was adopted by the owner on
2026-11-15: "I like your "Keeping it repeatable" comments and we should
follow that guidance."

## Owner's requirements

Set by the owner on 2026-11-15, in answer to the open question of local
or cloud models:

> "Sending confidential information to the cloud requires client
> approval, testing of public interfaces does not. That concern is
> outside of the scope of this work as it varies from client to client.
> What I do need is a switch to turn off AI analysis, and a note in any
> report that uses it to say that AI was used in the testing, and
> whether a cloud or local model was used."

1. **Client approval is out of scope.** Whether a client's frames may go
   to a cloud model varies from client to client and is the tester's
   decision. The recorder does not ask for or record it.
2. **A switch turns off AI analysis.** With it off, no request is sent to
   any model, local or cloud, and the checks that need one are not run.
   The report lists them as not run, so a missing finding is not read as
   a pass. Where it sits and what it covers are decisions 5 and 6
   below.
3. **A report that uses AI says so.** Any report with a finding or text
   from a model says, once, that AI was used in the testing, and whether
   the model was a cloud or a local one. A report that used none says
   nothing about AI. This applies to the summaries as well, whose
   reports say so since 2026-11-15 (`android-report.md`, "Cloud or
   local").

4. **Testing is passive.** "No, you can't do that, you can't modify the
   phone settings or UI, testing is passive from user navigation and
   recording." (owner, 2026-11-15). A check works from what was recorded
   while the tester used the phone; nothing changes the phone's settings
   or drives its interface, for example to make text larger and see
   which text grows.

5. **Checks that read text do not trust the screen reader's text
   alone.** Proposed with the screen title check measured against what
   a screen reader says on the screen, the owner answered on
   2026-11-17: "Actually not really. You are assuming the page is
   working for screen-readers. It needs to also include OCR as a
   minimum, and probably vision for image heavy screens". The same day
   the owner applied it to the other checks that read text: "Apply the
   OCR and vision sources to the other text checks too". What a screen
   reader says is what these checks test, so it cannot be the only
   account of what the screen shows: drawn text, images and views with
   no name would leave it thin or wrong. Each such check is given:
   - what a screen reader says on the screen, as what a screen reader
     user is given;
   - the screen's text as OCR read it, at least, as what a sighted user
     can read, whatever the tree says;
   - for a screen mostly of images, the picture, asked of the model for
     vision-based checks.

   The checks are: a title that does not describe its screen (2.4.2),
   a heading that does not describe its section (2.4.6), instructions
   that rely on shape, colour or place (1.3.3), error messages that do
   not help (3.3.1, 3.3.3), and link text that does not say where it
   goes (2.4.4). Where the rules choose items by their text, such as
   the words of instructions that rely on shape or colour, or an error
   message, they look in the OCR text too, so text that is drawn and
   not in the tree is not missed. Each source is marked in the
   question, so the model's reason can say which it relied on. Which
   screens are mostly images is chosen by a rule, without a model, set
   after measuring the recordings as for every check. A large
   difference between what a screen reader says and what OCR read is
   not these checks' finding: it is the separate issue "Text shown on
   screen that is not in the tree".

## Owner's decisions

Given by the owner on 2026-11-15, in answer to the open questions:

1. **Settling the existing warnings comes first:** logotypes in images
   of text, the target-size exceptions, and whether a control's boundary
   is needed to identify it. The owner accepted this suggestion.
2. **Model findings are shown like the others.** Each has an impact of
   High, Medium or Low, as other findings do, and is also marked as
   inferred.
3. **"Cannot tell" is a warning that needs manual review.** It is never
   reported as a pass or a failure, and never left out.
4. **The text-only checks are wanted.** "Yes I do want them. The rule is
   really AI only when non-AI cannot do the test. The biggest group is
   vision AI." A model is used only for a test that cannot be done
   without one, whether it reads pictures or text.
5. **Where the switch sits.** One setting in the analysis settings turns
   AI analysis on or off, and each report has a checkbox that starts
   from that setting, so a single report can be run without AI.
   Replaced by decision 8.
6. **What the switch covers.** It also turns off the summaries, so that
   off means no model at all.
7. **The order of building** (owner, 2026-11-15: "Yes, I accept the
   order"), proposed from the measurements under "Settling existing
   warnings":
   1. the switch that turns AI analysis off, as it must be there before
      any check that needs a model;
   2. images of text: is the text a logotype, or essential? The one test
      of the three that needs a picture to be understood;
   3. the target-size exceptions, without a model, as each can be judged
      from the tree, the pictures' text and the positions;
   4. boundaries, with a model, last, as it would mostly confirm the
      information items already there.
   On 2026-11-16, measured with Claude Haiku 5.5, the model confirmed
   95% of the boundary info findings on both of two runs and confirmed
   wrong evidence as readily, so the owner chose rules without a model
   (`android-checks.md`, "Step 18: boundaries without a model").
8. **The project chooses the models** (owner, 2026-11-15): "Change it so
   that projects have settings and we can choose models (or no model)
   for report analysis, for text-based checks, for vision-based checks.
   Remove the existing checkboxes and model selection from analysis and
   checks. Once I have those in place, you will use the settings I
   choose." This replaces decisions 5 and 6: no model for a use means no
   model at all for it, cloud or local, and choosing no model for all
   three is AI analysis off. See `projects.md`, "The project's models".

## What a model adds

The checks built so far test whether something is there, can be
measured, or is consistent: a name exists, a contrast ratio is met, a
title is not shared. What they cannot do is judge meaning: whether a
name, a title, a colour or a graphic does its job. A vision model can
also look at what the accessibility tree does not describe at all.

Checks already built without a model are left out. Several of these
were already noted as limits in `android-checks.md`.

Each row is one issue, so that its remediation can be specific.

## Candidate checks

### Names and labels

| Check | WCAG | What the model is asked | Chosen without a model |
| --- | --- | --- | --- |
| A name that does not describe the control | 4.1.2, 2.4.6 | Does this name say what the control does? | Controls with a name, such as "Button", "icon_3" or "image", or any name, by sample |
| A name that contradicts its icon | 1.1.1 | Does the icon in this outline fit the control's name? | Controls with an image and a description or text of their own; built, see "Icons and descriptions, as built" |
| A description that does not match its image | 1.1.1 | Does the description fit what the outlined image shows? | Images that are not controls, with a description or text of their own; built, see "Icons and descriptions, as built" |
| An informative image with no description | 1.1.1 | Is this image informative or decorative? | Image items with no name |
| A decorative image that is announced | 1.1.1 | Is this image informative or decorative? | Images that are not controls whose description a screen reader says with other text, or alone; built, see "Decorative images, as built" |
| A label that is only placeholder text | 3.3.2 | Is there a visible label beside this field once text is typed? | Fields whose hint was the only text, before and after typing |

### Structure

| Check | WCAG | What the model is asked | Chosen without a model |
| --- | --- | --- | --- |
| Text that looks like a heading but is not one | 1.3.1 | Is this text shown as a heading? | Text items larger or bolder than the screen's body text, not marked as headings |
| A heading that does not look like one | 1.3.1 | Is this text shown as a heading? | Items marked as headings |
| A title that does not describe its screen | 2.4.2 | Does this title describe the screen shown? | Each titled screen once, unless its title is exactly its own heading, OCR line or spoken item, with what a screen reader says, the screen's OCR text, and the picture for a screen at least half images (requirement 5); built, see "Screen titles, as built" |
| A heading that does not describe its section | 2.4.6 | Does this heading describe what follows it? | Marked headings, and text shown as a heading that the model judged one, that are not the screen's title, with their sections' items and OCR text, and the picture for a section mostly of images; built, see "Headings and sections, as built" |
| Content in an order that does not make sense | 1.3.2 | Does the content read in a sensible order? | Screens with several blocks of text, in the order a screen reader moves |

### What the tree does not show

| Check | WCAG | What the model is asked | Chosen without a model |
| --- | --- | --- | --- |
| Text shown on screen that is not in the tree | 1.1.1, 4.1.2 | Is this text drawn where a screen reader cannot reach it, covered, or not text? | Lines OCR read in no item's text, leaving out pictures out of step, TalkBack's caption, lines in images, short lines, lines held elsewhere or in another snapshot of the visit, and a small item's own text misread; built, see "Text not in the tree, as built" |
| A graphic with no item in the tree | 1.1.1 | Does this area hold a graphic that informs, such as a chart or progress bar? | Areas of the frame with detail and no item, as `android-checks.md` step 5 left for the vision model (owner, 2026-11-12) |
| The contrast of such a graphic | 1.4.11 | Which parts of the graphic are needed to understand it? | The graphics above, then measured as non-text contrast is now |
| Text that is cut off or overlaps | 1.4.4, 1.4.10 | Is this text cut off or overlapped? | Text items whose text ends in an ellipsis, or whose bounds overlap or reach their parent's edge, most useful at a large font size |
| Video in the app with no captions | 1.2.2 | Is video playing here, and are captions shown? | Areas of the frame changing at a video's rate |

### Colour and sensory characteristics

| Check | WCAG | What the model is asked | Chosen without a model |
| --- | --- | --- | --- |
| A selected state shown only by colour | 1.4.1 | Apart from colour, what shows this tab or item is selected? | Rows of controls of one class and size where one is drawn differently, as asked in `android-checks.md` step 5 |
| An error shown only by colour | 1.4.1 | Apart from colour, what shows this field is in error? | Fields whose colours change after input, with no error text |
| Links shown only by colour | 1.4.1 | Apart from colour, what shows this text is a link? | Clickable text within other text |
| A chart that relies on colour | 1.4.1 | Can the chart's series be told apart without colour? | The graphics found above that are charts |
| Instructions that rely on shape, colour or place | 1.3.3 | Does this text refer to something only by its shape, colour or place? | Text in the tree or the OCR text with words such as "green", "round", "on the right"; text only, with the picture for a screen mostly of images (requirement 5) |

## Settling existing warnings

Some findings are warnings now because an exception cannot be judged
without understanding the picture. A model could settle them:

- **Images of text (1.4.5, 1.4.9):** is it a logotype, or otherwise
  essential?
- **Target size (2.5.5, 2.5.8):** does the exception for an equivalent
  target, or for a target in a line of text, apply?
- **Boundaries (1.4.11):** is this control's boundary needed to identify
  it? These are listed as info now. Measured on 2026-11-16, the owner
  chose no model for it (see `android-checks.md`, "Step 18: boundaries
  without a model").

This mainly removes false positives rather than adding checks, and may be
the best value for the cost. The owner chose it as the first to build
(decision 1).

### Measured on 2026-11-15

From the client's Android report of 2026-11-12, run with OCR and the
pictures, and from the accessibility trees of the 20 phone recordings of
2026-11-09 to 2026-11-14 with phone screens (6 of the client's app; the
others of Settings, the Launcher, Chrome, Gmail and TalkBack), checked
at AA and at AAA:

- **Images of text:** 4 warnings, the four pictures of the client's
  library cards, each with its title drawn in it. Whether the text is a
  logotype or essential needs the picture understood, so this is the one
  of the three that needs a model.
- **Boundaries:** 21 information items, each a control identified by its
  visible text or icon, such as the four tabs and the mood buttons. A
  model would mostly confirm them. The 5 errors where a state is shown
  only by its fill are measured, and need no model.
- **Target size, 24 dp (2.5.8, AA):** no errors, and 2 not run, as the
  item is against the edge of a list that scrolls.
- **Target size, 44 dp (2.5.5, AAA):** 7 errors and 5 not run. Three of
  the errors are on one screen of the client's app ("Forget Password?",
  "Terms of Service", "Privacy Policy"), and four are text of the
  Launcher's date and weather inside a larger item that can be tapped.

Each target-size question can be answered without a model: whether
other text shares the target's line, from the pictures' text and the
positions; whether a large enough control with the same name is on the
screen, from the tree; whether a larger item that can be tapped is the
target; and, for an item against a scrolling list's edge, whether a
later snapshot of the same visit shows it whole. By the owner's rule,
"AI only when non-AI cannot do the test", they are built without one
(decision 7). Built on 2026-11-16 from the tree, not the pictures' text,
as the tree's positions are exact and repeatable; a larger item that
can be tapped is a warning, not a pass, as WCAG counts overlapping
targets as one only when they do the same thing (android-checks.md,
step 17).

### Images of text, measured on 2026-11-15

A probe, deleted afterwards, chose the candidates as the images-of-text
rule would and asked one model about each, on the 20 phone recordings
of 2026-11-09 to 2026-11-14. It found 44 candidates, which fall into
groups by what the tree and the pictures show:

- **17, TalkBack's on-screen caption** over a picture. The caption's
  window is not in the recorded list of windows, so the rule that leaves
  out text drawn above a picture's window missed it. A rule without a
  model can leave these out.
- **3, a picture whose tree was out of step** with the frame by then. A
  rule without a model can leave these out, as other checks already do
  for out-of-step trees.
- **2, logotypes**, a search engine's and a store's.
- **5, the preview in Settings' display size and text**, text drawn as an
  example, arguably essential.
- **1, an illustration** with other content in a TalkBack setting, as
  first read. Measured later the same day (below), the picture shows a
  dialog over the setting's page that the tree does not hold, so this is
  a picture out of step, and its text is the dialog's.
- **16, the client's library cards**, each a picture whose description
  holds its text, from the app's framework. The pictures alone cannot
  tell whether the text is drawn in the picture or set over it, so a
  model cannot settle these either; each would be "cannot tell", for a
  manual check.

The model asked was the local "This PC, with images" profile
(Qwen3.6-35B-A3B, 4-bit, temperature 0, seed 1) on the development PC.
It gave the same answer, reason and output for all 44 in two runs, so
it was repeatable, but its answers were poor: only "could be text" (35)
and "logotype" (9), never "essential", "picture" or "cannot tell", with
reasons that contradicted the answers. A request took about 20 seconds
after the model was loaded, about 15 minutes for the 44; a request's
body was about 476 KB at the median, and about 1,070 tokens in and 57
out. The owner had already found this model poor ("I know the local
model with 4 bit quant is shit, we proved it earlier"), and the model
to use is now chosen in the project's settings (decision 8). Estimated
for Claude, not sent: about US$0.30 for the 44 with Opus 5.5, US$0.15
with Sonnet 5.5 and under US$0.01 with Haiku 5.5.

The rule's options, from these, are for the owner to choose: leave out
TalkBack's caption and the out-of-step pictures without a model; list
pictures whose description holds their text as needing a manual check;
and ask the model a narrower question, or several yes or no questions,
about the rest.

### Images of text with Claude, and four rules, measured on 2026-11-15

The same probe, on the same 44 candidates, then asked the model chosen
for vision-based checks in the candidates' project (decision 8), with
pictures cut and sent as before:

- **Claude Haiku 5.5:** a median of 2.0 seconds a request, 3.4 minutes
  for the 44, about US$0.024 for 158,684 tokens in and 16,876 out. 40 of
  the 44 gave the same answer twice. For TalkBack's caption it answered
  "cannot tell" 21 times and "could be text" 11 times, and 32 of its 34
  reasons named the caption.
- **Claude Sonnet 5.5:** a median of 2.3 seconds a request, 3.9 minutes,
  about US$0.41 for 158,684 tokens in and 9,403 out. 40 of the 44 gave
  the same answer twice. For TalkBack's caption it answered "picture with
  other content" 22 times, and all 34 reasons named the caption; for the
  Settings preview, "could be text" 9 times of 10, one reason saying a
  label below an icon was clipped into the picture.

Neither model was right for the groups a rule can settle, so the owner
chose, on 2026-11-15, four rules without a model, applied before any
question is asked, and a narrower question for the rest:

1. **TalkBack's caption.** A line OCR read in the picture is left out
   when its words are in what TalkBack said or was reading at the time:
   TalkBack's own phrases (such as "Double-tap to activate") when the
   recording holds TalkBack's speech or focus, the utterances of the ten
   seconds before the picture, and the focused item's text, description
   and label. A word within one letter of a word said also counts, as
   OCR misreads the caption now and then ("Doublertap").
2. **Picture out of step.** A picture is not judged when OCR read at
   least 3 lines outside every image that are in no item's text, and
   not TalkBack's, and those outnumber the text items OCR found where
   the tree places them. Too few found alone is not enough: OCR misses
   small labels over a photograph, so a first rule, under a third of the
   tree's text found, also caught pictures in step. Measured: 13 to 38
   such lines in the pictures out of step, 0 to 6 in the pictures in
   step.
3. **Clipped or neighbouring label.** Only lines wholly inside the
   image's bounds count, and not a line a visible text item holds whose
   bounds touch the image's, within a line's height.
4. **The app draws its own text.** When the app's framework, as
   detected at the recording's stop (`android-framework-metadata.md`), is
   Flutter, a picture whose description holds every word left is
   "cannot tell from the recording, check manually", without a model.
   The owner decided that the framework is taken from the recording
   itself only, with no match to other recordings: "We don't need to do
   anything clever for Flutter. All real recordings going forward will
   have the framework detection in place anyway". Recordings made before
   the detection are left to the model.

A line is left out by the rules in that order; a picture with fewer than
3 letters or digits left is set aside by the rule that removed them.
The question now names the app's package and the screen, and keeps its
five answers: logotype, essential, picture with other content, could be
text, and cannot tell. A sixth answer, "the text is not part of the
image", was tried and dropped: with rules 1 and 3 it hardly ever applies
to what is left, and Haiku chose it for cards whose text is inside the
image's bounds, saying the picture held only the drawing.

With the four rules, measured by the probe:

| Group (of the first measurement) | Set aside without a model | Asked |
|---|---|---|
| TalkBack's caption, 17 | 15 by rule 1, 2 by rule 3 (a long caption past the icon's edge) | 0 |
| Out of step, 3, and the dialog over a setting, 1 | 4 by rule 2 | 0 |
| Logotypes, 2 | 1 by rule 2 (on the same picture out of step) | 1 |
| Settings preview, 5 | 5 by rule 3 | 0 |
| Library cards, 16 | 12 by rule 4, matched to the framework detected in a later recording of the same version | 4 |

39 of the 44 were settled without a model; 5 were asked. Haiku 5.5 gave
the same answer twice for all 5, about US$0.003 for 20,128 tokens in and
1,575 out: logotype for the search engine's logo, which is right, and
for the four cards from a recording made before the detection, "could
be text" three times and "picture with other content" once, for the
same kind of card. This confirms that the pictures alone cannot settle
the cards. The match to a later recording was the probe's only; the
rule as built takes the framework from the recording itself (rule 4).

### Images of text, as built

Built on 2026-11-16, at the owner's request to "build the planned AI
checks based on our work", as measured above and following "Keeping it
repeatable".

- **The rules.** Step 8 of `android-checks.md` applies the four rules
  before anything else, and counts the lines and images each set aside.
  An image the rules leave, with 3 or more letters or digits that no
  item's text holds, is a question for the model; the 1.4.5 finding and,
  at AAA, the 1.4.9 finding of an image share one question, asked from
  the first picture the image was judged on. A picture out of step makes
  its images' findings "not run", not a pass and not a question; rule 4
  makes a finding a warning to check manually, without a model.
- **The question.** Two pictures as measured: the whole screen at half
  size with the image outlined in cyan, 4 pixels outside its bounds, and
  the image at full size, at most 1,024 pixels on its longer side, both
  PNG and the same bytes for the same pixels. The words OCR read inside
  the image and its description go with them, and the app's package and
  the screen's name. The answer is one of the five fixed answers with a
  one-sentence reason: a JSON schema holds the model to them, and the
  recorder's own check refuses any other answer, an empty reason or a
  member not asked for, without repairing it.
- **Twice, or once.** Each question is asked twice, and a finding follows
  only where the two answers agree. A local model at temperature 0 gives
  the same answer to the same request, so it is asked once. Claude has no
  seed and takes no temperature (`cloud-models.md`, "5. Repeatability"),
  so a Claude model is always asked twice.
- **What the answers make of the finding.** Agreed "logotype",
  "essential" or "picture with other content": information, the
  exception taken to apply but not confirmed. Agreed "could be text": an
  error, inferred by a model for a person to confirm, with whether a user
  can customize the image not judged. Agreed "cannot tell", or two
  different answers: a warning to check manually, with both reasons. The
  1.4.9 finding takes the same. Every such finding says it is the model's
  inference, names the project's model and whether it is a cloud or a
  local model, and quotes the reason. A question with no answer, as when
  no model is chosen or a request fails, stays a warning, and the check's
  note says why.
- **Kept by the request.** Each question's step key is made from the
  request's SHA-256: the question's template and version, its
  instructions, its words and both pictures. An answer accepted for the
  same request, by the same model with the same settings, in any task of
  any recording, is reused without asking again, as a rerun's steps are
  (`analysis-runner.md`, "Check answers kept by their request"). Running
  a report again asks nothing that has been asked, so it costs nothing
  and gives the same answers; a new template version asks again.
- **Where it runs.** The Android report and the project report ask the
  project's model for vision-based checks about the images the rules
  leave, after the checks without a model. "Model checks", a task of its
  own, asks the same and writes `android-model-checks.md`, listing each
  answered finding. The Checks tab shows the answers already kept for
  the project's model, without asking; its "Ask the project's model"
  button queues the model checks when an image the rules leave has no
  answer from that model. With no model chosen for vision-based checks,
  nothing is asked, and the summary says so.
- **Disclosure.** A report with any finding inferred by a model says so
  (decision 6): the client's report "AI is used in generating this
  report, with a cloud model" (or a local one, or both), and the
  auditor's that its findings are the recorder's own checks except where
  a check asked the model about an image or a piece of text. The auditor's
  findings mark each inferred level,
  give the model's answer in its own row, and add ", if confirmed" to the
  impact; "Limits" gives the check's note on the model.
- **Web images** (2026-11-18, step 26 of `android-checks.md`). A web
  image is an image to the rules, and its alt text, from its text or
  its description, is given as "the image's alt text". Its question
  adds that the image is on a web page and that a browser that has not
  loaded an image draws a stand-in in its place, such as its alt text
  or a broken-image icon, and a sixth answer, "not-loaded": both
  answers agreeing on it make the finding "not run", as the image was
  not seen. An ImageView's question and answers are unchanged, so the
  template stays version 2 and the answers kept for ImageViews still
  hold.

### Text shown as a heading, as built

Built on 2026-11-16, at the owner's request, "Build rules 1 to 4 for the
heading check as you described". Step 19 of `android-checks.md` has
the measurement and the rules: candidates found without a model, and
each asked of the project's model for vision-based checks as images of
text are, with the same pictures, the same three-level answers and the
same keeping by request. Picture B is the part of the screen from the
text down, as whether text is a heading depends on the content after
it. The reports' disclosure says a check asked the model about an
image or a piece of text, as there are now three.

### Images with no text alternative, as built

Built on 2026-11-16, at the owner's request, "Build the image check
with the proposed rules 1 to 5". Step 20 of `android-checks.md` has the
measurement and the rules: images a screen reader says nothing for,
found without a model, and each asked of the project's model for
vision-based checks whether it gives information, with the same
three-level answers. Picture B is a close-up of the image, enlarged
when it is small. Keeping by request was not enough here: 6 identical
icons, with pictures that differed only by the screen round them, were
given two answers. So the answer is also kept against the image's own
pixels and the text beside it, and the first answer for the same image
in the project is given again (point 4 of "Keeping it repeatable",
extended; `analysis-runner.md`, "Answers kept by a signature"). The
question also gives what a screen reader says on the whole screen, with
the image's place marked, and asks whether the image gives information
that this does not (version 2, at the owner's request, "Pass the screen
reader's text for the whole screen to the image check"), and the answer
is given again only where a screen reader says the same on the screen
("Pass the screen reader text to the image check's rule 4 matching
too").

### Icons and descriptions, as built

Built on 2026-11-17, at the owner's request, "Build rules 1 to 8 for
the icon and description checks as proposed". Step 21 of
`android-checks.md` has the measurement and the rules: images with a
description or text of their own, found without a model; for a
control, a question of what kind of image it is, then whether its icon
fits its name; for an image that is not a control, whether its
description fits what it shows. Each answer is kept against the image's
pixels, its name or description and what a screen reader says on the
screen, as the image check's are. A contradiction or a mismatch is a
warning for a person to verify; cannot tell, or two answers, a warning
to check manually; fits or matches no finding, counted. 2.5.3 is not
given for the icon check, as Label in Name is about a visible text
label and an icon has none.

### Screen titles, as built

Built on 2026-11-17, at the owner's request, "Add the screen title does
not describe its screen check with rules 1 to 8 and Chrome option A".
Step 22 of `android-checks.md` has the measurement and the rules. It is
the first check with a text-only question: a title that is not exactly
the screen's own heading, OCR line or spoken item is asked of the
project's model for text-based checks, with what a screen reader says
and the OCR text, or of its model for vision-based checks, with the
screen's picture too, where images cover at least half the screen. In
Chrome, the web page's title is judged. A title judged not to describe
its screen is a warning for a person to verify; cannot tell, or two
answers, a warning to check manually; describes, no finding, counted.

### Decorative images, as built

Built on 2026-11-17, at the owner's request, "Build rules 1 to 8 for
the decorative image check with low impact". Step 23 of
`android-checks.md` has the measurement and the rules: of the images
the icon and description checks choose, one that is a control, or
whose description is all the item holding it says, names that control
or item, so is not asked about; any other that a screen reader says is
asked whether it is informative or decorative, with the whole screen,
a close-up and what a screen reader says on the screen, the image's
own description marked as the image's. Decorative is a warning for a
person to verify, low impact; cannot tell, or two answers, a warning
to check manually; informative no finding, counted.

### Headings and sections, as built

Built on 2026-11-17, at the owner's request, "Build the heading check
with rules 1 to 10 as revised, with the same-area and scrolling
changes". Step 24 of `android-checks.md` has the measurement and the
rules: marked headings, and text shown as a heading that the model
judged one, each once a screen; a heading that is the screen's title is
left to the screen title check, and one with nothing under it is
counted. The rest are asked whether they describe their sections, from
what a screen reader says in the section on screen, the section's items
the tree holds that are not on screen and the text OCR read in it,
within the heading's scrolling area, of the model for text-based
checks, or of the model for vision-based checks with the screen's
picture where images cover at least half of the section. Does not
describe is a warning for a person to verify, medium impact; cannot
tell, or two answers, a warning to check manually; describes no
finding, counted.

### Text not in the tree, as built

Built on 2026-11-18, at the owner's request, "Build the check with
rules 1 to 10, including rule 2c, at high impact". Step 25 of
`android-checks.md` has the measurement and the rules: lines OCR read
in no item's text, each once a visit, on pictures in step with the
tree; TalkBack's caption, lines inside images, short lines, lines a
visible item elsewhere holds, a small item's own text misread and lines
an item in another snapshot of the visit holds are set aside. The rest
are asked, with the whole screen, a close-up, the item the line is
drawn in and what a screen reader says on the screen, whether the line
is drawn text a screen reader cannot reach, covered, not text, or
cannot tell. Drawn text is a warning for a person to verify, high
impact; cannot tell, or two answers, a warning to check manually;
covered and not text no finding, counted.

### Simple and complex questions, measured on 2026-11-17

At the owner's request, "Run the vision checks on the project's
recordings with the new complex model setting", the three checks were
run on the 20 recordings with phone screens in the development
project, with Claude Haiku 5.5 for simple questions and Claude Sonnet
5.5 for complex ones (`projects.md`, "Simple and complex questions").
A probe queued the tasks and ran the analysis worker as the player
does; the probe and its copies were deleted afterwards.

| Check | Questions | Settled by Haiku | Asked again of Sonnet |
|---|---|---|---|
| Text shown as a heading | 54 | 48 | 6 |
| Images of text | 17 | 17 | 0 |
| Images with no text alternative | 13 | 10 | 3, one given the answer kept for the same image |

- **Settled by Sonnet: 4 of 8 asked.** Sonnet gave the same answer
  twice for each: an information icon above a note the screen reader
  reads in full, and a search provider's logo beside a field that names
  the provider, both decorative; a large one-line sentence above a
  control, not a heading; and an uppercase label above a list, a
  heading. For the information icon, one of Haiku's answers contradicted
  its own reason, which said the icon's meaning was not conveyed.
- **Not settled by Sonnet: 4.** All were the same uppercase title of a
  card, in four recordings, styled unlike the screen's section headings.
  Sonnet could not tell twice in three, and gave heading and could not
  tell in the fourth, with consistent reasons: it reads as the card's
  title, not a section heading. These stay warnings for a person to
  verify, which is the right outcome for a borderline case.
- **Cost.** Haiku: 166 requests, 406,376 input and 16,948 output
  tokens, about US$0.049. Sonnet: 16 requests, 35,410 input and 1,995
  output tokens, about US$0.091. Asking Sonnet only what Haiku did not
  settle cost a seventh of asking Sonnet everything, by the 20 times
  measured on the 51 described images.
- **Limits.** Haiku's 75 settled answers were not checked against the
  owner's or Sonnet's: agreeing twice shows repeatability, not
  correctness. The recordings are of apps with few real defects, so
  how often each check finds a real failure waits for the owner's
  fixtures.
- **Owner's conclusion.** "It looks as if this is as far as this check
  can go without a further escalation." No third group is built; the
  next AI check is built on the two groups as they are.

## Later, or text only

- **Focus visible (2.4.7):** needs a Bluetooth keyboard, which the owner
  is waiting for. With one, a model can judge whether the focus is shown
  in the frames.
- **Error messages that do not help (3.3.1, 3.3.3):** text only, and
  wanted (decision 4), with the OCR text and the picture as requirement
  5 sets.
- **Link text that does not say where it goes (2.4.4):** text only, and
  wanted (decision 4), with the OCR text and the picture as requirement
  5 sets, but needs links' spans, which the companion app does not
  record yet.
- **Flashing (2.3.1):** better measured from the frames without a model.

## Keeping it repeatable

Adopted by the owner, 2026-11-15. The same approach as the summaries
(`analysis-runner.md`, `cloud-models.md`):

1. **Rules choose, the model answers.** The checks without a model
   choose the items and frames; the model is asked one narrow question
   about each, as in the tables above, never "find the problems on this
   screen". This keeps the number of requests, and the cost, set by the
   recording, and each answer tied to an item.
2. **Closed answers with their evidence.** Each question has a fixed set
   of answers, such as "informative", "decorative" or "cannot tell",
   given with the item's outline on the frame as its evidence. An answer
   outside the set is refused, as the summaries' checks refuse output now;
   it is never completed or patched.
3. **Fixed settings.** A local model is run at temperature 0 with a fixed
   seed. Claude has neither (`cloud-models.md`, "Repeatability"), so with
   a Claude profile repeatability rests on reuse alone, and the report
   says which profile answered, and whether it was cloud or local
   (requirement 3).
4. **Each answer reused.** An answer is kept by the picture's SHA-256
   (the text's, for a text-only check, OCR's as well), the question and its version,
   the model (the file's SHA-256 and the
   server's build for a local model, the model ID and API version for
   Claude) and the settings, as accepted steps are reused now. Running a
   report again asks nothing that has been asked, so it costs nothing and
   gives the same answer; changing the question's version asks again.
5. **Marked as inferred.** A finding from a model is labelled as
   inferred, with the profile and question that gave it, and kept apart
   from measured findings, with its own confidence.
6. **Measured first.** Before a check's rules are set, it is run on the
   phone recordings to hand: how many items it sends to a model, how long
   and how much that takes, and how often the answers agree with the
   owner's on a sample. The owner then chooses the rules, as for every
   check so far.
7. **The reason before the answer.** A closed answer is a JSON object
   with the reason first and the answer after it
   (`ClosedAnswer.Schema`), and each question ends "First write one
   sentence saying what in the pictures shows the answer, then give the
   answer." Added 2026-11-17, at the owner's request ("Make the changes
   and commit"), after measuring it on the check of images with a
   description: asked for the answer first, Claude Haiku 5.5 gave some
   answers that its own reasons contradicted, the same way both times,
   such as "contradicts" for a house icon named "Open the home page"
   with the reason that the icon matches; asked for the reason first, on the same
   51 images, all 220 replies came back reason first and none of the
   answers read contradicted its reason. Every check asked so far was
   given a new template version: images of text 2, text shown as a
   heading 2, and images with no text alternative 3, so no answer given
   in the old order is reused.
8. **Simple and complex questions.** Each question is simple or complex,
   and each group has its own model in the project's settings, the same
   one by default. A simple question the model for simple questions
   does not settle, with two answers or agreeing that it cannot tell, is
   asked again of the model for complex questions where that is another
   model, as runs 3 and 4, and that model's answers decide. Added
   2026-11-17 at the owner's request ("build the two now and add
   escalation"), after measuring Claude Haiku 5.5 and Claude Sonnet 5.5
   on the same 51 described images: Sonnet split one question of 116 and
   knew icons that Haiku split on, at about 20 times the cost. See
   `projects.md`, "Simple and complex questions".

## Measuring before building

For each check the owner picks, on the phone recordings of 2026-11-10 to
2026-11-14:

- the items and frames the rule chooses, a screen and a recording at a
  time;
- the requests that makes, their size, and the time and cost with a local
  model and with Claude;
- the answers, against the owner's for a sample of items, to see how
  often the model is right and where it is wrong;
- whether the same request gives the same answer again, for the local
  model.

The probe and its copies are deleted afterwards, as before.

## Open questions for the owner

The owner answered the earlier questions on 2026-11-15 (decisions 1 to
8 above), and chose the rules for images of text the same day ("Images
of text with Claude, and four rules, measured on 2026-11-15"). New
questions will come from measuring each check on the recordings before
its rules are set.
