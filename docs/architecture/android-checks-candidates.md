# Android Checks: Candidates From WCAG2ICT

## Status

Recorded 2026-11-12 for future reference, at the owner's request. These
are options, not decisions: no check here is built, and each would be
measured on the recordings to hand before its rules are set, as the
earlier checks were (see `android-checks.md`). The owner's standing
rule applies: testing is AI-free as far as possible, with AI kept for
summary reports and vision-centric tests, and OCR may be used in
addition to the text the tree gives, saying when it is.

## Already built

The ATF checks (touch target 48dp, speakable text, duplicate speakable
text, duplicate clickable bounds, editable content description,
redundant description, class name, traversal order), target size to
WCAG 2.5.8 (24dp) and, at the AAA audit level, 2.5.5 (44dp), text
contrast to 1.4.3 or, at AAA, 1.4.6, non-text contrast to 1.4.11, the
screen's title to WCAG2ICT 2.4.2, and headings to 1.3.1.

## What the recordings hold

- **The tree** of each window, with views not important for
  accessibility included (`flagIncludeNotImportantViews` in the
  companion's service settings). For each item: class, view ID, text,
  content description, state description, hint, pane title, tooltip,
  error text, bounds, the checked, expanded, enabled, clickable, long
  clickable, focusable, focused, editable, password, heading and
  selected states, input type, maximum text length, live region, the
  items that label it (`getLabeledByList()` or `getLabeledBy()`), its
  traversal before and after, its actions, and its collection, item and
  range information.
- **The accessibility events**, with their source, text and records.
- **The touches**, with the display's rotation, and the display's state
  as it turns.
- **The display density** and **each app's version**, with its target
  API.
- **The pictures** of settled snapshots (the phone's screenshots, or
  video frames), and the words Windows OCR reads on them, each placed
  as the app's text, words in no item's text, or the app's text where
  OCR read none.

Not held: the font scale, the locale, the app's manifest and resources
(the APK work is deferred), and autofill hints, which the accessibility
tree does not give.

## Easy wins, from the tree already recorded

1. **2.5.3 Label in Name (level A).** WCAG2ICT applies it directly as
   written ([WCAG2ICT](https://www.w3.org/TR/wcag2ict-22/)). The
   criterion: "For user interface components with labels that include
   text or images of text, the name contains the text that is presented
   visually." Capitalization and most punctuation are ignored; text used
   as a symbol (such as ">") is not a label; having the label's text at
   the start of the name is a best practice
   ([Understanding SC 2.5.3](https://www.w3.org/WAI/WCAG22/Understanding/label-in-name.html)).
   For each clickable item, the visible text is its own and its
   children's text; the name is its content description where it has
   one. Two discrete findings: the name lacks the visible text (an
   error), and the visible text is not at the start of the name (a
   warning, best practice). An item with no visible text is not
   checked.
2. **Clickable but not focusable (2.1.1 Keyboard).** From API 26 a
   view's focusability defaults to "auto", and it is focusable if it is
   clickable or has an `OnClickListener`
   ([Android View reference](https://developer.android.com/reference/android/view/View#FOCUSABLE_AUTO)).
   A clickable item that is not focusable has usually been made so, and
   a keyboard or switch user cannot reach it. A warning, as a likely
   failure until a keyboard test confirms it; an item inside a
   focusable clickable parent would be set aside.
3. **Smaller checks of fields and states**, each to be measured first:
   - a text field with no label: no item labelling it, no content
     description, a hint only (3.3.2 and 4.1.2); Material's floating
     labels may need setting aside;
   - a field marked invalid with no error text (3.3.1);
   - an item with expand or collapse actions but no expanded state
     (4.1.2);
   - a slider with no range information (4.1.2).

## Medium, from events and touches, by rules that may mislead

- **4.1.3 Status Messages:** text that appears with no change of focus,
  and whether it was announced (a live region, an announcement event or
  a toast). WCAG2ICT: there is "still a user need to have status
  messages be programmatically exposed so that they can be presented to
  the user by assistive technologies without receiving focus"
  ([WCAG2ICT](https://www.w3.org/TR/wcag2ict-22/)).
- **2.2.1 Timing Adjustable and 2.2.2 Pause, Stop, Hide:** screens or
  content that change with no touch, such as carousels that move on or
  time-outs; flags for a person, leading into the deferred APK and
  timer work.
- **3.2.1 On Focus and 3.2.2 On Input:** a new window straight after a
  change of focus, typing or a choice, with no tap.
- **1.3.4 Orientation:** the display's rotation is recorded, so if a
  test turns the phone and the app's window does not turn, the app is
  locked. WCAG2ICT applies it except to software never shown on
  hardware that is turned in typical use
  ([WCAG2ICT](https://www.w3.org/TR/wcag2ict-22/)). Needs a step in the
  test procedure.
- **1.3.2 Meaningful Sequence:** the screen reader's order against the
  order on the screen, top to bottom and left to right; layouts in
  columns would give false findings.

## 1.4.5 Images of Text

### What the standards ask (checked in cited documents)

- **1.4.5 (level AA):** "If the technologies being used can achieve the
  visual presentation, text is used to convey information rather than
  images of text", except where the image of text can be visually
  customized to the user's requirements, or a particular presentation
  is essential; "Logotypes (text that is part of a logo or brand name)
  are considered essential". WCAG2ICT applies it directly as written
  ([WCAG2ICT](https://www.w3.org/TR/wcag2ict-22/#sc1-4-5-images-of-text)).
- **An image of text** is "text that has been rendered in a non-text
  form (e.g., an image) in order to achieve a particular visual
  effect", and "does not include text that is part of a picture that
  contains significant other visual content", such as graphs,
  screenshots and diagrams
  ([Understanding SC 1.4.5](https://www.w3.org/WAI/WCAG22/Understanding/images-of-text.html)).
- **1.4.9 Images of Text (No Exception)** is level AAA; WCAG2ICT does
  not cover it.

### What the recordings give (measured 2026-11-12)

Yes, the images' places are known when they are in the tree: an
`ImageView` is listed with its bounds, whether or not it is important
for accessibility. OCR's words are already placed on the picture, so
words inside an image's bounds can be found with no new reading.

In both recordings of the client's app (test 16, session
20261112-154851-32c7b61a5552453bbf5febffcca978c7, from video frames;
test 18, session 20261112-171107-88cfc05214a34a3682222e7fb25d840d, from
the phone's lossless screenshots), the Sleep Learning Library screen
has 4 `ImageView` items, 963 by about 400 pixels each, each described
as a card's title and subtitle ("Understanding Melatonin", "Sleep
Hygiene", "Sleeping Environment", "Emotion, Health and Sleep"). OCR read
those titles (words 42 pixels high) and subtitles (23 to 29 pixels
high) inside each image's bounds, matching its description, and no
other item in the tree has that text. The cards' text is drawn in the
pictures. No other screen measured has an image in the tree, and OCR
found no words outside the tree's text on any of them.

### A possible rule

- **Probable image of text (1.4.5)**, a warning on an image item
  (an `ImageView`, or an item whose role is image) when OCR reads
  words inside its bounds that no text item inside those bounds holds.
  It says the words were found by OCR, how high they are, and whether
  the image's description holds them.
- **Text found by OCR in no item**, a separate finding, when OCR reads
  words where the tree has no item holding them: the text is on the
  screen but not given to assistive technologies. This is where a
  decorative Compose image would show, as Compose leaves an image with
  no description out of the tree.
- **At AAA**, the same finding is also reported to 1.4.9.

### Limits

- The exceptions are not judged: a logotype, a photograph, chart or
  diagram with significant other visual content, and an image that can
  be customized are reported, and need a person or a vision model to
  set aside. On an image the size of a card with a title and subtitle,
  as in the client's app, the finding is likely to stand.
- OCR misses stylised, small or low-contrast text, so an image of text
  can be missed; and it can read shapes as letters, so a few
  characters in an image are better treated as no finding.
- A later AI-free confirmation: text in an image does not grow with
  Android's font size, so a recording at a larger font scale would
  show the words inside the image staying the same height (with 1.4.4,
  once the font scale is recorded).

## Hard, or needing more to be recorded

- **1.4.4 Resize Text:** needs the font scale recorded, a pass at 200%
  (or the platform's largest), and a way to find clipped text.
  WCAG2ICT recommends scaling "up to 200 percent or, if the platform
  provides text resizing capabilities but it does not reach 200 percent
  for all text, up to the text sizing capabilities of the platform"
  ([WCAG2ICT](https://www.w3.org/TR/wcag2ict-22/)).
- **1.4.10 Reflow:** the Pixel 10 Pro XL is about 443 dp wide (1080
  pixels at 2.4375 pixels a dp), not the 320 CSS pixels the criterion
  uses, so the display's size would have to be changed for the test.
- **1.3.5 Identify Input Purpose:** WCAG2ICT puts out of scope
  technologies "that do not provide attributes that support identifying
  the expected meaning for the form input data"
  ([WCAG2ICT](https://www.w3.org/TR/wcag2ict-22/)); Android's autofill
  hints are such attributes but are not in the accessibility tree, so
  this waits for the APK work. The input type is a weaker sign.
- **2.5.1 Pointer Gestures, 2.5.2 Pointer Cancellation, 2.5.7 Dragging
  Movements:** the touches show which gestures were used, not whether a
  single-pointer alternative exists.
- **3.1.1 Language of Software:** needs the locale recorded; little to
  find in an app that follows the platform's language.

## Not applicable to one app

WCAG2ICT applies 2.4.1 Bypass Blocks, 2.4.5 Multiple Ways, 3.2.3
Consistent Navigation, 3.2.4 Consistent Identification and 3.2.6
Consistent Help to sets of software programs, and says such sets
"appear to be extremely rare"
([WCAG2ICT](https://www.w3.org/TR/wcag2ict-22/)). 4.1.1 Parsing is
obsolete and removed in WCAG 2.2.

## For a person or a vision model

Whether descriptions are good (1.1.1), sensory characteristics (1.3.3),
use of colour (1.4.1), whether headings and labels describe their topic
or purpose (2.4.6), and focus visible (2.4.7, which could become an
AI-free comparison of pictures if the recorder drove a keyboard).
