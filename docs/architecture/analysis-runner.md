# Analysis Runner

## Status

Proposed on 2026-10-13 and agreed by the owner the same day, to be tried as
it stands. Step 1 of the build order was built the same day; see
"Implementation status". The owner set three requirements the same day:

- Analysis runs detached from the player. Tasks are queued, and the player
  announces each completed task and report as it becomes available.
  Real-time response is not needed.
- A pipeline may use a specialised model for each stage, for example one
  for frames and one for text.
- The model is a setting. The PC is expected to be upgraded, and a server
  on the local network is expected within a few weeks.

The answers to the questions raised with the design are under
"Decisions (2026-10-13)". The first task kind, the recording summary, is
designed in [recording summary](recording-summary.md). Listed in
[outstanding work](analysis-outstanding-work.md).

## The problem

The recorder holds observed evidence and derives some of it, such as the
reading position and what NVDA had to announce, with its own code. Turning
a recording into findings and reports needs a language model, and models
are slow, large, and wrong in ways that look right. The runner has to queue
that work without holding up the player, keep every model claim separate
from the evidence, and let the model, the machine running it, and the
settings change without making earlier results unreadable.

## What the trial showed

A trial on the target machine on 2026-10-13 ran Qwen3.6-35B-A3B at 4-bit
(UD-Q4_K_M, 22.1 GB,
[Hugging Face](https://huggingface.co/unsloth/Qwen3.6-35B-A3B-GGUF)) in
llama.cpp build b11607, CUDA 12.4
([llama.cpp releases](https://github.com/ggml-org/llama.cpp/releases/tag/b11607)).
The machine has a 6 GB RTX 2060 and 32 GB of RAM. The model has 40 layers.

| Expert weights in RAM | Reading 512 tokens | Reading 4,096 tokens | Writing |
|---|---|---|---|
| All 40 layers | 163 per second | 247 per second | 25.4 per second |
| 36 layers, 4 on the GPU | 231 per second | 271 per second | 28.9 per second |
| 33 layers, 7 on the GPU | 97 per second | 96 per second | 18.4 per second |

One recording frame took 45 seconds in all. The model read the page text
accurately, but called the instrumented Chromium "Microsoft Edge" and
called the player and NVDA's Speech Viewer a "Developer Tools /
Accessibility Inspector". That is the failure the runner has to contain:
fluent statements, made as fact, that the evidence does not support.

The model file, at 22.1 GB, uses most of the memory left free. A larger
model or a higher-precision file needs more RAM or another machine, which
is why the model is a setting.

## Proposed design

### 1. A separate worker process

A new program, `Recorder.AnalysisWorker.exe`, runs the queue. Its logic is
in a library, `Recorder.Analysis`, which the player also uses to queue
tasks and read their state. It is not part of
the player or the capture host, so a slow or failed model never holds up
playback or capture. The player starts it when a task is queued. It keeps
running until the queue is empty, whether or not the player is open, and
then exits.

The worker does not run while a recording is in progress, because a loaded
model uses most of the RAM and much of the CPU. If a recording starts while
a stage is running, the worker stops the stage, records the attempt as
stopped for a recording, asks the server to unload the model, and runs the
stage again from the start after the recording ends.

### 2. The queue is in the catalog

Tasks are stored in the app's PostgreSQL catalog, so the queue survives
closing the player, a restart, or a crash. Migration 0034 adds:

- **analysis_tasks**: the session, the task kind and its version, what the
  task covers (the whole recording or a time range), the model profile
  chosen for each model stage, the state (queued, running, done, failed, or
  cancelled), and when it was queued.
- **analysis_attempts**: one row for each try at a stage, with the
  provenance listed under rule 6, the raw output, and the outcome
  (accepted, failed with its reason, or stopped for a recording).
- **analysis_results**: the accepted output of each finished task, with
  each claim stored separately with its evidence references.
- **analysis_reviews**: a person's acceptance, rejection, or correction of
  a claim.

### 3. The model is a setting

A model profile names one model at one endpoint. The profiles are kept in
`analysis-models.json` in the Windows user's
`%LOCALAPPDATA%\Windows A11y Recorder` folder, next to the player layout.
Each profile holds:

- a name for the player to show;
- the endpoint address and the model's name at that endpoint;
- what it can take: text, images, or both;
- the model file's name, quantization, and SHA-256, as the publisher gives
  them;
- the generation settings: context size, output limit, temperature, and
  seed;
- for a network endpoint, the API key, kept protected with the Windows Data
  Protection API, as the catalog password already is.

Each task kind names the capability each of its stages needs and a default
profile. The player lets the auditor choose another profile when queuing a
task. Changing a profile never changes a finished result, because each
attempt records what was actually used.

### 4. The endpoint

The worker talks to any server with the OpenAI-compatible
`/v1/chat/completions` request, which llama.cpp's server provides, with
images in the same request
([llama.cpp server README](https://github.com/ggml-org/llama.cpp/blob/b11607/tools/server/README.md),
[llama.cpp multimodal](https://github.com/ggml-org/llama.cpp/blob/master/docs/multimodal.md)).

- **This PC, now.** llama.cpp's server in router mode, listening on the
  loopback address only. Router mode loads and unloads models on request,
  with `/models/load` and `/models/unload`, and `--models-max` caps how
  many are loaded at once (default 4)
  ([llama.cpp server README](https://github.com/ggml-org/llama.cpp/blob/b11607/tools/server/README.md)).
  The worker starts it with `--models-max 1` and `--parallel 1`, and with
  each model's settings in a preset file, since presets carry any
  command-line setting per model
  ([llama.cpp server README](https://github.com/ggml-org/llama.cpp/blob/b11607/tools/server/README.md)).
  For Qwen3.6-35B-A3B that is the trial's best layout, `-ngl 99 -ncmoe 36
  -fa on`.
- **The network server, later.** The same request to another address.
  Its security is deferred; see "Decisions (2026-10-13)".
  llama.cpp's server can require an API key (`--api-key`) and serve over
  TLS (`--ssl-key-file`, `--ssl-cert-file`)
  ([llama.cpp server README](https://github.com/ggml-org/llama.cpp/blob/b11607/tools/server/README.md)).
  The worker does not start or manage a network server.
- **No services outside the local network.** The worker refuses any
  endpoint that is not a loopback address or a private network address.
  Recordings hold client test accounts, URLs, and page content, so sending
  them to an outside service is a separate decision, not a setting.

### 5. Pipelines group work by model

A task kind is a fixed list of stages. Some stages are the recorder's own
code. Some send a request to a model and name the capability they need.
The worker runs the oldest queued stage. Before it unloads a model, it runs
every other queued stage that uses the model already loaded. In the trial,
loading took about 11 seconds with the file already in Windows' file
cache; from the SSD alone, 22 GB at its rated 560 MB per second takes about
40 seconds, so switching only when needed matters.

### 6. Provenance with every attempt

Each attempt records:

- the profile name, the endpoint, and the model's name there;
- the model file's name, quantization, and SHA-256. On this PC, the worker
  hashes the file itself, once per file and change time. For a network
  server, the hash is the profile's stated one, recorded as stated, not
  checked;
- the server's build where it reports it, such as llama.cpp b11607, or
  unknown;
- every generation setting sent;
- the prompt template's name and version;
- the SHA-256 of the exact input sent, and the evidence references it
  contained;
- the start and end times, the token counts, and the speeds the server
  reports;
- the raw output, unchanged.

For repeatability, the worker sends one request at a time, with temperature
0, a fixed seed, and prompt caching off. llama.cpp says reusing the prompt
cache can give results that are not identical, and its own default is to
reuse it
([llama.cpp server README](https://github.com/ggml-org/llama.cpp/blob/b11607/tools/server/README.md)).
Even so, an identical result from a different build or a different machine
is not promised, so the record says what produced a result rather than
claiming it can be reproduced exactly.

### 7. Facts in, cited claims out

A model stage is given facts the recorder already holds, not asked to
discover them:

- the recorder's own summaries, with each item tagged with its evidence
  reference: window titles and program names, the pages visited, the keys
  and their outcomes, the reading position, and what NVDA had to announce,
  each labelled observed, derived, or inferred as it is in the player;
- frames, when the stage takes images, with the window titles at that
  moment given as text beside them, so a model is told the browser is the
  instrumented Chromium rather than guessing.

The model answers in a JSON shape the stage defines. llama.cpp's server can
hold output to a JSON schema
([llama.cpp server README](https://github.com/ggml-org/llama.cpp/blob/b11607/tools/server/README.md)).
Every claim must cite at least one evidence reference. The recorder's own
code then checks that the output parses, that every claim cites a
reference, and that every reference cited was in the input. A claim that
fails the check fails the attempt; it is not dropped quietly.

### 8. Failed output is rerun, not repaired

A stage may make several requests, such as one for each page visit. Each
request is one step, and the attempts are kept for each step. An attempt
fails if the server reports an error, if the output stops at the output
limit, if it does not parse, or if a citation check fails. The worker runs
the whole step again from the start, up to two more times by default,
and the accepted attempt replaces the failed ones in the result. Failed
attempts stay in the catalog with their reasons. Output is never
completed, trimmed, or patched to look finished. After three failures, the
task is marked failed with the reasons shown.

### 9. Results are inferred

Every model claim is shown as inferred, with the profile that made it, and
can be opened to show its evidence and its full provenance. A person can
accept, reject, or correct a claim. Each review is its own record, with who
made it and when. It never changes the attempt's output or any recorded
evidence. Model output is never used as evidence by later deterministic
stages; a later model stage may read an earlier stage's claims, and they
stay labelled inferred.

### 10. The player

The player gains a menu bar with an Analysis menu. Its items queue each
task kind for the open recording, open the Analysis tab, and open the model
profiles. Each menu item is at least 44 pixels high and opens with a single
click, with no submenu that has to be held open.

An Analysis tab, detachable like the other tabs, lists the session's tasks
with their state, the profile used, and when each finished. It has:

- a button to queue each task kind for the recording, with the profile to
  use;
- for each task, buttons to cancel it, to run it again, and to open its
  result;
- a line saying whether the worker is running, waiting for a recording to
  end, or stopped, and which model is loaded.

All of it works with the mouse alone, for head pointer and eye tracking
use. Every target is at least 44 by 44 pixels, nothing needs dragging, and
every button also works from the keyboard. When a task finishes or fails,
the status line says so, the Analysis tab's header shows how many results
are unread, and the player raises a UI Automation notification so screen
readers announce it. The player does not need to be open: results waiting
when it opens are shown as unread.

### 11. Reports are Markdown files

A task's output is one or more Markdown files, written by the recorder's
own code from the accepted, checked model output, not taken from the model
as written. They are kept in an `analysis` folder in the session folder,
one folder per task, and listed in the catalog with their SHA-256. A rerun
writes new files; the earlier ones stay until the task is deleted.

Each claim in a report ends with its evidence references, such as
`[E12]`, and the report ends with an evidence list giving, for each
reference, the time in the recording and what the evidence is. The
Analysis tab opens a report in a reading view in the player, where each
reference is a button that moves the playhead to that evidence. The file
can also be opened outside the player, where the references read as plain
text.

### 12. The database server

The player starts the catalog's PostgreSQL server and stops it when it
closes, if it was the one that started it; a program that attaches to a
running server leaves it running. The worker does the same: it starts the
server, or attaches to the one running. If the player closes and stops
the server while the worker is running, the worker's next use of the
catalog fails; the worker then starts the server again, and repeats the
write that failed. A model request in progress is not affected, because
it does not use the catalog until its output is saved.

## Build order

Agreed by the owner on 2026-10-13, with the recording summary:

1. The runner: the catalog tables, model profiles, the endpoint client,
   the local server, and the worker, with no player changes and a stand-in
   task kind used only by the tests.
2. The recording summary's stage 1: dividing visits, the collation, the
   model requests, and the Markdown report.
3. The player: the Analysis menu, the Analysis tab, the reading view, the
   notifications, and the capability description. The system test on the
   target machine follows this step.
4. The recording summary's stage 2: the moments of a visit, the pictures,
   and the vision requests.

## Implementation status

Step 1, the runner, built on 2026-10-13. Not yet run on the target
machine, and the player has no analysis controls yet.

- **Projects:** the library `Recorder.Analysis` and the worker program
  `Recorder.AnalysisWorker.exe`. Migration 0034 adds the tables
  `analysis_tasks`, `analysis_attempts`, `analysis_claims`,
  `analysis_reviews`, `analysis_reports`, and `analysis_model_file_hashes`.
  Removing a recording removes its analysis.
- **Classes:** `AnalysisSettingsFile` reads the settings;
  `EndpointPolicy` refuses addresses outside this PC and the local
  network; `ChatRequest` builds the request body; `HttpModelEndpoint`
  talks to the server; `LocalModelServer` and `LocalServerHost` start
  llama.cpp in router mode; `ClaimCheck` checks the output;
  `AnalysisCatalog` holds the queue and the attempts; `AnalysisWorker`
  runs the queue; `IAnalysisTaskKind` is what each task kind implements.
- **The settings file**, `%LOCALAPPDATA%\Windows A11y Recorder\analysis-models.json`:
  `version` (1); `maxAttempts` (1 to 10, default 3); `localServer` with
  `executable` and `port`; `profiles`, each with `name`, `endpoint`,
  `model`, `capabilities` (`text`, `image`), `file` (`name`,
  `quantization`, `sha256`, `path`), `local` for a model this PC serves
  (`modelPath`, `imageAddOnPath`, `arguments`), `generation`
  (`maxTokens`, default 2048; `temperature`, default 0; `seed`, default 1;
  `thinking`, default false), and `protectedApiKey`; and `defaults`, the
  profile for each capability. A profile that breaks a rule is left out
  and its problem listed; the others are still used. There is no editor
  yet; the player's settings come in step 3.
- **The worker's files**, in `%LOCALAPPDATA%\Windows A11y Recorder\Analysis`:
  `worker-status.json` (what it is doing, the loaded profile, the
  settings problems), and for the local server `llama-server-models.ini`,
  `llama-server.log`, and `llama-server.pid`. One worker runs at a time
  for each Windows user, held by the mutex
  `Local\WindowsA11yRecorder.AnalysisWorker`.
- **No task kinds yet.** Until step 2, a queued task fails with "This
  worker has no task kind". The tests use a stand-in kind.
- **A worker killed during an attempt:** the next worker closes the
  attempt as failed, "The worker stopped during the attempt.", and it
  counts against the attempt limit. Steps already accepted are not asked
  again.
- **A recording left marked as in progress by a crash** keeps the worker
  waiting, with no model loaded, until the app next starts and marks the
  recording interrupted.
- **Tests:** 71 new, unit tests of the rules and integration tests of the
  worker with a real PostgreSQL server, a stand-in model server, and a
  stand-in HTTP server on loopback. The full suite passed, 1,631 tests,
  with the Chromium launcher and browser receiver tests left out as
  before.

## Required tests

- **Unit tests:** queue order and grouping by model; profile reading,
  including a missing or damaged file and refused outside addresses; the
  JSON shape and citation checks; the failure reasons; rerunning and
  replacing; provenance capture; review records leaving output unchanged.
- **Integration tests:** the worker with the catalog in a real PostgreSQL
  server and a stand-in endpoint that gives set answers, slow answers,
  truncated answers, and errors; the queue surviving the worker being
  killed; stopping for a recording and running again after it.
- **Report tests:** a report written from checked output lists every
  reference it cites, and in the reading view each reference moves the
  playhead to its evidence.
- **System test on the target machine:** one task end to end with the
  llama.cpp server and Qwen3.6-35B-A3B, checking on screen that the player
  shows the task finishing, the result labelled inferred, and its
  evidence; and queuing, cancelling, and opening a result with the mouse
  alone.

## Not covered here

- The task kinds and report formats. Each gets its own design.
- Comparing models. That becomes one of the first uses of the runner,
  once there are real tasks to compare on.
- The threat model. Its trust boundaries gain the worker and the model
  endpoint once the network server's security is settled; see
  "Decisions (2026-10-13)".

## Decisions (2026-10-13)

The owner's answers to the questions raised with the design:

1. **The first task kind** is a summary of the recording: a description of
   each page visited and of the site or application tested, from the
   recorder's own collation of the recorded data and a model's text
   analysis, then enhanced with vision analysis of the rendered pages.
   Each stage writes a Markdown file. The task needs the Analysis menu and
   the reporting proposed here. Designed in
   [recording summary](recording-summary.md).
2. **Security of the network server is deferred.** The server will be a
   machine on the owner's home network, behind the router, and the local
   model work is experimental. A network profile may therefore have no API
   key and use plain HTTP for now. Encryption in transit, authentication,
   and the threat model's trust boundaries are to be settled once the
   architecture is one the owner is happy with. Refusing endpoints outside
   the local network (rule 4) stays.
3. **The worker keeps running with the player closed**, as proposed.
4. **The worker starts the local llama.cpp server itself**, as proposed.
5. **Three attempts per stage** is a starting point, to be judged from
   use. The limit is a setting, so it can change without a new build.
