# Analysis Runner: Validation, 2026-10-14

The first system test on the target machine of the
[analysis runner](../architecture/analysis-runner.md), steps 1 to 3 of its
"Build order": the runner, the [recording summary](../architecture/recording-summary.md)'s
stage 1, and the player's analysis controls. Run by the owner with the
mouse alone; the reports and the catalog's attempts read from the target
machine afterwards.

## Setup

- Target machine: RTX 2060 with 6 GB, 32 GB of memory.
- Model server: llama.cpp build b11607-ff026f8c0, started by the worker in
  router mode on port 8091.
- Model: Qwen3.6-35B-A3B, file `Qwen3.6-35B-A3B-UD-Q4_K_M.gguf`, SHA-256
  ac0e2c1189e055faa36eff361580e79c5bd6f8e76bffb4ce547f167d53e31a61,
  computed from the file by the worker.
- Profile "This PC": text only, arguments `-ngl 99 -ncmoe 36 -fa on -c
  16384`, up to 8,192 output tokens, temperature 0, seed 1, thinking off.
- Recording: 20261013-173155-6672eb45f4314526936fac89c814cffd, one visit
  to https://www.cnib.ca/en of 54 s, with NVDA 2026.2 running. No
  capability description was given.
- Package: wr-f03ebf9, run from source.

## Results

The six tests, as given to the owner, all passed on package f03ebf9.

| Test | What was checked | Result |
|---|---|---|
| 1. Settings file | The Model profiles window lists "This PC" with no problems, This PC as the default for text, and no default for images | Passed |
| 2. Queue a summary | Summarise this recording shows the Analysis tab, the status line, and a Queued or Running row with the worker's line | Passed |
| 3. Finished and unread | The row reads "Finished, unread", the tab "Analysis (1 unread)", with the notice in the status bar | Passed |
| 4. Reference moves playhead | Open result shows the report window with its title; a reference button moves the playhead to its time | Passed |
| 5. Run again | A new row at the top, the finished row still below it | Passed |
| 6. Cancel | The row reads "Cancelled", with the notice in the status bar | Passed, in 3 s |

Run again gave the same report. The two reports, written at 8:49 and 9:00,
differ only in the reading and writing times in the attempts table. The
text, the references, and the token counts are the same:

| Step | Prompt tokens | Output tokens | Reading, first and second | Writing, first and second |
|---|---|---|---|---|
| visit-0001 | 2,337 | 1,254 | 15.8 s, 14.5 s | 44.5 s, 44.3 s |
| recording | 1,617 | 433 | 8.8 s, 8.8 s | 15.5 s, 15.4 s |

Each step was accepted on its first attempt. Writing ran at about 28
tokens a second (1,254 in 44.3 s; 433 in 15.4 s), as in "What the trial
showed" in the [analysis runner](../architecture/analysis-runner.md),
28.9 a second with these arguments.

## Failures found and fixed

Each was found by a run on the target machine, fixed with tests, and run
again.

1. **The worker did not start** (997fb9b): "Could not load file or
   assembly 'System.Security.Cryptography.ProtectedData'". The worker runs
   from the player's folder, and now references the same frameworks as
   the player.
2. **The model server answered 503 "Loading model"** three times (8825fb6).
   In router mode, loading a model returns before the model is ready. The
   worker now polls the model list until the model's status is "loaded",
   failing if the load fails, or after 15 minutes. The status values are
   those of the [llama.cpp server](https://github.com/ggml-org/llama.cpp/blob/master/tools/server/README.md).
3. **The first visit stopped at 2,048 output tokens three times, with the
   same 7,107 characters** (f0a1d68). The profile now allows 8,192, and the
   prompts, version 2, keep the sections apart, group repeated actions,
   and put the JSON on one line: 2,169 of the 7,098 characters had been
   indentation. At temperature 0, refused output is no longer asked for
   again; see "Decisions (2026-10-14)" in the
   [analysis runner](../architecture/analysis-runner.md).
4. **The whole recording's step was refused:** "NVDA 2026.2 was observed
   running at the start of the recording" was a claim with nothing to cite
   (f03ebf9). The fact was given to the model without an evidence
   reference; it now has one, at the start of the recording and of each
   visit. The same run's "site" section repeated the visit's description,
   so the whole recording's prompt, version 3, names what goes in each
   section.
5. **The report contradicted itself** (this commit, found in the report of
   the passing run): "No assistive technology was observed running at the
   start of the recording" under the participant's capabilities, and
   "At the start of the recording, NVDA 2026.2 was running. [E1]" under
   the session. The report looked for a line starting "Observed running",
   which since fix 4 starts with its reference. It now finds the fact by
   its evidence entry and cites it.

## The report

Observations from reading the passing run's report, for the stages to
come. None is a test failure.

- **The visit's description is close to the facts.** Its page section
  gives the landmarks, the headings in order including the empty level 1
  heading, the buttons, and the counts of links and images, all cited. Its
  activity section groups the keys ("Page Down three times, Tab three
  times, Escape twice, H once, Page Up once") and lists what NVDA had to
  announce.
- **The whole recording's sections are cautious.** "The specific
  navigation actions performed by the participant are unknown as the
  model's inference is not a fact", where the visit's description, given
  to it as inferred, does say what they were. "The intended audience is
  unknown" for a page whose purpose the visit's description gives.
- **The recorder's own window is in "Other windows"**, as "00:00:00 to
  00:00:00: Recorder.App", and in the session. The owner's decision is that
  it stays: the recorder is a genuine window in the recording. See
  "Decisions (2026-10-14)" in the
  [recording summary](../architecture/recording-summary.md).
- **The evidence list gives only the references cited**, so E3 and E7 to
  E11 are absent, as designed.

## Not shown

- A recording with more than one visit, or with a capability description.
- A network profile, or a server on another machine.
- The report from fix 5, which needs the summary run again on this
  package or later.
- Stage 2, with vision, which is not built.
