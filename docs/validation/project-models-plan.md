# Validation plan: the project's models

These tests check each project's settings choosing the model, or no
model, for report analysis, text-based checks and vision-based checks
([projects](../architecture/projects.md), "The project's models"). They
replace the tests in [turning AI analysis off](ai-analysis-switch-plan.md),
whose controls are gone.

What was checked before these tests, in the Linux build environment: the
solution builds with no warnings; a project's models are saved and read
back, and start as no model; the choices offered for each use; a task
queued with no model asks none; choosing no model in the project's
settings stops a task already queued from asking one, with no answer
reused; and model setup no longer writes defaults. The player's windows
were not seen there; these tests do that.

Use a phone recording you have made an Android report of before. Only
test 5 uses a model, and only until the project is set to no model.

## Test 1: a project starts with no model

1. Build and install with the usual script, start the recorder, and open
   the phone recording.
2. Choose "Show the Analysis tab" from the "Analysis" menu, and take a
   screenshot of the whole screen.
3. Open the "Analysis" menu, and take a screenshot of the whole screen.

**On screen:**
- In the Analysis tab, under the line about the recording's project:
  "Models, from the project's settings: report analysis, no model;
  text-based checks, no model; vision-based checks, no model."
- There is no "Profile:" box and no "Use AI in this report".
- "Queue summary" and "Queue Android summary" are greyed out; "Queue
  Android report" and "Project settings..." can be chosen.
- In the menu: no "Use AI in analysis"; "Summarise this recording",
  "Add vision to the summary" and "Android summary" are greyed out;
  "Android report" and "Android report for the project" are named without
  ", with no model".
- No horizontal scrollbar.

## Test 2: the Checks tab

1. With the phone recording open, choose the Checks tab.
2. Take a screenshot of the whole screen.

**On screen:**
- There is no "Use AI in checks".
- Above the line about the phone's apps: "Models for checks, from the
  project's settings: text-based checks, no model; vision-based checks,
  no model. No check uses a model yet."
- The line wraps; no horizontal scrollbar.

## Test 3: the project's settings window

1. Choose "Project settings..." from the "Project" menu.
2. Take a screenshot of the whole screen.
3. Open the list under "Model for vision-based checks", and take a
   screenshot of the whole screen.
4. Press Esc to close the list, then choose "Cancel".

**On screen:**
- The window's title is "Settings of the project " and the project's
  name.
- It has three lists: "Model for report analysis, the summaries:",
  "Model for text-based checks:" and "Model for vision-based checks,
  which must take images:", each showing "No model".
- The vision list holds "No model", then "Claude Opus 5.5 (sends to
  Anthropic)", "Claude Sonnet 5.5 (sends to Anthropic)" and "Claude
  Haiku 5.5 (sends to Anthropic)", then only the models on this PC that
  take images, such as "This PC, with images", not "This PC".
- Below the lists: "Anthropic API key, for the Claude models:", with a
  line saying whether a key is saved for this project.
- The text above the lists wraps; no horizontal scrollbar.

## Test 4: choosing a model for report analysis

1. Choose "Project settings..." in the Analysis tab.
2. In "Model for report analysis", choose "This PC", and choose "Save".
3. Take a screenshot of the whole screen.
4. Open the "Analysis" menu, and take a screenshot of the whole screen.

**On screen:**
- The Analysis tab's status line: "The settings of the project ... were
  saved. Models: report analysis, This PC; text-based checks, no model;
  vision-based checks, no model."
- The models line says "report analysis, This PC".
- "Queue summary" and "Queue Android summary" can now be chosen.
- In the menu, "Summarise this recording" and "Android summary" can be
  chosen, and "Add vision to the summary" is still greyed out, as This
  PC does not take images.

## Test 5: no model stops a task already queued

1. Choose "Queue Android summary" in the Analysis tab.
2. At once, choose "Project settings...", set "Model for report
   analysis" to "No model", and choose "Save".
3. Wait until the Android summary's row says "Failed", and take a
   screenshot of the whole screen. A step already asking the model when
   you saved finishes first.

**On screen:**
- The row's second line begins "Models: report analysis, This PC."
- Its "Why:" line: "The project's settings now choose no model for
  report analysis, so no model was asked."
- "Queue summary" and "Queue Android summary" are greyed out again.

## Test 6: each project has its own settings

1. Choose "Projects and recordings..." from the "Project" menu.
2. Select a project other than the phone recording's, and choose
   "Project settings...".
3. Take a screenshot of the whole screen, then choose "Cancel" and
   "Close".

**On screen:**
- The window's title names the project selected, not the phone
  recording's.
- Its three lists show that project's own models, "No model" unless you
  have chosen one for it.

## Test 7: Claude Haiku 5.5 with the project's key

Changed on 2026-11-15, when the Model profiles window was removed.

1. Choose "Project settings..." from the "Project" menu.
2. In "Model for report analysis", choose "Claude Haiku 5.5 (sends to
   Anthropic)". If the window says no key is saved for this project,
   paste your Anthropic API key into the key box.
3. Choose "Save", and take a screenshot of the whole screen.
4. Open the "Analysis" menu, and take a screenshot of the whole screen.

**On screen:**
- The status line names "report analysis, Claude Haiku 5.5".
- "Queue summary" and "Queue Android summary" can be chosen.
- There is no "Model profiles..." in the menu; "Set up models..." is
  there.
