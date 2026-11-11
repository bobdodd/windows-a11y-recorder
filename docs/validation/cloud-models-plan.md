# Validation plan: Claude profiles through the Anthropic API

These tests are step 3 of [cloud models](../architecture/cloud-models.md):
adding a Claude profile with your Anthropic API key, checking it, and
summarising a recording with Claude Opus 5.5 and then Claude Sonnet 5.5,
on the development PC. Not yet run.

What was checked before these tests, in the Linux build environment: the
solution builds with no warnings, and 2,250 tests pass, with 1 skipped
and the 10 that need Windows failing there as before. The request to
Anthropic was tested only against a fake server in those tests; nothing
has been sent to Anthropic. Tests 3 to 8 do that.

Tests 3, 4, 6 and 8 send to Anthropic and are charged to your account.
The check in test 3 is one short question. The cost of a summary is not
known before the first one runs; its row on the Analysis tab shows the
estimate as each attempt ends, and its "Cancel" button stops it.

Paste the key only into the recorder's key box, not into this chat.

## Test 1: build, install, and open "Add a Claude profile" (development PC)

1. Close the recorder. Open PowerShell and run:

   ```powershell
   Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass -Force
   $repo = Join-Path $env:USERPROFILE "windows-a11y-recorder"
   git -C $repo pull --ff-only
   & (Join-Path $repo "scripts\Build-Installer.ps1")
   ```

2. Run the newest installer in `artifacts\installer\output` and go
   through its pages. On the last page, uncheck "Start the recorder and
   set up the analysis models", then choose Finish.
3. Start the recorder from the Start menu, and in the "Analysis" menu
   choose "Model profiles...".
4. In the "Model profiles" window, choose "Add a Claude profile...", and
   take a screenshot of the whole screen.

**On screen:**
- The build ends with `INSTALLER BUILT`.
- A window titled "Add a Claude profile", starting "A task queued with a
  Claude profile sends the recording's facts and pictures to Anthropic".
- "Model:" shows "Claude Opus 5.5 (claude-opus-5-5)", "Model ID:"
  `claude-opus-5-5`, and "Profile name:" "Claude Opus 5.5".
- Under them: "Prices for the cost estimate: US$4.00 per million input
  tokens and US$20.00 per million output tokens, Anthropic's prices of
  2026-11-11."
- "Effort:" shows "The model's default", and "Output limit, tokens:"
  32000.
- "Anthropic API key:" is an empty box, and the buttons are "Add the
  profile" and "Cancel".
- No horizontal scrollbar.

Send the screenshot, and leave the window open for test 2.

## Test 2: add the Opus 5.5 profile

The "Add a Claude profile" window is open as test 1 left it.

1. Copy your Anthropic API key.
2. Click in the "Anthropic API key:" box and press Ctrl+V.
3. Choose "Add the profile", and take a screenshot of the whole screen.

**On screen:**
- In the key box, before step 3, dots and not the key.
- The "Add a Claude profile" window has closed.
- In the "Model profiles" window, the selected profile in the list is
  "Claude Opus 5.5 (sends to Anthropic): takes text and image, model
  claude-opus-5-5".
- Under the list: "Sends to Anthropic: model "claude-opus-5-5" at
  https://api.anthropic.com/, with the recording's facts and pictures for
  each task queued with it." and "An API key is saved, protected for this
  Windows user."
- The fields are "Output limit, tokens:" (32000), "Effort:" ("The model's default"),
  "Input price, US$ per million tokens:" (4), "Output price, US$ per
  million tokens:" (20), and an empty "New API key, only to replace the
  saved one:". There is no temperature, seed or server arguments field.
- The status line: "The profile "Claude Opus 5.5" was added. Choose Check
  the profile to send Anthropic one short question with it."
- "Default for text:" and "Default for images:" are the profiles they
  were before, not Claude Opus 5.5.

Send the screenshot.

## Test 3: check the profile

The "Model profiles" window is open with "Claude Opus 5.5 (sends to
Anthropic)" selected, as test 2 left it.

1. Choose "Check the profile".
2. When the status line no longer reads "Asking Anthropic with the saved
   settings of "Claude Opus 5.5"...", take a screenshot of the whole
   screen.
3. Choose "Close".

**On screen:**
- The status line starts "Anthropic answered with model
  claude-opus-5-5," then gives the input and output tokens, "about
  US$" and an amount with four decimals, "stop reason end_turn",
  "request req_" and an ID, and ends "The answer: ready" (in either
  case, with or without a full stop).
- If the stop reason is "max_tokens" instead, the model's thinking used
  the check's output limit of 1,024 tokens; send the screenshot, as the
  check is then to be changed.
- If it reads "The check of "Claude Opus 5.5" failed:" instead, the rest
  of the line is Anthropic's error; stop here and send it.

Send the screenshot.

## Test 4: summarise a recording with Opus 5.5

1. Choose "Open recording..." and open a recording you have already
   summarised with Qwen3.6.
2. In the "Analysis" menu, choose "Show the Analysis tab". Open the
   "Profile:" list, and take a screenshot of the whole screen with the
   list open.
3. Choose "Claude Opus 5.5 (sends to Anthropic)" in the list, then
   choose "Queue summary".
4. When the new "Recording summary" row has a line starting "Sent to
   Anthropic:", take a screenshot of the whole screen.
5. When that row reads "Recording summary: Finished, unread" or
   "Recording summary: Failed, unread", take a screenshot of the whole
   screen.

**On screen:**
- In step 2, the tab lists the earlier "Recording summary" with "Profile"
  and the Qwen3.6 profile's name.
- In the open list, "Claude Opus 5.5 (sends to Anthropic)", and no
  "(sends to Anthropic)" after any other profile.
- After step 3, the status line: "Recording summary queued with Claude
  Opus 5.5 (sends to Anthropic)." The new row reads "Profile Claude Opus
  5.5. Queued" and the time.
- In step 4, the line reads "Sent to Anthropic: 1 attempt" (or more),
  then the input and output tokens, "about US$" and an amount, "at the
  profile's prices. Anthropic's bill is the record of what was charged."
- In step 5, if the row is "Failed", its "Why:" line gives the reason;
  send that screenshot and stop here.

Send the three screenshots.

## Test 5: the report says who wrote it

The recording from test 4 is open, with its new "Recording summary" row
finished.

1. In that row, choose "Open result".
2. In the report's window, scroll to "How this was made", and take a
   screenshot of the whole screen.
3. Send the file named at the top of the report's window.

**On screen:**
- "Model profile: "Claude Opus 5.5", model claude-opus-5-5, sent to
  Anthropic."
- "Model as Anthropic named it in its answers:" and a model name, not
  "not given".
- "Model file: none; the model is served by Anthropic, so it has no
  SHA-256 here, and its answers cannot be repeated on request."

Send the screenshot and the file.

## Test 6: running it again reuses the answers

The recording from test 4 is open on the Analysis tab.

1. In the row of the Opus 5.5 summary from test 4, choose "Run again".
2. When the new row reads "Recording summary: Finished, unread", take a
   screenshot of the whole screen.

**On screen:**
- The new row has no "Sent to Anthropic:" line, since every step reused
  the answer of test 4 and nothing was sent.
- The row of test 4 still has its "Sent to Anthropic:" line, unchanged.

Send the screenshot.

## Test 7: add the Sonnet 5.5 profile

1. In the "Analysis" menu, choose "Model profiles...", then "Add a Claude
   profile...".
2. In "Model:", choose "Claude Sonnet 5.5 (claude-sonnet-5-5)".
3. Copy your Anthropic API key, click in the "Anthropic API key:" box and
   press Ctrl+V.
4. Choose "Add the profile", take a screenshot of the whole screen, then
   choose "Close".

**On screen:**
- After step 2, "Profile name:" reads "Claude Sonnet 5.5" and the prices
  line "US$2.00 per million input tokens and US$10.00 per million output
  tokens".
- After step 4, the selected profile in the list is "Claude Sonnet 5.5
  (sends to Anthropic): takes text and image, model claude-sonnet-5-5",
  and the status line "The profile "Claude Sonnet 5.5" was added."
  followed by the same sentence as in test 2.

Send the screenshot.

## Test 8: summarise the same recording with Sonnet 5.5

The recording from test 4 is open on the Analysis tab.

1. In the "Profile:" list, choose "Claude Sonnet 5.5 (sends to
   Anthropic)", then choose "Queue summary".
2. When the new row reads "Recording summary: Finished, unread" or
   "Recording summary: Failed, unread", take a screenshot of the whole
   screen.
3. If it finished, choose "Open result" and send the file named at the
   top of the report's window.

**On screen:**
- The new row reads "Profile Claude Sonnet 5.5." and has its own "Sent to
  Anthropic:" line with "about US$" and an amount.
- If it failed, its "Why:" line gives the reason.

Send the screenshot and the file. The reports of tests 5 and 8 are then
compared with the Qwen3.6 report of the same recording.
