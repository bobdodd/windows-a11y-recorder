# Recording Names

## Status

Built in the Linux build environment and covered by its tests; awaiting
the development PC's tests in
[the validation plan](../validation/recording-names-plan.md).

## The request and the owner's decisions (2026-11-14)

The owner asked: "I want to be able to name and rename recordings.
Currently we give them an unique name, but I want to be able to give
them more readable names. SO I want to be able to chose when I start a
recording, and I want to be able to edit later".

Asked how, the owner chose:

- When a recording starts: "Prompt on Start". A dialog asks for the
  name each time Start recording is chosen, and the name may be left
  empty.
- Uniqueness: "Unique within project". No two recordings in one project
  have the same name; recordings in different projects may.
- How a named recording is shown: "Name, then session key", as
  "Sign in (20261114-184458-7e51…)", so its folder can always be found.
  The folder is never renamed.

## What a name is

A recording's session key, such as
`20261114-184458-7e51436044ce43f1a201eb2daef64acd`, stays its identity:
the name of its folder, the key the catalog and the player find it by,
and the name its project's report reads it from. A name is a readable
label beside it, kept in the catalog only.

- A recording may have no name, as every recording made before this
  change has none. It is then shown by its session key alone, as before.
- A name is saved with spaces at either end removed. A name of only
  spaces is no name.
- Names are compared as typed, as project names are: "Sign in" and
  "sign in" are different names.
- Migration 0043 adds `recordings.name`, with a check that it is never
  empty or padded with spaces, and a unique index on the project and the
  name for named recordings.

## Naming a recording when it starts

Choosing Start recording, after the recording's options are checked,
opens "Name this recording". It says which project the recording goes
into, that the name can be left empty, that no two recordings in a
project can have the same name, and where to rename it later. The name
box has the focus.

- "Start recording", the default button, starts the recording with the
  name typed, or with none.
- Cancel closes the dialog and the recording does not start.
- A name another recording in the project has is refused in the dialog,
  before the recording starts: the reason is shown under the name box
  and spoken, and the focus returns to the name box with the name
  selected. The catalog's unique index refuses it as well, if another
  recording took the name meanwhile, and the recording does not start,
  with the reason given.
- When the recording has started, it is announced with its name, as
  "Recording Sign in started."

## Renaming a recording

A recording is renamed in either of two places, with the same dialog,
whose default button is Save:

- Projects and recordings: select one recording in the selected project
  and choose "Rename recording...". The list shows the new name, the
  recording stays selected, and the status line says what changed.
- The Project menu: "Rename the open recording..." renames the
  recording open in the player. The window's title and the Analysis tab
  show the new name.

Leaving the name empty removes it, and the recording is known by its
session key again. A name another recording in the project has is
refused, as when a recording starts. Renaming changes neither the
folder, the session key, the recording file nor its analysis.

## Moving a named recording

Moving recordings to another project, in Projects and recordings, moves
each one unless the other project already has a recording of the same
name. Those are left where they were, and the status line lists them,
by name and session key, and says to rename them first. The others are
moved, with their analysis, as before.

## Where a name is shown

As "name (session key)", or the session key alone:

- In Projects and recordings, each recording in the list.
- In the player, the window's title, as
  "Sign in (20261114-…), Windows Accessibility Recorder", and the
  Analysis tab's line about the open recording's project.
- In reports written after the recording is named: the Android report's
  and summary's heading and their "Recording" row; a project's Android
  report's table of recordings and its list of recordings left out; and
  the heading of a desktop recording's summary and report.

The name is not given to a model: the reports' headings are written
after the model's steps, from the catalog.

## Limits

- The name is in the catalog only, not in the session folder's
  manifest or the recording file. A session folder copied to another
  computer without its catalog has no name there.
- A report written before a recording was named or renamed keeps the
  name it was written with; run the report again for the new one.
- The session folder, MP4 exports and analysis folders are still named
  by the session key.
- The Open recording dialog still chooses a session folder by its
  session key; choosing a recording by name from a list is not built.
- The window's title shows the open recording's name once the player has
  found the recording in the catalog, which is moments after it opens.
