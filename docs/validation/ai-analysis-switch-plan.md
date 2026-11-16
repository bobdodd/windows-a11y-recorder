# Validation plan: turning AI analysis off

Superseded on 2026-11-15, before these tests were run: the switch and the
checkboxes they test were replaced by each project's models. See
[the project's models](project-models-plan.md).

These tests check the switch that turns AI analysis off
([the analysis runner](../architecture/analysis-runner.md), "Turning AI
analysis off"): the setting "Use AI in analysis" in the Analysis menu,
the same setting as "Use AI in checks" in the Checks tab, the checkbox
"Use AI in this report" in the Analysis tab, and the worker
refusing to ask a model while either is off.

What was checked before these tests, in the Linux build environment: the
solution builds with no warnings; the setting is read and written, a
task keeps whether it may use AI, a task queued with AI off or run while
the setting is off asks no model and reuses no answer, and a task that
uses no model still runs. The player's menu and checkbox were not seen
there; these tests do that.

Use a phone recording you have made an Android report of before. Only
test 6 uses a model, and only for as long as it takes to turn the
setting off.

## Test 1: on by default

1. Build and install with the usual script, start the recorder, and open
   the phone recording.
2. Open the "Analysis" menu, and take a screenshot of the whole screen.
3. Close the menu, choose "Show the Analysis tab" from it, and take a
   screenshot of the whole screen.

**On screen:**
- In the menu, "Use AI in analysis" has a check mark, below "Limit the
  report to the browser while it is in front".
- In the Analysis tab, "Use AI in this report" is checked, after the
  "Profile:" box.
- "Queue summary" and "Queue Android summary" can be chosen.
- No horizontal scrollbar.

Keep the recording open for test 2.

## Test 2: off greys out the summaries

1. In the "Analysis" menu, choose "Use AI in analysis".
2. Look at the Analysis tab, and take a screenshot of the whole screen.
3. Open the "Analysis" menu, and take a screenshot of the whole screen.

**On screen:**
- In the Analysis tab, the checkbox is unchecked and greyed out, and
  reads "Use AI in this report (AI analysis is off in the Analysis
  menu)".
- "Queue summary" and "Queue Android summary" are greyed out; "Queue
  Android report" can be chosen.
- In the menu, "Use AI in analysis" has no check mark; "Summarise this
  recording", "Add vision to the summary" and "Android summary, with the
  chosen profile" are greyed out; "Android report, with no model" can be
  chosen.
- No horizontal scrollbar.

Leave the setting off for test 3.

## Test 3: an Android report with AI off

1. In the Analysis tab, choose "Queue Android report".
2. Wait until its row says "Finished", and take a screenshot of the row.
3. Choose "Open in Word" on the row, and look at the document's first
   page.

**On screen:**
- The row reads "Profile no profile. Without AI. Queued", then the time.
- The document does not say that AI is used in generating it.

## Test 4: the setting is kept

1. Close the recorder, start it again, and open the same recording.
2. Open the "Analysis" menu, and take a screenshot of the whole screen.
3. Choose "Use AI in analysis", and look at the Analysis tab.

**On screen:**
- After step 2, "Use AI in analysis" has no check mark.
- After step 3, "Use AI in this report" is checked and can be changed,
  and "Queue summary" can be chosen again.

## Test 5: one report without AI

The setting is on, as test 4 left it.

1. In the Analysis tab, uncheck "Use AI in this report", and take a
   screenshot of the whole screen.
2. Choose "Queue Android report", and take a screenshot of the whole
   screen.
3. When its row says "Finished", choose "Run again" on it, and take a
   screenshot of the newest row.

**On screen:**
- After step 1, "Queue summary" and "Queue Android summary" are greyed
  out.
- After step 2, the new row says "Without AI.", and "Use AI in this
  report" is checked again, with "Queue summary" available.
- After step 3, the newest row also says "Without AI.".

## Test 6: turning it off stops a summary already queued

This uses your local model's profile. The setting is on.

1. In the Analysis tab, choose the local profile in "Profile:", and
   choose "Queue Android summary".
2. While its row says "Queued" or "Running", choose "Use AI in analysis"
   in the "Analysis" menu to turn it off.
3. When the row says "Failed", choose "Why it failed", and take a
   screenshot of the whole screen.
4. Choose "Use AI in analysis" again, to turn it back on.

**On screen:**
- The reason ends "AI analysis is off in the analysis settings, so no
  model was asked."

## Test 7: the same switch in the Checks tab

The setting is on.

1. With the phone recording open, choose the "Checks" tab, then the
   button on the tab that opens Checks in its own window.
2. In the Checks window, uncheck "Use AI in checks", and take a
   screenshot of the whole screen.
3. Open the "Analysis" menu in the main window, and take a screenshot of
   the whole screen.
4. Choose "Use AI in analysis" in the menu, and look at the Checks
   window.

**On screen:**
- After step 1, "Use AI in checks" is checked, on the row with "Audit
  level:" and "Phone turned for:", which wrap if the window is narrow.
- After step 3, "Use AI in analysis" has no check mark, and the Analysis
  tab's checkbox reads "Use AI in this report (AI analysis is off in the
  Analysis menu)".
- After step 4, "Use AI in checks" is checked again.
- No horizontal scrollbar.
