# Design review: what the summary with vision asks of the local model

Date: 2026-10-18. Status: proposal. Nothing in the recorder has been
changed by this review.

The owner asked, after three full runs in a row failed at a layer step:
"Are we asking too much of this local 4 bit model?", and then for this
write-up, step by step, of what each step is asked and which rules can move
to the recorder.

## Summary

As built, yes. The analysis asks one model answer to obey about ten
wording rules in every statement at once, and fails the answer, and after
four attempts the whole task, for one statement that breaks one rule. On
the target machine the model's prose and its use of structure are good,
and its exact obedience to free-text rules is not: at temperature 0, 2 of
21 statements in the trials broke a rule; at temperature 0.7, far more.
With zero tolerance, failures compound over 17 checked steps, and a full
run is very likely to fail somewhere.

The evidence points to one direction: take each rule out of the
instructions and put it into the answer's form or the recorder, so that
the model is asked only for what a model must do: describe, connect, and
interpret. Where a rule has to stay with the model, ask the model to
revise the statements that break it rather than to write the whole answer
again at a higher temperature, which makes more faults, not fewer.

Whether a different or higher-precision model would also help is unknown;
it is not measured. Proposal 6 measures it.

## The model and the machine

- **Model:** Qwen3.6-35B-A3B, file Qwen3.6-35B-A3B-UD-Q4_K_M.gguf,
  quantization UD-Q4_K_M, as Appendix G.2 of the 88464e7 report gives.
- **Its size in use:** a mixture-of-experts model of 35 billion parameters,
  of which about 3 billion are active for each token, 8 routed experts and
  1 shared of 256
  ([Qwen model card](https://huggingface.co/Qwen/Qwen3.6-35B-A3B);
  [NVIDIA NGC](https://catalog.ngc.nvidia.com/orgs/nim/teams/qwen/models/qwen3.6-35b-a3b)).
- **Server:** llama.cpp build b11607-ff026f8c0, on the target PC (Ryzen 9
  3900X, RTX 2060 with 6 GB, 32 GB of memory).
- **Speed, measured:** the trial of moment 6's layer 2 read 11,312 prompt
  tokens in 56.8 s and wrote 1,313 tokens in 48.4 s, 105.2 s in all.
- **Endpoints:** the recorder sends requests only to this PC or the local
  network (`EndpointPolicy`), so any model measured must run locally.

## What the runs and trials measured

### The full runs

| Run | Step that failed | What every attempt did |
| --- | --- | --- |
| 88464e7, before the wording check | none: all 31 steps accepted | The report leaked K numbers and said NVDA announced things (report-fixes-2026-10-18.md); nothing refused it. |
| 2edb728 | visit 1, moment 6, layer 2 | Wrote "which elaborates on K52" in the text and cited only the frame, 4 of 4. |
| 4048f30 | visit 1, moment 2, layer 2 | Wrote "described in K34 and K35" in the text, 3 of 4; the fourth, "stopping NVDA speech". |

### The trials of 27c9b2b

Layer templates version 4 give an elaboration an "about" field, first,
held by the answer's form to the input's K numbers. Each failed step was
tried at temperature 0, then at 0.7 with seeds 1 to 3, as recovery does
(trial files in C:\Users\Public\Downloads\trials-27c9b2b on the target
PC). "Faults" counts the rule breaks the check found; a statement may have
more than one.

| Step | Sampling | Statements | Faults | Kind of fault |
| --- | --- | --- | --- | --- |
| moment 2, layer 2 | temperature 0 | 6 | 1 | NVDA speech stopped |
| moment 2, layer 2 | seed 1 | 7 | 1 | NVDA speech stopped |
| moment 2, layer 2 | seed 2 | 7 | 2 | NVDA speech stopped |
| moment 2, layer 2 | seed 3 | 7 | 1 | NVDA speech stopped |
| moment 6, layer 2 | temperature 0 | 15 | 1 | a reference outside brackets, "at E20" |
| moment 6, layer 2 | seed 1 | 26 | 13 | K numbers and references in the text |
| moment 6, layer 2 | seed 2 | 11 | 4 | K numbers in the text |
| moment 6, layer 2 | seed 3 | 10 | 1 | a K number in the text |

What this shows:

- **Structure works.** At temperature 0 both steps named their statement
  in "about" and wrote no K number in the text: 7 of 7 elaborations at
  moment 6. Asked instead, in words, to put the number in the evidence,
  every attempt of two runs had failed.
- **One input fact caused every fault at moment 2.** The recorder's own
  fact for the click reads "NVDA stops speech, inferred"
  (`MouseActivity`); the model kept the claim and dropped "inferred" in
  all 4 answers.
- **Recovery at temperature 0.7 makes more faults.** At moment 6, 1 fault
  at temperature 0 against 13, 4, and 1 at the seeds; the seeds' answers
  also differ in length, from 10 to 26 statements.
- **The model repeats itself across sections.** The moment 6 answer at
  temperature 0 gives the eight program icons, the call to action, and the
  volunteer image both as elaborations and again as new statements, which
  the instruction "Do not repeat what the knowledge already says" forbids
  and no check catches.

## Why zero tolerance fails

If each statement breaks a rule with probability \(p\), an answer of \(n\)
statements passes with probability \((1-p)^n\). The trials at temperature
0 give about \(p = 2/21 \approx 0.10\), a small sample. Then:

- an answer of 15 statements passes about 21 percent of the time;
- with the higher fault rate of recovery at 0.7, its three seeds are
  unlikely to do better;
- and a task has 17 checked steps on the cnib.ca recording (10 layer 2, 3
  layer 3, the page chapter, the story, the opening, and the executive
  summary), each of which must pass.

Even at \(p = 0.05\), an answer of 15 passes 46 percent of the time, the
four attempts all fail about 8 percent of the time, and 17 such steps
leave a task about a 23 percent chance of finishing. The three failed runs
in a row are what this predicts.

Every rule added to the check lowers the chance further, and every rule
not checked is obeyed only as often as the model happens to: the 88464e7
report, with no check, passed every step and broke the rules in its text.
Neither adding checks nor removing them is a solution; moving the rules is.

## Each step and what it asks

From the 88464e7 run, the last that finished (its Appendix G.6), with the
instructions of the current templates. Inputs have grown since: moment 6's
layer 2 was 9,578 prompt tokens then and 11,312 in the trial, with the
visit's timeline added.

| Step | Per task | Prompt tokens | Output tokens | Instruction words | "Never", "not", "only" and the like | Checks |
| --- | --- | --- | --- | --- | --- | --- |
| Moment, the captured frame | 10 | 2,778 to 3,012 | 585 to 864 | 384 | 16 | answer's form |
| Page tiles, a group | 5 sent | 4,719 to 5,127 | 648 to 1,083 | 438 | 19 | answer's form |
| Layer 2, what the frame adds | 10 | 2,283 to 12,062 | 332 to 1,666 | 745 | 26 | form, wording |
| Layer 3, what the page adds | 3 sent | 3,916 to 6,226 | 439 to 4,975 | 844 | 33 | form, wording |
| Page chapter | 1 | 12,520 | 1,825 | 802 | 33 | form, wording |
| Visit's story | 1 | 17,261 | 2,526 | 1,186 | 38 | form, wording |
| Story opening | 1 | 2,489 | 463 | 831 | 30 | form, wording |
| Executive summary | 1 | 6,638 | 1,452 | 993 | 43 | form, wording |

The steps that describe a picture have the fewest rules and have passed
reliably. The steps that add to or write from the knowledge carry two to
three times the rules, and layer 2's input grows with every moment, as the
whole visit's knowledge is given again: from 2,283 tokens at moment 1 to
12,062 at moment 10.

## The rules and where each can live

| Rule | Steps | Kept now by | Broken in the evidence | Where it can live |
| --- | --- | --- | --- | --- |
| The answer's sections, statements, kinds, and evidence lists | all | answer's form, ClaimCheck | not in these runs | stays in the form |
| Cite only references in the input | all | ClaimCheck, after the answer | not in these runs | the form: each evidence item one of the input's references, as "about" is |
| Name the statement an addition is about | layers | the form, since 27c9b2b | written in the text in 7 of the 8 attempts before it | done |
| No K number in the text | layers | wording check | 18 faults, most of them K numbers, in the 3 seed answers at moment 6 | the "about" field, and revision (proposal 2) |
| No E reference in the text | layers, report steps | the recorder takes out cited references in brackets and counts them in G.1; the check fails the rest | "at E20"; "E95, E96" | the recorder for brackets, as now; revision for the rest |
| NVDA only as inferred | layers, report steps | wording check | every fault at moment 2, from the recorder's own fact | the recorder: its facts in the allowed form, and none given where not needed (proposal 1) |
| What the participant saw, only if they work by sight | moment, layers, story | instructions only | not checked | the recorder gives the capability as a fact the step needs or not; a check is possible but adds to the failures |
| The look first, and what it is built as | layers, page chapter, story | instructions only | the 88464e7 report, before the rule | stays with the model: only a model can say how something looks; the recorder gives each element's role beside its name, as the page facts now do |
| Recorded order, no causes inferred from order | report steps | instructions only | the 88464e7 report (report-fixes part 2) | the recorder, for the numbered steps (proposal 4) |
| Counts as the facts count | report steps | instructions only | the 88464e7 report (report-fixes part 2) | the recorder, for the numbered steps (proposal 4) |
| The recording's ending is the auditor stopping it | report steps | instructions only | the 88464e7 report | the recorder writes the ending (proposal 4) |
| What a setting at its default shows | executive summary | instructions only | the 88464e7 report | the recorder lists the settings not at their defaults; the model interprets only those (proposal 4) |
| What the recreation lacks is not the experience | report steps | instructions only | the 88464e7 report | the input: leave the recreation's gaps out of the report steps' inputs, and the recorder gives them in Appendix G (proposal 5) |
| Do not repeat the knowledge | layers | instructions only | the moment 6 trial at temperature 0 | the input and the answer's size (proposal 5) |
| No Markdown | all | instructions only | not in these runs | the recorder, which escapes the text when it writes the report |
| One topic a paragraph; the forms; no headings | report steps | the form, partly | not in these runs | stays |

## Proposals

In the order recommended: each is measured by trial before the next.

### 1. The recorder words its NVDA facts as inferences, and gives them only where needed

- **What changes:** the click fact becomes, for example, "NVDA treats a
  left or right click as a command to stop speech, inferred; whether NVDA
  was speaking is not recorded". Layer 2, which adds what the frame shows,
  is given no inference of what NVDA would do or had to announce; the
  story and the page chapter keep them.
- **Why:** every fault at moment 2 came from that one fact, in all four
  answers. A step cannot misstate what it is not given.
- **Cost:** the player's mouse panel shows the same text, and would change
  with it.
- **Decision needed:** the wording, and whether the player changes too.
- **Test:** trial of moment 2's layer 2; it should pass at temperature 0.

### 2. Revision before resampling

- **What changes:** where an answer breaks a wording rule, the worker sends
  one more request at temperature 0, with the same instructions and input,
  the answer, and each fault with its statement, asking for the answer
  again with only those statements rewritten. The revised answer is
  checked in full, as any answer is. If it passes it is accepted, and
  Appendix G.6 lists the step as revised, with each fault; if not, the
  seeds follow as now.
- **Why:** at temperature 0 the faults are few and in single statements:
  1 in 15 at moment 6. Writing the whole answer again at 0.7 throws away
  the 14 good statements and makes more faults; revising keeps them.
- **How it differs from what the owner ruled out:** nothing is left out
  and the recorder edits no text. The model rewrites its own statements,
  under the same checks, repeatably, and the report says so.
- **Decision needed:** this changes the recovery rule, "first valid at
  temperature 0.7, seeds 1 to 3", by putting one revision before it.
- **Test:** trial of moment 6's layer 2, revising the temperature 0 answer.

### 3. Every evidence item one of the input's references, in the form

- **What changes:** the answer's form holds each evidence item to the
  input's references, as it holds "about" to its K numbers.
- **Why:** a reference not in the input then cannot be written, rather
  than being refused after the answer.
- **Cost:** the form grows with the input's references, up to several
  hundred in the story; whether the server's grammar handles that size
  well is unknown until tried.
- **Decision needed:** none beyond the trial.

### 4. The recorder writes the report's mechanical parts

- **What changes:** in the visit's story, the numbered steps of
  "actions" are written by the recorder from the visit in time order: each
  input, counted as recorded, with what the recorder found after it, and
  the recording's ending. The model writes "arriving" and "experience",
  and may refer to the steps. In the executive summary, the recorder lists
  the settings not at their defaults; the model says what they mean.
- **Why:** the order, the counts, the causes, and the ending are rules the
  model is now asked to obey in prose, with no check; the recorder has
  them exactly.
- **Cost:** the steps read as the recorder's lines, plainer than a model's
  prose.
- **Decided,** 2026-10-18: "Of the proposals, the only one I want you to
  act on for now is (4)." Built as described in `recording-summary.md`,
  "The recorder writes the steps and the settings list (built)". The other
  proposals are not acted on for now.

### 5. Smaller inputs and answers for the layers

- **What changes, to be measured first:** layer 2 is given the visit's
  layer 1 and the layer 2 and 3 statements of the moments before that are
  about what this frame shows, rather than the whole knowledge; and each
  section is asked for at most a set number of statements.
- **Why:** layer 2's input grows to 12,062 tokens by moment 10, and the
  longest answer at moment 6 had 26 statements and 13 faults. Fewer
  statements give fewer chances to break a rule, and a shorter input is
  read faster.
- **Cost:** with less of the knowledge in view, the model may repeat more
  of it; how much is unknown.
- **Decision needed:** the limit, after a trial.

### 6. Measure the model

- **What changes:** nothing in the recorder. The same set of trial steps is
  run against other local models: a higher-precision file of the same
  model, as far as the PC's 32 GB of memory and 6 GB graphics card allow,
  and a dense model of similar size.
- **Why:** the trials so far say what this model does, not whether another
  would do better. If another breaks the rules far less often, the model
  is part of the limit; if not, the design is.
- **Decision needed:** which models, and whether to wait for a memory
  upgrade, which would allow a larger file.

## What this review does not propose

- **Leaving out the statements that break a rule.** The owner ruled this
  out on 2026-10-18: "I don't want the omission option, I want a real
  solution to this."
- **A grammar that forbids reference patterns in the text.** Held to a
  form that forbids "K" followed by a digit, the model would still begin
  "K" and have to end it some other way, which passes the check and spoils
  the sentence.
- **More seeds.** Each sample at 0.7 made as many or more faults than the
  answer at temperature 0.
- **Loosening a rule without saying so.** Any rule relaxed is the owner's
  decision and is recorded.

## Testing before a full run

A fixed set of trial steps, run before each full run with the script of
2026-10-18 (Run-LayerTrials), and a full run only when every step passes
at temperature 0:

- the steps that have failed: visit 1, moment 2, layer 2; visit 1, moment
  6, layer 2;
- a layer 3 step, and the last layer 2, moment 10, whose input is the
  largest;
- and, once proposal 4 is decided, the story, which needs the steps before
  it accepted in a task.

Each trial gives its statements and faults, so that the rate of each rule
can be followed from build to build.

## Decisions needed

1. Proposal 1: the click fact's wording, and whether the player's mouse
   panel changes with it.
2. Proposal 2: one revision at temperature 0 before the seeds.
3. Proposal 4: decided and built, 2026-10-18.
4. Proposal 6: which models to measure.

## Follow-on review

`intermediate-outputs-review-2026-10-19.md` reviews the steps whose
answers only a later model reads, at the owner's question of 2026-10-18,
with proposals A to D. Its proposal A overlaps proposals 1 and 2 here, and
its proposal D overlaps proposal 5.

## Sources

- Trial files: C:\Users\Public\Downloads\trials-27c9b2b on the target PC,
  build 27c9b2b, 2026-10-18.
- Failed runs: tasks 01a1517e-88f1-7967-9aa5-e07f5214aafc (2edb728) and
  01a151e0-bbd5-73a1-b41b-957901c5674f (4048f30), each folder's
  failed-step-output.txt.
- The 88464e7 report: Appendix G.2 for the model, G.6 for each step's
  tokens.
- The templates: src/Recorder.Analysis/Summary/RecordingSummaryVisionKind.cs;
  the check: src/Recorder.Analysis/WordingCheck.cs; the click fact:
  src/Recorder.Session/MouseActivity.cs.
- The model:
  [Qwen3.6-35B-A3B model card](https://huggingface.co/Qwen/Qwen3.6-35B-A3B)
  and [NVIDIA NGC](https://catalog.ngc.nvidia.com/orgs/nim/teams/qwen/models/qwen3.6-35b-a3b).
- Earlier decisions: report-fixes-2026-10-18.md; analysis-runner.md,
  "Answers whose wording breaks the report's rules".
