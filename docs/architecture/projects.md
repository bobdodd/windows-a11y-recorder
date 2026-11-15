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
for the project, with no model" in the Analysis menu, or "Queue Android
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
