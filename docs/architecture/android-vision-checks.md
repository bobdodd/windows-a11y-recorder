# Android Checks With a Vision Model: Preliminary Plan

## Status

Recorded 2026-11-15 at the owner's request, "so it doesn't get lost".
This is a preliminary plan: the checks below are options, not decisions,
until the owner chooses them. None is built. Each is to be measured on
the recordings to hand before its rules are set, as the earlier checks
were (see `android-checks.md` and `android-checks-candidates.md`).

The owner's standing rule applies: "As far as possible testing should
be AI-free both for cost and repeatability, I expect to use AI to help
with summary reports and vision-centric tests only". Almost everything
here is vision-centric. The few that would need a model reading text
alone are marked, and are outside the rule unless the owner extends it.

The guidance in "Keeping it repeatable" was adopted by the owner on
2026-11-15: "I like your "Keeping it repeatable" comments and we should
follow that guidance."

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
| A name that contradicts its icon | 1.1.1, 2.5.3 | Does the icon in this outline show what the name says? | Controls with an icon and a content description, and no text |
| An informative image with no description | 1.1.1 | Is this image informative or decorative? | Image items with no name |
| A decorative image that is announced | 1.1.1 | Is this image informative or decorative? | Image items with a name, beside text that says the same or nothing |
| A label that is only placeholder text | 3.3.2 | Is there a visible label beside this field once text is typed? | Fields whose hint was the only text, before and after typing |

### Structure

| Check | WCAG | What the model is asked | Chosen without a model |
| --- | --- | --- | --- |
| Text that looks like a heading but is not one | 1.3.1 | Is this text shown as a heading? | Text items larger or bolder than the screen's body text, not marked as headings |
| A heading that does not look like one | 1.3.1 | Is this text shown as a heading? | Items marked as headings |
| A title that does not describe its screen | 2.4.2 | Does this title describe the screen shown? | Each screen's unique title, once a screen |
| A heading that does not describe its section | 2.4.6 | Does this heading describe what follows it? | Each heading and the items under it |
| Content in an order that does not make sense | 1.3.2 | Does the content read in a sensible order? | Screens with several blocks of text, in the order a screen reader moves |

### What the tree does not show

| Check | WCAG | What the model is asked | Chosen without a model |
| --- | --- | --- | --- |
| Text shown on screen that is not in the tree | 1.1.1, 4.1.2 | Is this recognised text part of an item, or drawn? | Text found by OCR in the frame where no item's text or bounds match |
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
| Instructions that rely on shape, colour or place | 1.3.3 | Does this text refer to something only by its shape, colour or place? | Text with words such as "green", "round", "on the right"; text only, outside the standing rule |

## Settling existing warnings

Some findings are warnings now because an exception cannot be judged
without understanding the picture. A model could settle them:

- **Images of text (1.4.5, 1.4.9):** is it a logotype, or otherwise
  essential?
- **Target size (2.5.5, 2.5.8):** does the exception for an equivalent
  target, or for a target in a line of text, apply?
- **Boundaries (1.4.11):** is this control's boundary needed to identify
  it? These are listed as info now.

This mainly removes false positives rather than adding checks, and may be
the best value for the cost.

## Later, or text only

- **Focus visible (2.4.7):** needs a Bluetooth keyboard, which the owner
  is waiting for. With one, a model can judge whether the focus is shown
  in the frames.
- **Error messages that do not help (3.3.1, 3.3.3):** text only.
- **Link text that does not say where it goes (2.4.4):** text only, and
  needs links' spans, which the companion app does not record yet.
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
   says which profile answered.
4. **Each answer reused.** An answer is kept by the picture's SHA-256, the
   question and its version, the model (the file's SHA-256 and the
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

1. Which checks first? Settling the existing warnings is likely the
   cheapest and most useful start.
2. A local model, Claude, or either? Sending a client's frames to Claude
   needs the client's agreement; choosing a cloud profile does not give
   it.
3. How a model's findings appear in the client's report: with their own
   impact, High, Medium or Low, as other findings, and how the report says
   they were inferred and then inspected.
4. Whether "cannot tell" is reported, and as what.
5. Whether the text-only checks are wanted, which would extend the
   standing rule.
