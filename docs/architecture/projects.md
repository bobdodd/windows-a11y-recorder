# Projects of Several Recordings

## Status

Built 2026-11-14, not yet checked on the development PC. The tests in
the Linux build environment pass (`ProjectCatalogTests` and the project
tests in `AndroidReportTests`), including a project's Word document
checked against Office 2016's schema and converted to PDF with
LibreOffice to be looked at page by page; see
[the validation plan](../validation/projects-plan.md) for the tests on
the development PC.

## The request and the owner's decisions (2026-11-14)

The owner asked: "Next, it is time to introduce projects with multiple
recordings. The user would be able to select a report for a specific
recording, or a report covering all recordings with grouping of screens
between recordings".

Asked three questions, the owner answered:

1. How a recording comes to be in a project: "both. automatically on
   creation. possible to add later or move from another project".
2. When a screen in one recording is the same as a screen in another:
   "Same screen key", the option given as "Same app, activity and
   title, the rule already used within a recording; app versions grouped
   together and each finding labelled with its version".
3. A finding seen on the same screen in more than one recording: "Group
   it too", the option given as "Same check, level and item given once
   under the screen, listing each recording's visits, as for visits
   now".

This replaces decision 7 of 2026-11-11 in
[the Android checks](android-checks.md), that screens are matched
within one recording and that matching across recordings waits for the
test data model. The rest of the test data model, people, tests and
pages, is still to come; a project is the first part of it in use.

The project's standing rule applies: the report of a project, like a
recording's, is made without AI, for cost and repeatability.

## Projects in the catalog

A recording in a project may also have a readable name, unique within
the project; see [recording names](recording-names.md).

The catalog has had a projects table since the first migration, and
every recording a project, "Default project" until now. So every
recording made before this change is in "Default project", and a
project report of it covers all of them.

- **On creation.** The side panel has a Project group with "Project for
  new recordings:", a list of the projects, and "New project...". A new
  recording goes into the project chosen. The choice is remembered
  between runs in `project.txt`, beside the player's layout file; when
  the project remembered is gone, "Default project" is chosen. The
  list cannot be changed while recording.
- **Later.** "Projects and recordings..." in the side panel and in the
  Project menu opens a window listing the projects, with their numbers
  of recordings, and the selected project's recordings in the order
  they were made. Projects can be made and renamed there, and selected
  recordings moved to another project. A name is unique, compared as
  typed with spaces at either end removed; a name in use is refused
  with a message saying so.
- **Moving.** A recording moves with its own analysis tasks and their
  reports. A project's tasks stay with the project, so a project report
  written before a move still lists the recordings it was written
  from.

No project is removed by the player yet; removing one in the database
removes its tasks with it.

## A task of a project

An analysis task was of one recording. Migration 0042 lets a task be of
a project instead: `recording_id` may be null, a new `project_id`
refers to the project, removed with it, and a check constraint requires
exactly one of the two. The worker runs a project's task as any other;
a task kind that needs one recording fails with "This task is of a
project, not of one recording." if given a project's.

The Analysis tab of an open recording says which project it is in and
how many recordings it has, and lists the project's tasks with the
recording's own. The project's tasks are queued from "Android report
for the project" in the Analysis menu, or "Queue Android
report for the project" on the tab. Its kind is
`android-project-report`, named "Android report for the project".

The task is given the folder that holds the open recording's session
folder, and reads each of the project's recordings from
`<that folder>\<session key>`, in the order they were made. Its report
is written to `<that folder>\Projects\<project ID>\analysis\<task ID>\`,
as `android-project-report.md` and `android-project-report.docx`, with
the client's Word document made as a recording's is. "Open in Word"
opens it.

A recording is left out of the report, with the reason given under
"Limits", when it was still being recorded, when its events are in the
database's evidence tables rather than a recording file, when its
recording file is not in the folder the task was given, or when it has
no phone screens. A task with no recording left fails, saying why each
was left out.

## The project's models

Built 2026-11-15, not yet checked on the development PC. The owner asked:
"The model UI selection is nonsense. Change it so that projects have
settings and we can choose models (or no model) for report analysis, for
text-based checks, for vision-based checks. Remove the existing
checkboxes and model selection from analysis and checks. Once I have
those in place, you will use the settings I choose."

- **Three settings for each project.** The model for report analysis,
  the summaries: the recording summary, the summary with vision and the
  Android summary. The model for text-based checks, checks that ask a
  model about text, such as an item's name. The model for vision-based
  checks, checks that ask a model about a picture, such as an image of
  text. Each is a model, by name, or no model. No model means
  none is asked, cloud or local, and what needs one is not done: a
  summary cannot be queued, and a check that needs one is listed as not
  run.
- **In the catalog.** Migration 0045 adds `report_model`,
  `text_checks_model` and `vision_checks_model` to `projects`, each the
  profile's name or null for no model. Every project, the ones made
  before and every new one, starts with no model for each, so nothing
  is sent to a model until one is chosen. A name no model has any
  longer is kept, shown as "(not offered on this PC)", and not used.
- **Where they are chosen.** "Project settings..." in the Project menu,
  for the open recording's project, else the project chosen for new
  recordings; "Project settings..." in the Analysis tab, for the open
  recording's project; and "Project settings..." in Projects and
  recordings, for the selected project. The window has three lists, each
  of no model and the profiles that can do what the use needs: those
  that take text for report analysis and text-based checks, those that
  take images for vision-based checks. Since 2026-11-17 each kind of
  check has a second list, for its complex questions ("Simple and
  complex questions", below). A profile that sends to Anthropic
  is listed with "(sends to Anthropic)" after it, as everywhere a profile
  is chosen. Nothing is chosen for a project by default, cloud or local.
- **Where they are shown.** The Analysis tab says the open recording's
  project's models, one use after another. The Checks tab says the two
  models for checks, that the images-of-text check, the check of text
  shown as a heading and the check of images with no text alternative
  ask the model for vision-based checks about the images and text their
  rules leave, and that no
  check uses the model for text-based checks yet; it is there because a
  torn-out Checks window has no menu, and the checks that need a model
  run from that tab ("Ask the project's model";
  `android-vision-checks.md`, "Images of text, as built").
- **Used when a task is queued.** The project's settings are read again
  each time a task is queued, Run again included, so a task always uses
  the settings as they are then, not those an earlier task was queued
  with. A summary is queued with the model for report analysis. Add
  vision to the summary needs that model to take images, and queues the
  summary and vision both on it; until now the summary used a text
  profile and vision the default profile for images. The Android reports
  and the model checks are queued with the models for checks, and the
  Android summary with the model for report analysis and the models for
  checks; the images-of-text check uses the model for vision-based
  checks, and no check uses the model for text-based checks yet. Each task's row names the models it was queued with,
  by use, or says "No model."
- **Used at each step.** A task keeps the models it was queued with, but
  the worker reads the project's settings again before each model step:
  where they now choose no model for the step's use, the step asks no
  model, reuses no earlier answer, and fails the task saying so. So
  choosing no model stops a task already queued or running from asking
  one.
- **What was removed.** "Use AI in analysis" in the Analysis menu, "Use
  AI in checks" in the Checks tab, the Analysis tab's Profile list and
  "Use AI in this report", and the default profiles for text and for
  images in the model profiles window and the settings file. See
  `analysis-runner.md`, "Turning AI analysis off".
- **The models offered (2026-11-15).** Each list offers Claude Opus 5.5,
  Claude Sonnet 5.5 and Claude Haiku 5.5, with default settings and no
  profile to make, then the models set up on this PC. The window has the
  project's Anthropic API key, kept protected in the catalog, which a
  Claude model needs; the Model profiles window is gone. See
  [cloud models](cloud-models.md), "As built: Claude models in the
  project's settings".
- **The reports.** A report made with a model says so, and whether the
  model was in the cloud or on this PC, as before (`android-report.md`,
  "Cloud or local"). One made without says nothing about AI.

## Simple and complex questions

Built 2026-11-17, and run the same day on the development PC on the
project's 20 recordings with phone screens, with the models chosen in
the settings window, by a probe running the analysis worker; the
Checks tab's "Ask the project's model" is not yet checked there. The
measurement is in `android-vision-checks.md`, "Simple and complex
questions, measured on 2026-11-17". The owner
asked: "For some image checks Haiku is clearly good enough, for others
we may need a more powerful model that would be wasted on simpler
checks. We need to be able to set the model per group of checks -
simple, more complex, hard perhaps?", and then: "build the two now and
add escalation".

- **Two groups for each kind of check.** Each closed question a check
  asks is simple or complex. The model for text-based checks and the
  model for vision-based checks are now each kind's model for simple
  questions, as saved before, so a project's settings and its queued
  tasks keep working. Each kind also has a model for complex questions.
  Text-based checks have the two groups as well, so that they work the
  same way when the first one is built.
- **The same model by default.** The model for complex questions is "The
  same model as simple questions" until another is chosen, so choosing
  one model for a kind of check, with the project's API key for a Claude
  model, is enough. It can also be no model, or any profile that can do
  what the kind needs.
- **Which group a question is in.** Each question says its group by its
  request's use: `vision-checks` for a simple question,
  `vision-checks-complex` for a complex one, and the same for text. The
  three questions asked now (images of text, text shown as a heading,
  images with no text alternative) are simple. No complex question is
  built yet.
- **Asking again.** A simple question that the model for simple
  questions does not settle, as its two answers differ or it agreed that
  the pictures do not tell, is asked again of the model for complex
  questions, where that is another model. It is asked as runs 3 and 4,
  or run 3 alone for a repeatable model, a local model at temperature 0.
  Its answers decide the finding, and the finding says why it was asked:
  for example "Claude Haiku 5.5, a cloud model, the project's model for
  simple questions, could not tell, so the question was asked again of
  the project's model for complex questions; Claude Sonnet 5.5, a cloud
  model, inferred ...". The finding is that model's inference. Where the
  question asked again fails, the first model's answers stand, a
  warning, and the check's note says why. A settled question is not
  asked again, so the stronger model is paid for only where the first
  one did not settle the question.
- **Kept answers.** The answers asked again are kept and reused as the
  others are (`android-vision-checks.md`, "Keeping it repeatable"): by
  their request, and for images with no text alternative by the image's
  signature, rule 4. The Checks tab reads them for the model for complex
  questions as it reads the first model's for the model for simple ones;
  a question that needs asking again whose answer is not kept yet is
  left unanswered, so "Ask the project's model" can ask it, and the note
  counts it as waiting for the model for complex questions.
- **When "Ask the project's model" is available.** The Checks tab's
  button is available when a recording is open, a question the rules
  left is unanswered, no model checks of the recording are queued or
  running, and the project can use its model for vision-based checks
  and, where another is chosen for complex questions, that one too:
  each must be offered on this PC, take images, and, for a Claude
  model, have the project's API key (`ProjectModels.ChecksProblem`).
  Added 2026-11-17 at the owner's request, "Make the button also
  require a usable complex model when one is chosen"; before, only the
  model for simple questions was checked. Where either cannot be used,
  the Checks tab's line of models says why and that the button is
  unavailable until it is put right.
- **In the catalog.** Migration 0048 adds `complex_text_checks_model` and
  `complex_vision_checks_model` to `projects`: null for the same model
  as simple questions, an empty name for no model, or the profile's
  name. A check task is queued with the model each group asks, by its
  use (`ProjectModels.ForChecks`), and its row names both.
- **In the settings window.** The window has five lists, with a line
  saying how the groups work: report analysis, then each kind's simple
  and complex questions. The lists for complex questions start with
  "The same model as simple questions", then "No model", then the
  profiles. Their text wraps, with no scrolling sideways.

## Grouping screens across recordings

Each recording's screens and visits are found as before, within the
recording ([the Android checks](android-checks.md)). The report then
takes the recordings in turn, and a screen of a later recording is the
same report screen as an earlier one when it has the same screen key:
the same app, the same title, and the same title shared by other
screens or not, the same view IDs where those name it, and the same
activity, or an activity unknown in either. This is the rule used to
match visits within a recording.

- A report screen is numbered by its first visit in the first recording
  it was seen in, and named from that recording.
- Untitled screens, named by their activity and a number, as
  "MainActivity, untitled screen 2", are numbered again across the
  project, so that each number is one screen in the whole report.
- Its visits are named by their recording and their number within it,
  as "Recording 2, visit 3", in the headings, the pictures' alternative
  text, and the evidence.
- The screen's introduction says which recordings it was seen in.

A report of one recording is the same as before: the grouping does
nothing when there is one recording, which a test checks.

## Grouping findings across recordings

A finding on a screen seen on more than one visit, in one recording or
in several, the same check at the same level on the same item, is given
once, under "Findings on more than one visit" before the screen's
visits, as within a recording
([the Android report](android-report.md#findings-on-more-than-one-visit)).
Its "Seen" row lists each visit with its recording and the app's
version in that recording, for example "recording 1, visit 1 (version
1.0), twice, in 1 picture, at 00:00:01.500 [E8]".

A finding of the app as a whole, such as no live region anywhere in the
app, is given once for the project, with each recording's measurement
and its version.

## What a project's report adds

- **About this project.** The project's name, how many recordings it
  covers and how many were left out, their length in all, each app's
  version in each recording, the screens and visits with how many were
  seen in more than one recording, and the audit settings.
- **The recordings.** A table of the recordings, numbered in the order
  they were made, with each one's session key, start, length, screens
  and visits, and evidence.
- **How to read this report.** Two paragraphs on how screens and
  findings are grouped across recordings.
- **Evidence.** The column "Recording and time" gives each reference's
  recording and its time within it, as "Recording 2, 00:00:08.500".
- **Pictures.** Their files are named by recording, as
  `recording-02-visit-0003-8500000000.png`, so that two recordings'
  pictures do not overwrite each other.

The report's title is "Android accessibility report: project" and the
project's name; the client's Word document is titled by the apps it
covers, as a recording's is.

## Measured on the development phone's recordings (2026-11-14)

The rule was run, without a model and without the pictures, on every
phone recording made on the development PC from 2026-11-09 to
2026-11-14, as one project.

- Of 43 phone recordings, 23 had no phone screens in their recording
  file and would be left out of a project's report, with that reason.
- The other 20 had 58 screens between them, which became 31 report
  screens; 14 of those were seen in more than one recording. Their 195
  findings, without the contrast checks, became 117.
- Screens named by a window title matched well across recordings: the
  launcher's Home in six recordings, Gmail's Mail in three, Settings in
  three, and Settings screens such as Colour and motion, Colour
  inversion, and Display size and text in two each.
- A client app named only by its screens' first fixed text, under a
  shared app title, also matched: one screen in five recordings, one in
  four, and two in three, one of those named by an emoji, its first
  fixed text.
- The limit above was seen. The launcher's Home was named by its
  window title in six recordings and by its first fixed text, under a
  shared title, in two others, so the report gives it as three screens
  (the same text under two different shared titles in those two); All
  apps as two. The client app's Sleep Learning, seen once, did not
  match its Sleep Learning Library, seen in four; whether they are one
  screen the rule cannot say.

## Limits

- A screen named from its own text, when no window title names it, can
  be named differently in two recordings if that text was shared by
  other screens in one of them, and is then given as two screens. The
  report says so under "Limits".
- A project's evidence times are each within their recording, so the
  references in the player's reading view do not move the playhead of
  the open recording.
- The recordings are read from the folder holding the open recording.
  A recording kept elsewhere is left out, with the reason given.
- Every recording's evidence is held in memory while the report is
  written, so a project of many long recordings needs more memory than
  any one of them.
- There is no Android summary, with a model, of a project yet.
- A screen matched by its key is the same screen by that rule, not
  proven to show the same content; its findings stay per visit in the
  checks and the player's Checks tab, and the report groups only those
  with the same check, level and item.
