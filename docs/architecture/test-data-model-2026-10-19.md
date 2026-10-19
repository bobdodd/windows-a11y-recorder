# Design: the test data model

Date: 2026-10-19. Status: agreed with the owner in conversation, 2026-10-19;
not built; where it is stored is proposed, not decided. It corrects the
design of the summary with vision as built, and the reading of "The layered
report" of 2026-10-15 in `recording-summary.md` on which that build rests.
It is the frame for the work in `intermediate-outputs-review-2026-10-19.md`
and `model-load-review-2026-10-18.md`.

## The owner's words

On the report and its steps, 2026-10-19:

> "Step 1 is not supposed to be subservient to the later work, and the
> recorded details and analysis of the overall test and user experience are
> a key part of the report. Vision augments, it does not replace"

> "it is an accumulation of the knowledge needed for the final report.
> Think of the final report taking a carefully constructed data model,
> completed at the end of step 3, of the user experience based solely on
> evidence recorded and interpretation of that evidence including visual
> analysis."

On the model and the evidence, 2026-10-19:

> "The model holds no facts, no evidence, it organizes and refers to those
> facts/evidence. The raw recorder evidence is one collection of facts and
> evidence, and step 1 builds the initial model referencing it all ...
> Step 2 inspects the frame and creates new facts and interpretations and
> adds references to its data in the model. Step 3 works similarly at
> browser level. The report is then build using the model."

> "It certainly sounds like it should persist. It will only grow as we
> start testing for usability and accessibility rather than merely
> reporting on experience and behaviour."

> "It sounds like the recorder owns step 1."

On the test process, 2026-10-19:

> "When we test, we do not make single recordings. It is often many
> recordings made by multiple testers whose tests intersect with the same
> pages, sometimes repeating the same test, sometimes not, sometimes using
> different assistive tech, usually with different user capabilities.
> Those groups of recordings form a project and a single recording does
> not sit in isolation. We may report against a single recording, as we
> are now, or in future across related recordings within the project, or
> against individual pages and their visits."

The owner's answers on who tests, the kinds of test, pages, audits, audio,
and test definitions are given where each part is described below.

## Principles

1. **Facts live in evidence collections; the model holds none.** The model
   organises the facts and interpretations, and refers to them by stable
   ID. It never copies a fact's content.
2. **Each step adds; nothing is replaced.** A step adds facts and
   interpretations to its own collection and references to the model. A
   correction stands beside what it corrects. A step run again adds a new
   version of its facts and interpretations; the earlier version is kept,
   and the model records which version it refers to.
3. **Each step is given the model so far and only its own new evidence.**
   Evidence a step before has analysed is never given again.
4. **The recorder owns step 1.** Its code builds the initial model from
   the recorded evidence.
5. **Only the final report is a report.** No step before it writes for a
   reader. The report is built from the completed model, following its
   references into the collections; the report's wording rules apply to
   the report alone.
6. **The collections and the model persist,** with IDs that stay the same
   across tasks and runs, so that the model can grow across recordings and
   as testing moves to usability and accessibility.

## The parts

### The test process

- **Project:** a group of related recordings, as now.
- **Person:** an auditor, a supervisor, or a participant, with capability
  profiles that can change over time. An auditor has a capability profile
  and a set-up recorded for each audit: "There are disabled auditors, so it
  is an important thing to identify" (owner).
- **Session mode,** one for each recording (owner, 2026-10-19):

  | Mode | People | Whose experience it is |
  | --- | --- | --- |
  | Audit | an auditor | the auditor's, as an expert evaluating with their own capabilities and set-up |
  | Moderated lived experience | a supervisor and a participant | the participant's |
  | Self-moderated lived experience | a participant | the participant's |
  | Guided self-moderated, planned | a participant, guided by the recorder | the participant's |

  The mode bounds what an interpretation can say: in an audit, a barrier
  found is an expert's finding with that auditor's capabilities and
  set-up, not the experience of a participant who lives with those needs.
  In a guided session, the recorder's guidance, each instruction and when
  each task began and ended, is recorded evidence.
- **Test,** of one of three kinds (owner, 2026-10-19), defined by the
  tester in the recorder, in the recorder's own format: "The tester can and
  should define its own" (owner).

  | Kind | Structure |
  | --- | --- |
  | Scripted | tasks, each with set steps |
  | Unscripted task-based exploratory | tasks, each with a goal and no set path |
  | Exploratory | no set tasks; auditors tend to test this way |

- **Task attempt:** a span of a recording in which a task was attempted,
  with its start, its end, and its outcome. The same test or task across
  recordings is how repeats are found.
- **Recording:** links its project, its people and their roles, its mode,
  its test where there is one, and its set-up at the start (the assistive
  technology and the settings, referring to the evidence).

### Pages

"There probably needs to be a hierarchy of identical URLs potentially
containing inferred pages within" (owner, 2026-10-19). The levels, each
with its provenance:

| Level | Provenance | Example |
| --- | --- | --- |
| Site | observed: the origin | `https://www.cnib.ca` |
| URL | observed: the address; a rule the owner can change says whether a query string or a fragment makes a new URL | `/en/programs` |
| Page within a URL | derived by the recorder's rules, or inferred by a model from the frames | the main content replaced at the same URL |
| State within a page | derived or inferred | an open dialog, an expanded menu |

The recorder already derives pages within a URL in one place: a fragment
route with no target, or the main content replaced (the main landmark's
children having no node in common), begins a new visit
(`recording-summary.md`, "Main content replaced"). The project's earlier
view and visit design gives the same idea: a unique view is an identity
that visits belong to, under rules that can be changed (project wiki,
"View and Visit Model").

An auditor may merge or split pages; their decisions are reported, and
kept apart from the recorder's and the models'. Each visit points to the
lowest level it can be placed at, so that a report on a page gathers its
visits from every recording.

### The evidence collections

Each item has an ID, its provenance class (observed, derived, inferred,
or reported, as in the project wiki's "Evidence Provenance and
Confidence"), and the step that made it, with that step's template
version, model, and settings.

- **The recorder's evidence:** what the collation now numbers E1, E2, and
  so on in memory for each task, stored per recording, each item pointing
  to its events in the recording file.
- **Audio:** "both the supervisor's and the participant's audio is
  evidence ... The supervisor's comments are imortant as they are giving
  guidance and instruction to the participant" (owner, 2026-10-19). The
  recorder can capture the microphone and the system's audio now;
  transcription in the recorder is to be built. Each utterance is an item
  with its time and its speaker's role. A transcript is a model's work, so
  it is inferred, and its audio is the evidence it cites.
- **Frame evidence, step 2:** the facts a model observes in a captured
  frame, and its interpretations of them, each item citing the frame and
  any items of other collections it rests on.
- **Browser evidence, step 3:** the same, from the page as the recorder
  rebuilt it.

### The model

- **For each recording:** its visits, each pointing to a page; its timed
  events, including the supervisor's guidance and the recorder's; its
  moments; its task attempts; and the interpretations, all as references
  to items in the collections.
- **Across recordings:** the same structures joined through pages, tests,
  tasks, and people, so that a step can say, for example, that two NVDA
  users missed the same control on the same page. Interpretations across
  recordings are a layer of their own, written from the recordings'
  models; they never change a recording's own model.
- **Findings,** for usability and accessibility testing, are a part the
  model is shaped to take later.

### The steps

1. **The recorder** builds the initial model from its evidence and the
   test process: the visits and their pages, the events in order, the
   set-up, the people and the mode, and the task attempts.
2. **The frames:** a model inspects each chosen frame, given the model so
   far and that frame, and adds frame evidence and references.
3. **The browser:** a model inspects the rebuilt page, given the model so
   far and that page's tiles, and adds browser evidence and references.

The final report is then built from the model. A report may cover one
recording, as now, related recordings within a project, or a page and its
visits.

## The build against the design

| The design | As built at c8384b7 |
| --- | --- |
| Step 1 is the recorder's, building the model | Step 1 is the text summary's visit step: a model writes prose for an auditor, in sections and paragraphs, summarising and grouping actions |
| The analysis of the whole test is part of what the report draws on | The text summary's whole-recording description (the site, its audience, the session) is used by no step of the vision task and is not in its report |
| The model refers to facts | The visit's "knowledge" is statements of text, many restating facts; layer 1's K3, for example, lists the page's headings |
| Each step is given the model and its new evidence only | The moment step and layer 2 are given the facts since the moment before, from the same lines as step 1's input (`FactsSince` reads `visit.Lines`); the page chapter is given the page's changes; the story is given step 1's whole input again, the steps from the same lines, and the knowledge; the opening and the executive summary are given the steps |
| The collections persist with stable IDs | The E references are rebuilt for each task; the frame and tile answers are stored per step and indexed in `moments.json`, as inferences citing E references, not as items with IDs of their own |
| The report is built from the model | The report's chapters are written by four model steps from the knowledge and the raw facts |
| The test process is in the model | The catalog has projects and recordings, and a free-text capability description per recording; no people, roles, modes, tests, tasks, or pages across recordings |

## The work in progress, against the design

- **Proposal B of the 2026-10-19 review is withdrawn.** It would have put
  the facts in place of step 1's interpretations, against principles 1 and
  3.
- **Proposals A and C fit:** A keeps the wording rules for the report, C
  lays out what the model refers to more compactly. **D fits:** what a step
  gives is items and references, not prose.
- **Proposal 4 of the 2026-10-18 review** fits as part of building the
  report: the recorder writes each visit's steps from the events in the
  model. Giving those steps to the story, the opening, and the executive
  summary gives evidence again, against principle 3, until the steps are
  events in the model.
- **The text summary task** has no role of its own under the design: step
  1 is the recorder's, and the analysis of the whole test is built into
  the model and the report.

## Decided, 2026-10-19

1. **Who speaks:** "For now I was trying to keep it simple and assume the
   testing is in person in a lab, and I assumed an open shared mic rather
   than headsets. So yes, we do need user identification as we may hear
   the screen-reader too" (owner). One microphone hears the supervisor,
   the participant, and the screen reader through the speakers, so each
   utterance in a transcript carries its speaker (supervisor, participant,
   or screen reader) with a score for how sure that is. The recorder's
   audio collector has two sources, the microphone and the system's audio
   (`AudioCaptureOptions`); the system's audio carries the screen reader's
   speech on its own, and is evidence of which sounds on the microphone
   are the screen reader's. Speech the screen reader is heard to give is
   observed evidence, where today the report can only say what NVDA had to
   announce. Remote sessions are left for later.
2. **When interpretation happens:** "interpretation can happen at any step
   where the information is available. Typically I expect that to be at
   the earliest point that the interpretation can be performed without
   rework later" (owner). An interpretation that rests only on the recorded
   evidence belongs in step 1, after the recorder has built the model; one
   that needs a frame or the browser's page waits for step 2 or 3. A later
   step that disagrees adds a correction beside it.
3. **Pages:** "I suspect we have to give a score to the accuracy of our
   \"page\" identification. I've seen CMS based sites that use the query to
   identify pages, It's something that I think we will develop over time,
   analyzing pages with the same base URL but different get params to see
   if they are the same page or not" (owner). Every placing of a visit at a
   level of the page hierarchy carries a score for how sure it is. As a
   starting rule, a URL is the origin and the path; each distinct query
   string under it is a candidate page, scored for whether it is the same
   page as its siblings; a fragment is a place or state within a page,
   unless the recorder's rule on fragment routes makes it a new visit. The
   rule is to be developed by comparing pages with the same path and
   different queries.
4. **The order of work:** "We should do the full run of what we have
   first, just to know where we are before starting" (owner). The full run
   of c8384b7 comes before any building toward this model.

## Proposed: where the model is stored

The owner asked for a suggestion: "The content sounds kind of relational,
but there is also a tree-like hierarchy to it as well." The proposal, not
yet decided:

- **The raw recorded evidence stays in each recording's file,** unchanged
  and complete in itself, as now.
- **Everything else goes in the PostgreSQL catalog** the recorder already
  bundles and runs: the recorder's evidence register (each item pointing to
  its events in the recording's file), the frame and browser evidence, the
  transcripts, the test process, and the model.
- **Relational where it is relational:** people, roles, sessions, tests,
  tasks, and the references between items are tables and link tables. A
  link table lets one interpretation rest on many items and one item
  support many interpretations, and a foreign key makes "the model holds no
  facts" a rule the database keeps: a model entry can only name an item, it
  has no column to copy one into.
- **Trees as parent references:** the page hierarchy, and project,
  recording, visit, and event, are rows with a parent. Recursive queries
  walk them; the catalog already uses one, for the chain of a task's
  prerequisites (`AnalysisCatalog`).
- **Each item's own content** (a fact's text, an interpretation and its
  score, a transcript's words) is a column of its row, in `jsonb` where its
  shape varies by kind, as several catalog tables already do.
- **Versions are added, never overwritten:** each row names the step run
  that made it; the model refers to a version; a later run adds rows.
- **Why not another kind of store:** a graph or document database would be
  a second server beside the one the recorder bundles, and reports across a
  project's recordings need one store that sees them all, which files
  beside each recording would not give. An export of one recording's model
  and evidence to a file, for archiving or sharing, can be added on top.

## Sources

- The owner's words: this conversation, 2026-10-19
  (pplx://sessions/6dfcef00-2f6e-406d-8b15-d9d20a35ccd3).
- The design of 2026-10-15: `recording-summary.md`, "The layered report
  (built)".
- The catalog: `src/Recorder.Database/Migrations/0001_core.sql` (projects,
  recordings), `0035_recording_summary.sql` (participant capabilities);
  the recursive query in `src/Recorder.Analysis/AnalysisCatalog.cs`.
- The audio sources: `src/Recorder.Collectors.Audio/AudioCaptureOptions.cs`.
- The collation and its evidence register: `SummaryCollator` and
  `EvidenceRegister` in `src/Recorder.Analysis/Summary/Collation.cs`.
- The inputs: `RecordingSummaryKind.VisitInput`, and `MomentInput`,
  `Layer2Input`, `PageChapterInput`, `StoryInput`, `OpeningInput`,
  `ExecutiveInput`, and `FactsSince` in `RecordingSummaryVisionKind`, at
  c8384b7.
- The project wiki: "View and Visit Model" and "Evidence Provenance and
  Confidence".
