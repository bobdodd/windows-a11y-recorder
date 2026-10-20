# Trials of the report's steps on other models (2026-10-20)

The executive summary of the run of f06412e failed on its wording in all
four answers. The owner: "I fear the real problem is the model", and then
"Maybe we need to experiment with a different model for the report". The
trials of ad487b2 (`Run-ModelTrials-ad487b2.ps1`) gave the story of visit 1
and the executive summary of that task (01a1566e-3cd9-7651-84e9-936e9059764f)
to four models or modes, at temperature 0 and at 0.7 with seeds 1 to 3:
Qwen3.6-35B-A3B UD-Q4_K_M, the task's own profile, with thinking off as the
run was, and on; and Gemma 4 26B A4B UD-Q4_K_M with thinking off, and on.
Each trial at temperature 0 with the task's own settings sent the same
request as the task's first attempt (the same SHA-256), and failed, as that
attempt had.

## Passes

| Model or mode | Story of visit 1 | Executive summary | Time of the executive summary |
| --- | --- | --- | --- |
| Qwen3.6, thinking off | 0 of 4 | 0 of 4 | 114 to 134 s |
| Qwen3.6, thinking on | 0 of 4 | 3 of 4 | 417 to 646 s |
| Gemma 4, thinking off | 0 of 4 | 1 of 4 | 132 to 167 s |
| Gemma 4, thinking on | 0 of 4 | 2 of 4 | 555 to 847 s |

- **Without thinking**, the failures are the wording ("NVDA announcing",
  "stopping NVDA speech"), claims that cite nothing, a reference written in
  the text and not cited, and a citation of "1", which was not in the
  input.
- **With thinking, the story did not fit the context.** Its prompt is
  25,405 tokens (Qwen) and 25,440 (Gemma); with the thinking, 7 of the 8
  answers reached the server's context of 32,768 tokens and stopped. The
  trial file says "stopped at the output limit of 16384 tokens", which is
  wrong: the output limit was not reached. The one that finished failed on
  its wording.
- **The executive summary at temperature 0 with thinking** reached the
  output limit of 16,384 tokens with both models, after about 56,000
  characters of thinking.

## What the passes say

Every passing answer keeps the recorder's "inferring that any NVDA speech
was stopped" in its sentences on the steps, and "inferring that NVDA had
to announce". But each, bar one, says the same elsewhere in words the
check does not list:

- Qwen3.6, thinking on, seed 1: "mouse interactions halted the audio
  stream", "repeated mouse clicks terminated the audio stream", "unintended
  speech termination".
- Seed 2: "halted the screen reader's speech", "halted the auditory flow".
- Seed 3: "repeated mouse clicks halted screen reader output".
- Gemma 4, thinking on: "mouse inputs that stopped potential assistive
  technology speech", "the use of the mouse, which inferred that any NVDA
  speech was stopped".
- Gemma 4, thinking off, seed 3: "stopped any inferred NVDA speech", which
  keeps the inference.

Qwen's thinking checks the rule by its list of words: "never say that NVDA
announced, said, read, or spoke anything ... -> Checked." The rule and the
check are both lists of words, and the model keeps to the list and not to
what it is for.

The passing answers also retell the steps one by one, up to 57 statements,
in the recorder's own words ("kept, screen reader running: NVDA; inferring
the command: next page in browse mode"), where the executive summary is to
interpret.

## Two faults the trials found

- **The check refused a sound answer twice.** Gemma 4, thinking off, wrote
  "resulted in the inference that any NVDA speech was stopped" in the story
  and in the executive summary; the check takes "inferred", "inferring",
  and "infers", but not "inference".
- **An accepted step carries an error from the recorder's own wording.**
  For a page load the recorder writes "on a page load NVDA reads the page's
  title and, with say all on page load, reads on from the top"
  (`NvdaAnnouncements.cs`), a condition it does not settle, although it
  recorded `autoSayAllOnPageLoad: False` [E157]. The story's opening,
  accepted in the task, wrote "with say all on page load enabled, began
  reading from the top", and the executive summary of Qwen3.6, thinking on,
  seed 2 repeated it. Gemma 4, thinking off, seed 3 wrote the opposite,
  from E157.
