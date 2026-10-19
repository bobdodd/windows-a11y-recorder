# Design review: the answers only a model reads

Date: 2026-10-19. Status: proposal C decided, built, and tried,
2026-10-19; proposal A built for its trial, 2026-10-19; proposals B and D
not decided. The one change made in the same
conversation before this review, the one-line rule removed, is recorded
under "Done alongside this review".

The owner asked, 2026-10-18: "The only report the user will see is the
final summary markdown. All other intermediate reports are for the benefit
of AI. Could they be optimized to reduce workload for the model?", and then
for this write-up: "record this as a design review so we don't lose
anything".

It follows `model-load-review-2026-10-18.md`, whose proposals 1 to 6 keep
their numbers. The proposals here are lettered A to D; where one overlaps
an earlier proposal, it says so.

## Summary

Yes. Of the summary with vision's steps, five write answers that no reader
sees: they are given only to later model steps. They are written and
checked as if they were the report: as prose in paragraphs, held to the
report's wording rules, and given again in full to every later step. The
two full runs that failed most recently both failed on wording in one of
these steps.

What they need is less: statements a later model can use, each citing
what it rests on. Four changes would ask less of the model without losing
any evidence:

- **A.** Keep the wording checks for the steps whose answers a reader sees,
  and take them off the steps whose answers only a model reads.
- **B.** The recorder writes layer 1 from its facts, in place of a model's
  restatement of them.
- **C.** The recorder lays out the knowledge more compactly; the model's
  work is unchanged.
- **D.** The steps only a model reads answer in short notes, not paragraphs
  of prose.

## Which answers a reader sees

| Step | Per task, 88464e7 run | Its answer is given to | Seen by a reader | Checks now |
| --- | --- | --- | --- | --- |
| Visit description, the text summary's visit step (`RecordingSummaryKind`), which becomes layer 1 | 1 for each visit, in the text summary task | the knowledge: every layer 2 and 3, the page chapter, the story | no | form, evidence |
| Moment, the captured frame | 10 | that moment's layer 2 | no | form, evidence |
| Page tiles, a group | 5 sent | that moment's layer 3, and later layer 3 steps where the recorder finds the same tiles | no | form, evidence |
| Layer 2, what the frame adds | 10 | the knowledge | no | form, evidence, wording |
| Layer 3, what the page adds | 3 sent | the knowledge | no | form, evidence, wording |
| Page chapter | 1 | chapter 3 | yes | form, evidence, wording |
| Visit's story | 1 | chapter 4; the opening and the executive summary | yes | form, evidence, wording |
| Story opening | 1 | 4.1 | yes | form, evidence, wording |
| Executive summary | 1 | chapter 1 | yes | form, evidence, wording |

The per-task counts are those of the 88464e7 run, as the 2026-10-18 review
gives them. Each moment's record, its frame and tiles with the steps that
describe them, is also kept in the moment index file, for lookup; it is
not part of the report.

Layer 1 comes from the text summary task, which summary with vision needs
first: the vision task takes each visit's accepted description from it, and
fails if the description was written from other references
(`RecordingSummaryVisionKind`, before the moments are described;
`Knowledge.FromRecorded`).

## What one layer request holds

Measured from the trial of visit 1, moment 6, layer 2, build 27c9b2b, at
temperature 0 (C:\Users\Public\Downloads\trials-27c9b2b on the target PC,
`visit-0001-moment-06-layer-2-t0.txt`):

| Part | Characters | Share of the input |
| --- | --- | --- |
| The input in all | 45,863 | 100% |
| The visit's knowledge so far, 133 statements | 31,431 | 69% |
| of which layer 1, 32 statements | 8,349 | 18% |
| of which layer 2, 35 statements | 8,329 | 18% |
| of which layer 3, 66 statements | 14,197 | 31% |
| of which the labels, such as "(layer 3, the web page at moment 5, elaborates K3, inferred)" | 6,525 | 14% of the input, 21% of the knowledge |
| The facts since the moment before, the frame, its description, and the scope note | 14,009 | 31% |
| The instructions, not part of the input | 4,450 | |
| The answer, at temperature 0 | 4,554 | |

The same request read 11,312 prompt tokens in 56.8 s and wrote 1,313
tokens in 48.4 s (`model-load-review-2026-10-18.md`, "The model and the
machine"). In the 88464e7 run, layer 2's input grew from 2,283 tokens at
moment 1 to 12,062 at moment 10, as the whole knowledge is given again at
every moment.

What the measurements show:

1. **The knowledge is most of every layer's input,** and it is given again,
   whole, to every later layer, the page chapter, and the story.
2. **Layer 1 is a model's restatement of facts the recorder holds,**
   labelled "inferred". For example, K3 restates the page's headings in
   order, a list the recorder already has. The story is given the visit's
   facts (`RecordingSummaryKind.VisitInput`) and also layer 1, so it reads
   them twice, once as facts and once as inferences. The page chapter is
   given the visit's changes and the knowledge, but not the visit's facts,
   so layer 1 is where it learns the page's structure.
3. **Every statement repeats ", inferred"** (133 of 133), although the
   heading before the knowledge already says "These are inferences, not
   facts".
4. **Answers spend words on what the form already says.** K126, a layer 3
   statement: "The frame shows the 'Braille Practice Fall Sessions' event
   card with its date and descriptive text, which elaborates on the content
   of the EVENTS section described in K3 and K46 by providing specific
   details about the event visible in this scroll position." Its "about"
   already names K3; the clause after "descriptive text" adds nothing. It
   also says "the frame shows" in a layer that describes the rebuilt page,
   not the frame.
5. **The two failed runs failed on wording in an answer no reader sees.**
   2edb728: visit 1, moment 6, layer 2, "which elaborates on K52" in the
   text, 4 of 4 attempts. 4048f30: visit 1, moment 2, layer 2, "described
   in K34 and K35", 3 of 4, and "stopping NVDA speech" in the fourth.

## Proposals

In the order recommended: C first, as it changes nothing the model is
asked; then A, B, and D, each measured by trial before the next.

### A. Wording checks only where a reader sees the answer

- **What changes:** layers 2 and 3 are no longer checked by
  `WordingCheck` (`ChecksWording` off for those two requests). The page
  chapter, the story, the opening, and the executive summary keep it. Their
  instructions keep the rules about what is true, and drop those about how
  the text reads:
  - **Kept:** cite only the input's references; say what the participant
    saw only if their capabilities say they work by sight; what NVDA had to
    announce is inferred, not recorded speech; a tile is a recreation, not
    the page.
  - **Dropped:** never write a K number in the text, with its example of a
    right and a wrong answer; references in the evidence only, never in
    the text; no Markdown.
  - **Evidence check:** kept on every step. Each statement's evidence must
    still be among the input's references (`ClaimCheck`), because the
    report's citations rest on it.
- **Why:** both failed runs would have passed these steps, as each failed
  only the wording check (finding 5). A K number or an "E20" in a layer's
  text harms no reader. Later steps are given the knowledge without its
  numbers, so a K number left in a statement's text points to nothing they
  hold.
- **Risk:** wording a layer writes may be copied into the report. The
  steps a reader sees keep their wording checks, so such wording is caught
  there and the step is sampled again, as now. In the 88464e7 run, that
  would have been 4 checked steps in place of 17: the page chapter, the
  story, the opening, and the executive summary, without the 10 layer 2
  and 3 layer 3 steps.
- **Disclosure:** Appendix G says which steps' wording is checked and
  which is not, and why.
- **Overlap:** with proposal 1 (the NVDA click fact) and proposal 2
  (revision before resampling): A makes both unnecessary for the layers,
  and they would still apply to the steps a reader sees.
- **Decided for a trial,** 2026-10-19: "It's defintely an improvement so
  let's push the changes and then we trial Option A". This changes the rule
  set on 2026-10-18, that layer answers are held to the report's wording.
- **Built:** the layer 2 and layer 3 requests no longer set
  `ChecksWording`. `LayerAnswer` drops `ReferencesOutOfText`, the sentence
  "never write a statement's number, such as \"K3\", in the text" with its
  right and wrong example, and "Write plain sentences without Markdown";
  it keeps `ParticipantRules`, `VisualAndSemantics`, `UnknownIsUnknown`,
  `CiteWithoutBrackets`, and "Cite only references that appear in the
  input". The layer templates are version 6. Appendix G.1 says: "The
  wording of layers 2 and 3, which no reader sees, is not checked against
  the report's rules, such as never writing that NVDA spoke; the wording of
  chapters 1, 3, and 4 is, and an answer of theirs that breaks a rule is
  refused and sampled again. Every statement of every step, layers 2 and 3
  included, cites only references given to that step."
- **What a trial shows:** a trial of a step whose wording is not checked
  says, for an answer that passes, what the wording check would have found
  ("The step's wording is not checked; the wording check would have found:
  ..."), so that the trial shows what later steps are given.
- **Test:** the trials of moment 2 and moment 6, layer 2, should be
  accepted at temperature 0; then a full run, checking that chapters 1, 3,
  and 4 pass their own wording checks.

#### The trial of A, 2026-10-19

The trials of c8384b7, with C, against those of df8d75a, with C alone, the
same two steps with the same samplings (C:\Users\Public\Downloads\trials-c8384b7
on the target PC). The last column is what the wording check, no longer
applied to the layers, would have found.

| Step, sampling | df8d75a: prompt tokens, statements, outcome | c8384b7: prompt tokens, statements, outcome | c8384b7: the wording check would have found |
| --- | --- | --- | --- |
| Moment 2, temperature 0 | 4,817; 6; passes | 4,669; 8; passes | "stopping speech" |
| Moment 2, seed 1 | 4,817; 4; passes | 4,669; 10; passes | nothing |
| Moment 2, seed 2 | 4,817; 7; fails, "stops speech" | 4,669; 8; passes | "stopped NVDA speech" |
| Moment 2, seed 3 | 4,817; 7; fails, "stop speech" | 4,669; 5; passes | K34 in the text |
| Moment 6, temperature 0 | not given; 99; fails, a loop | 9,500; 8; passes | E21 outside brackets |
| Moment 6, seed 1 | 9,648; 16; passes | 9,500; 9; passes | K127 in the text |
| Moment 6, seed 2 | 9,648; 9; fails, K19 in the text | 9,500; 15; passes | K19 in the text, and more |
| Moment 6, seed 3 | 9,648; 7; passes | 9,500; 8; passes | K19 in the text |

What the trial shows:

- **Every answer passes:** 8 of 8, against 4 of 8 with C alone and none at
  27c9b2b. Each still passes the evidence check: every statement cites only
  references given to it.
- **No loop:** moment 6 at temperature 0 gave 8 statements in 28.1 s of
  output, where with C alone it went through the knowledge one statement at
  a time and was stopped at 99 after 326.8 s. One answer cannot show that
  A is the cause.
- **Fewer prompt tokens:** 148 fewer at both moments, from the rules
  dropped, on top of C's saving.
- **What later steps would be given:** 7 of the 8 answers hold wording the
  check would have refused: the NVDA "stop speech" wording in 2, a K number
  in the text in 4, and an E reference outside brackets in 1. With the
  rule on K numbers dropped, K numbers in the text are more common: 4 of 8
  answers, against 1 of 8 at df8d75a, where the rule was in the
  instructions and the first fault stopped the check.
- **The risk this leaves:** the page chapter and the story are given the
  knowledge without its numbers, so a K number in a statement's text, such
  as "confirming the final scroll position mentioned in K19", points to
  nothing they hold, and may be copied. Their own wording check refuses
  such an answer and samples it again. Whether that makes chapters 1, 3,
  and 4 fail more is what a full run will show.

### B. The recorder writes layer 1

- **What changes:** the visit's knowledge starts with the recorder's
  facts of the visit, each with its E reference, as the visit's request in
  the text summary gives them, and labelled as recorded facts, not
  inferences. The vision task no longer needs the text summary's accepted
  visit descriptions.
- **Why:** layer 1, 32 statements and 8,349 characters at moment 6, is a
  model turning facts into inferences of the same facts (finding 2). The
  recorder has them exactly. This removes one model step for each visit,
  and every later step is given facts where it is now given inferences.
- **Cost:** the facts may be longer than the model's restatement of them;
  how much longer is unknown until measured. A layer that adds to a layer 1
  statement needs a number to name as its "about": the recorder numbers its
  fact lines as it numbers the knowledge's statements.
- **Duplication removed:** the story would be given the facts once, not as
  facts and again as layer 1.
- **Decision needed:** whether the text summary task stays as a report of
  its own; summary with vision would no longer need it.
- **Test:** a full run, as the vision task's first step changes; compare
  the inputs' sizes and the layers' answers with those of 27c9b2b.

### C. The knowledge laid out compactly

- **What changes,** in `Knowledge.Append` and `Knowledge.Label`, with no
  change to what the model is asked:
  - the statements grouped under headings by layer and moment, such as
    "Layer 3, the web page at moment 5:", in place of a label on every
    statement;
  - ", inferred" said once, in the heading before the knowledge, as it is
    now, and not on each statement;
  - an elaboration or correction kept under the statement it is about, as
    now, with "adds to K3" or "corrects K3" in place of the longer label.
- **Why:** the labels are 6,525 characters, 21% of the knowledge and 14%
  of the input at moment 6 (finding 3), and the knowledge is given again at
  every later step.
- **Risk:** none to the evidence: every statement keeps its number, its
  layer, its moment, and its references. Whether the model reads grouped
  statements as well as labelled ones is measured by the trial.
- **Decided,** 2026-10-19: "okay, let's implement C and we will then test
  the result".
- **Built:** `Knowledge.Key`, said once at the start of the knowledge;
  `Knowledge.Heading`, for a statement that adds to none ("Layer 1, the
  recorded data, page:", "Layer 2, the captured frame at moment 5:",
  "Layer 3, the web page at moment 5:"), given when it changes; and
  `Knowledge.Tag`, for one that adds to another ("(layer 3, moment 5)", or
  "(layer 2, moment 1, corrects)"), indented under it as before. The order
  of the statements, their numbers, and their references are unchanged.
  The layer templates say "under a heading that gives its layer" in place
  of "labelled with its layer", and are version 5. The page chapter and the
  story are given the knowledge in the same layout, without the numbers;
  their instructions are unchanged.
- **Expected saving:** the knowledge of the moment 6, layer 2 trial laid
  out the new way, from the same statements, is 25,426 characters in place
  of 31,431: 6,005 fewer, 19% of the knowledge and 13% of the input.
- **Test:** the trials of moment 2 and moment 6, layer 2, at temperature 0
  and at 0.7 with seeds 1 to 3, as for 27c9b2b; compare prompt tokens and
  faults. The trial takes the task's accepted answers for the steps before,
  so the knowledge holds the same statements as in the 27c9b2b trials.

#### The trial of C, 2026-10-19

The trials of 27c9b2b and df8d75a, the same two steps with the same
samplings, C:\Users\Public\Downloads\trials-27c9b2b and trials-df8d75a
on the target PC. Each takes the task's accepted answers for the steps
before it, so the knowledge holds the same statements in both; the layout,
and the layer template's sentence on it, is the difference.

| Step, sampling | 27c9b2b: prompt tokens, statements, outcome | df8d75a: prompt tokens, statements, outcome |
| --- | --- | --- |
| Moment 2, temperature 0 | 5,313; 6; fails, "NVDA's speech was stopped" | 4,817; 6; passes |
| Moment 2, seed 1 | 5,313; 7; fails, "stop speech" | 4,817; 4; passes |
| Moment 2, seed 2 | 5,313; 7; fails, "stop speech" | 4,817; 7; fails, "stops speech" |
| Moment 2, seed 3 | 5,313; 7; fails, "stop speech" | 4,817; 7; fails, "stop speech" |
| Moment 6, temperature 0 | 11,312; 15; fails, "E20" outside brackets | not given; 99; fails, a loop, stopped by the worker |
| Moment 6, seed 1 | 11,312; 26; fails, "E95, E96" outside brackets | 9,648; 16; passes |
| Moment 6, seed 2 | 11,312; 11; fails, a K number in the text | 9,648; 9; fails, a K number in the text |
| Moment 6, seed 3 | 11,312; 10; fails, a K number in the text | 9,648; 7; passes |

What the trial shows:

- **Fewer prompt tokens:** 496 fewer at moment 2 (9.3%), and 1,664 fewer
  at moment 6 (14.7%), where the knowledge is larger. The prompt took 47.3
  to 47.8 s at moment 6, in place of 56.0 to 56.8 s.
- **More answers pass:** 4 of 8, in place of none. With four samples of
  each step, this may be partly chance: every failure that remains is of a
  kind seen at 27c9b2b, the NVDA "stop speech" wording and a K number in
  the text, which proposal A, and for the first proposal 1 of the
  2026-10-18 review, are about.
- **What a task would now do with these two steps:** at moment 2, accept
  the answer at temperature 0. At moment 6, stop the answer at temperature
  0 as a loop, then take seed 1, the first valid, and disclose the
  recovery in Appendix G. Both steps failed their tasks before. A full run
  sends every step again, so its knowledge will not be the same as these
  trials'.
- **The loop at moment 6, temperature 0:** the answer did not repeat at
  once. It went through the knowledge one statement at a time, an
  elaboration about each ("The frame does not show the toggleButton
  described in K96."), and at statement 94 began to repeat "The frame
  shows the page layout after scrolling, which involved changes in
  elements." until the worker stopped it at 99, after 326.8 s. The other
  three samples of the step gave 16, 9, and 7 statements. Whether the new
  layout makes this more likely cannot be told from one answer; a limit on
  the statements of a layer, proposal 5 of the 2026-10-18 review, would
  bound it.

### D. Notes, not prose, for the answers only a model reads

- **What changes,** for the moment, the tiles, and layers 2 and 3:
  - each section a flat list of statements, not a list of paragraphs each
    a list of statements, so the answer's form is simpler;
  - each statement a short note of one fact: what is there, where, and
    what it rests on, without saying how it relates to the knowledge, which
    "about" already says (finding 4);
  - the page chapter and the story, which a reader sees, write the prose
    from those notes, as they write it now from the knowledge.
- **Why:** fewer output tokens make each answer faster, and give fewer
  chances to reach the output limit, to repeat, or to break a rule. Output
  is the slower part: 1,313 tokens took 48.4 s at moment 6, while 11,312
  prompt tokens took 56.8 s.
- **Cost:** the answers' form changes for four steps, with
  `Knowledge.Add` and `ClaimCheck`. The moment index file's descriptions
  become notes; it is for lookup, not for a reader.
- **Overlap:** with proposal 5 (smaller inputs and answers for the
  layers): D shortens each statement, proposal 5 limits how many.
- **Decision needed:** whether the moment and tile descriptions change
  too, or only the layers.
- **Test:** the trials of moment 6, layer 2, and of a layer 3 step;
  compare output tokens, statements, and faults.

## What this review does not propose

- **Merging the moment's description into layer 2.** One request would
  then both describe the picture and compare it with the knowledge, which
  asks more of each call, not less.
- **Dropping the evidence check from any step.** The report's citations
  rest on every statement citing only what it was given.
- **Leaving out statements that break a rule.** Ruled out by the owner,
  2026-10-18: "I don't want the omission option, I want a real solution to
  this." Proposal A does not leave anything out: it stops checking the
  wording of text no reader sees, and says so in Appendix G.
- **Loosening a rule without saying so.** Any rule taken off a step is the
  owner's decision, recorded here and disclosed in the report.

## Done alongside this review

- **The one-line rule removed,** 2026-10-18, build 6a30bcb. "Write the
  JSON on one line, with no indentation and no line breaks." was in the
  text summary's visit and recording templates and in the moment's and the
  tiles' templates. Every answer ignored it, as the server's grammar for the
  schema allows line breaks and indentation (`recording-summary.md`,
  "Answers on one line, held by a grammar of the recorder's own"). The
  owner: "If it is not needed then it should go. The cost of one run here
  to fix the data is essentially zero cost." Those templates are now
  version 5.
- **The JSON given to a model,** 2026-10-18, build 04708c9: every request
  was already written compact and its inputs are lines of text; a test
  (`AssertJsonOnOneLine`) checks every request of both summary tasks
  (`recording-summary.md`, "The recorder writes the steps and the settings
  list (built)").

## Decisions needed

1. Proposal A: built and tried, 2026-10-19 (see "The trial of A");
   whether to keep it follows a full run.
2. Proposal B: the recorder writes layer 1, and whether the text summary
   task stays as a report of its own.
3. Proposal C: decided and built, 2026-10-19, and tried: see "The trial of
   C".
4. Proposal D: whether the moment and tile descriptions become notes too,
   or only the layers.

## Sources

- Trial file: C:\Users\Public\Downloads\trials-27c9b2b\visit-0001-moment-06-layer-2-t0.txt
  on the target PC, build 27c9b2b, 2026-10-18; measured 2026-10-19.
- Failed runs: tasks 01a1517e-88f1-7967-9aa5-e07f5214aafc (2edb728) and
  01a151e0-bbd5-73a1-b41b-957901c5674f (4048f30).
- Earlier review: `model-load-review-2026-10-18.md`, for the 88464e7 run's
  steps and tokens, the speed measured, and proposals 1 to 6.
- Code at 6a30bcb: `RecordingSummaryVisionKind` (the steps, their inputs,
  and `ChecksWording`), `Knowledge` (`FromRecorded`, `Append`, `Label`),
  `WordingCheck`, `ClaimCheck`, `RecordingSummaryKind.VisitInput`.
