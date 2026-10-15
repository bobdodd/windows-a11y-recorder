# Analysis Runner

## Status

Proposed on 2026-10-13 and agreed by the owner the same day, to be tried as
it stands. Steps 1 and 2 of the build order were built the same day, and
step 3 on 2026-10-14; see "Implementation status". Steps 1 to 3 passed
their system test on the target machine on 2026-10-14; see
[the validation](../validation/analysis-runner-2026-10-14.md). Step 4a was
built the same day and is not yet tested on the target machine; step 4b
is not built. The owner set three requirements the same day:

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

A statement's fields are in the order text, kind, evidence (owner,
2026-10-14). The server turns the schema into a grammar that writes an
object's required fields in the order of its properties
([llama.cpp json-schema-to-grammar.cpp at b11607](https://github.com/ggml-org/llama.cpp/blob/b11607/common/json-schema-to-grammar.cpp)),
and every answer before the change began each statement with its kind,
the first of the schema's fields. On the target machine the summary with vision
twice began a statement as a claim and then wrote that something was
unknown, citing nothing: "It is unknown whether the mouse clicks affected
the CNIB webpage...", at moment 9 with build 043cbf1, and "What NVDA
announced at this moment is not provided in the facts...", at moment 1
with build 8addabd, after a sentence in the prompt said such a statement
is of kind unknown. With the text first, the model writes the statement
before saying what kind it is. The check is unchanged, so a claim citing
nothing still fails. Holding every claim to cite one of the input's
references was considered and not done: a claim with nothing to cite would
then have to cite something, and the check would no longer show it. Every
template's version was raised by one, as each shows the fields' order.

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

Results are to be repeatable (owner, 2026-10-14). At temperature 0 the same
request gives the same output, so output the checks refuse is not asked for
again: the step fails at once, saying so. Only a failure that gave no
output, such as a server error or a server that could not be reached, is
run again at temperature 0. On the first run on the target machine the
same 7,107 characters came back three times, cut off at the output limit,
so the two reruns took 2.5 minutes and could not succeed. A profile with a
temperature above 0 has its refused output run again, up to the limit, as
above. To try again with different settings, change the profile and choose
Run again.

A request the server refuses itself, answering 400 Bad Request, as for a
request longer than its context, is not sent again either, at any
temperature: the same request would be refused again (owner, 2026-10-14).
On the target machine the visit step of the summary with vision, 20,927
tokens, was refused three times by a server with a context of 16,384
tokens before this was built.

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
   and the vision requests. Built in two steps, agreed by the owner on
   2026-10-14: 4a, the moments and their captured frames; and 4b, the
   recreated pages.

## Implementation status

Step 1, the runner, built on 2026-10-13. Passed its system test on the
target machine on 2026-10-14, with steps 2 and 3; see
[the validation](../validation/analysis-runner-2026-10-14.md). The
player's controls are step 3, below.

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
  and its problem listed; the others are still used. There is no full
  editor; the player's Model profiles window, from step 3, sets the
  defaults and opens the file in Notepad.
- **The worker's files**, in `%LOCALAPPDATA%\Windows A11y Recorder\Analysis`:
  `worker-status.json` (what it is doing, the loaded profile, the
  settings problems), and for the local server `llama-server-models.ini`,
  `llama-server.log`, and `llama-server.pid`. One worker runs at a time
  for each Windows user, held by the mutex
  `Local\WindowsA11yRecorder.AnalysisWorker`.
- **Task kinds:** the recording summary, from step 2; see
  [recording summary](recording-summary.md). A task of a kind the worker
  does not have fails with "This worker has no task kind". The tests also
  use a stand-in kind.
- **Loading a model:** llama.cpp's router loads a model in the
  background after `POST /models/load` and answers other requests with
  503 "Loading model" until it is loaded. On the first run on the target
  machine, on 2026-10-14, the worker sent its first request at once, and
  the three attempts of the first visit failed that way while the 22.1 GB
  file loaded. The worker now reads the model's state in `GET /models`
  every 2 seconds and sends nothing until it is "loaded"
  ([llama.cpp server](https://github.com/ggml-org/llama.cpp/blob/master/tools/server/README.md)).
  A load the router marks failed, or one not finished after 15 minutes,
  fails the step, saying which.
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

Step 3, the player, built on 2026-10-14 as agreed under "Decisions on the
player (2026-10-13)". Passed its system test on the target machine on
2026-10-14: all six tests, with the mouse alone; cancelling took 3 s.

- **Classes:** in `Recorder.Analysis`, `ReportDocument` reads a report's
  Markdown back into headings, paragraphs, list items, quotes, and tables,
  and its evidence list into each reference's time; `AnalysisPlayer` holds
  the names the player shows, the unread count, what to announce, the
  worker's line, the default profiles, and starting the worker. In the
  player, `MainWindow.Analysis.cs`, `ReportWindow`, `CapabilityDialog`, and
  `ModelProfilesWindow`. The catalog gained `RecordingIdAsync`,
  `ReportsAsync`, and `AnyQueuedAsync`.
- **The menu bar** has one menu, Analysis: Summarise this recording,
  Describe the participant's capabilities..., Show the Analysis tab, and
  Model profiles.... Each item is 44 pixels high, in the system's menu
  colors, and none has a submenu.
- **The Analysis tab** is the fifth detachable tab. It shows the worker's
  line (what it is doing and the model loaded, or that it has stopped,
  with any settings problems), the profile to use and Queue summary, Read
  the queue again, and each task of the open recording with its state,
  profile, times, and failure. Each task has Cancel, Run again, and Open
  result, or Why it failed for a failed task; every button is at least 44
  by 44 pixels. The rows are made again only when a task changes, and the
  focused button keeps focus.
- **Reading the catalog:** every 3 seconds, the worker's status file and
  the open recording's tasks. The recording is found by its session
  folder's name, as the catalog's `recordings.session_key` holds it.
- **Unread:** a finished or failed task with no read time. The tab's name
  gives the count, such as "Analysis (2 unread)". Opening a result, or Why
  it failed, marks the task read.
- **Notifications:** a task that finishes, fails, or is cancelled between
  two reads is announced as a UI Automation notification, activity
  `Recorder.App.Analysis`, and shown in a second line of the status bar
  until the next. Results already waiting when a recording is opened are
  shown as unread, not announced.
- **The reading view** opens each report in its own window. Headings have
  their heading level for screen readers. Each paragraph, list item, or
  quote is followed by its reference buttons, such as "E3 00:00:04", named
  "Go to E3, 00:00:04.210" with the evidence as help text; in the evidence
  list, each reference is a button. A reference with no time in the
  recording, such as the auditor's description, has no button. The
  playhead moves only when the report's session folder is the recording
  open in the player; otherwise the window says so.
- **Checking the report:** the file's SHA-256 is checked against the
  catalog when it is opened. A file that has changed still opens, with a
  warning that it may not be the task's result.
- **The worker** is `Recorder.AnalysisWorker.exe` in the player's own
  folder (`AppContext.BaseDirectory`); the player's project references it,
  so it is built beside the player. The player starts it, hidden, when a
  task is queued and when the database starts with tasks still waiting.
  A second worker exits at once. Because it runs from the player's
  folder, the worker uses the same frameworks as the player: .NET, Windows
  Desktop, and ASP.NET Core. The player takes some files from those
  frameworks rather than copying them to its folder. On the first run on
  the target machine, on 2026-10-14, the worker had only .NET and stopped
  at once: "Could not load file or assembly
  'System.Security.Cryptography.ProtectedData, Version=10.0.0.0'". The
  player showed this in the worker's line, and the task stayed queued.
- **The capability dialog** shows the current description and when it was
  written, and the earlier versions read only. Saving unchanged text adds
  no version, and an empty text is refused.
- **Model profiles** lists each profile with its model, endpoint, and
  capabilities, and the file's problems, with a default for text and for
  images ("No default"). Save the defaults changes only `defaults` in the
  file. Open the file in Notepad writes an empty file first when there is
  none (version 1, no profiles); Read the file again shows it as saved.
- **Tests:** 12 new unit tests of reading reports, the unread count, the
  announcements, the worker's line, and the defaults, and checks of the
  three catalog methods in the summary's integration tests. The full suite
  passed, 1,681 tests, with the Chromium launcher and browser receiver
  tests left out as before. The windows themselves are tested on the
  target machine.

Step 4a, the summary with vision from the moments' captured frames, built
on 2026-10-14; see "Stage 2 implementation status" in
[recording summary](recording-summary.md). For the runner and the player:

- **A task may follow another.** The vision task is queued after a
  summary, with `after_task_id`, which the worker already honoured.
- **The Analysis menu** has Add vision to the summary, available when a
  recording is open and there is a default profile for images.
- **The catalog** gives the input references of a step's accepted
  attempt, so that the vision task can check that it builds on a summary
  written from the same facts.
- **The settings file** reports two profiles served by this PC with the
  same model name as a problem.
- **Tests:** 13 unit tests of choosing moments, and 6 integration tests:
  the vision task end to end after a summary, without a summary, on a
  summary written from other facts, on a recording without frames, the
  recording file's moment records, and the duplicate model name. The full
  suite passed, 1,707 tests, with the Chromium launcher and browser
  receiver tests left out as before.

### Sampling options

Built on 2026-10-14, after decision 4 below.

- **A profile may give the server's other sampling options**, in its
  `generation`, as `sampling`: names and numbers, by the server's own
  names, for example `"sampling": { "presence_penalty": 1.5 }` for
  llama.cpp. They are sent with every request, recorded with each attempt
  under `sampling` in its parameters, and listed with the profile. The
  fields the worker sets itself, such as `temperature`, `seed`, and
  `max_tokens`, are refused there, and so is a value that is not a number.
- **The server's defaults are recorded with each attempt**, under
  `server_defaults` in its parameters: the model's default generation
  settings as llama.cpp's `/props` gives them, so that each attempt records
  how it was sampled, including the options it did not send. Before this,
  only the temperature, seed, and output limit were recorded.
- **Answers are streamed**, with `stream` and `stream_options` set, and
  the content, finish reason, usage, and timings are read from the
  stream's chunks, as llama.cpp sends them
  ([server-task.cpp](https://raw.githubusercontent.com/ggml-org/llama.cpp/master/tools/server/server-task.cpp)).
- **An answer that repeats a statement is stopped**: as the answer
  arrives, each statement's text is compared with those before it, and at
  the first that is the same word for word, the worker closes the request
  and the attempt fails, saying which statement and its text. At
  temperature 0 it is not asked for again, as for any refused output.

### Trying a step with other sampling options

Built on 2026-10-14, so that one step can be tried with other sampling
options without running its task again. The worker program, run as

`Recorder.AnalysisWorker.exe --trial TASK STEP FILE [OPTION=VALUE ...]`

runs the task's code again, giving back the task's accepted steps before
STEP, sends STEP's request with the options given on top of its profile's,
and writes what it gave to FILE: the outcome and why it would fail, the
number of statements, the tokens and times, the settings sent with the
server's defaults, and the answer as it arrived. Nothing is written to the
catalog. It does not run while the worker is running.

- **Tests:** the sampling options read, sent, recorded, and refused; the
  repeat watch; a streamed answer read whole and stopped where the watch
  says; the server's defaults read from its properties; an answer that
  repeats a statement failing without being asked for again; and a trial
  of one step that writes nothing to the catalog.

### Reusing steps when run again

Agreed with the owner on 2026-10-14 and built the same day, because a run
of the summary with vision takes about 45 minutes to reach its ninth
moment, and runs are expected to fail as the analysis is developed. The
player's task row has two buttons:

- **Run again** queues a new task that records the task it runs again.
  Before sending a step's request, the worker looks in that task for an
  accepted attempt at the same step made with the same request, the same
  model file, the same server build, and the same settings: the request's
  SHA-256, which covers the model's name, the prompt, the input, the
  pictures, the sampling settings, the seed, and the answer's form; the
  model file's SHA-256; the server's build; and the settings sent with the
  server's defaults. Where there is one, its answer is reused: it is
  copied into the new task as that task's accepted attempt, with its
  statements, token counts, and times, and it points to the attempt the
  model wrote, so that a chain of reruns leads back to it. A step with a
  model file whose SHA-256 is unknown, or a server whose build is unknown,
  is not reused.
- **Run again from scratch** queues a new task that reuses nothing, for
  checking that a run repeats.

Where that task's step failed with the same request, model file, server
build, and settings, and the checks refused its output, the new task fails
at once at temperature 0, saying that nothing has changed and why it
failed, as in decision 8. A failure with no output, such as a server
error, is run again. The report's "How this was made" lists the steps
reused and the tasks whose attempts the model wrote, and its table of
attempts marks each one reused.

- **Catalog:** migration 0036 adds `rerun_of_task_id` to `analysis_tasks`
  and `reused_from_attempt_id` to `analysis_attempts`.
- **Whether the page's steps are reused** depends on the recreated page
  being pictured the same, byte for byte, at each run: the request covers
  the pictures. On the target machine on 2026-10-14 moment 1's 8 tiles
  were the same, byte for byte, in three runs. The first run again with
  build 8addabd reused the frame's step but not the tiles' steps: each
  tile's text in the request ended with its file, in the task's own
  folder, so the request differed at every run. The request now leaves the
  file out; the report's evidence list still gives it (owner, 2026-10-14).
- **The run that reached the visit step,** build c790781 on 2026-10-14,
  had every moment's steps accepted, and its visit step was refused: its
  request, 20,927 tokens, was longer than the server's context of 16,384.
  The owner asked that the fix restart where it failed. The next build's
  start script raises the context of the image profile to 32,768 tokens,
  keeping the file as it was beside it; the context is not among the
  settings compared, as the server's settings read do not include it
  ([llama.cpp server-task.cpp at b11607](https://raw.githubusercontent.com/ggml-org/llama.cpp/b11607/tools/server/server-task.cpp)).
  The moments' requests of the vision task's integration test were the
  same, byte for byte, with build c790781 and the next. Whether 32,768
  tokens fit in the target machine's 6 GB with the profile's arguments is
  unknown until it runs. A visit's request grows by about 2,400 tokens a
  moment, so a longer visit would not fit in any set context; what the
  visit step is given is to be designed separately.
- **The first run again with build ba4eacf,** 2026-10-14, reused moment
  1's frame and tiles' steps and sent its combined description again,
  which then repeated a statement and failed. A reused step's statements
  were read back sorted by the name of their section, so the frame's came
  back as "change" before "screen", not in the order the model wrote
  them, and the combined description's request was not the same. The
  check before delivery had compared two runs that reused nothing, so it
  did not show this. A step's statements are now read back in the order
  of its sections as the request gives them, then by paragraph and
  statement, as the checks give them when the step runs; the summary's
  visit descriptions given to the vision visit step are read the same
  way. The integration test now runs the vision task, fails it at its
  first visit's step, runs it again, and requires every moment's step to
  be reused and the visit's request to be the same, byte for byte, and a
  run again once done to send nothing. With the old order it fails, with
  7 requests sent where 5 are expected.
- **The second run again, with build 91a51a1,** 2026-10-14, reused
  moments 1 to 5 whole and sent moment 6's first group of tiles again.
  Only the first tile of a page differed between runs, and in it only its
  top right corner, about 97 by 25 pixels: some pictures held the
  viewport's size, "929px × 925px", painted over the page, and others did
  not. The recreation browser opens DevTools for each tab, and the
  DevTools front end turns on the viewport's size painted after each
  resize
  ([DevTools front end, OverlayModel.ts](https://raw.githubusercontent.com/ChromeDevTools/devtools-frontend/main/front_end/core/sdk/OverlayModel.ts)),
  which fades on a timer, so whether a picture held it depended on timing.
  Of the 10 first tiles of the run being run again, 3 held it; of the 8
  of the run again, 1. The owner agreed the same day that the analysis's
  recreation browser opens without DevTools; the player's recreation
  keeps DevTools and its evidence panel. Nothing in the analysis uses
  DevTools: the build is waited for and the pictures taken over the
  browser's own DevTools protocol connection. A moment whose stored first
  tile held the size is sent again once; whether DevTools changed anything
  else in the pictures is unknown until two runs' files are compared. The
  same run then stopped at moment 8's combined description, which
  repeated a statement word for word; its first tile had not held the
  size, so the same request would fail again at temperature 0.
- **Tests:** integration tests with the catalog in a real PostgreSQL
  server and a stand-in endpoint: a step reused, with its answer and
  statements, and one run again because its request changed; nothing
  reused after the server build or the model file changed; nothing reused
  from scratch; a step that failed unchanged failing at once with no
  request sent; and a rerun of a rerun pointing to the attempt the model
  wrote. A report test for the reused steps listed.

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

## Decisions on the player (2026-10-13)

Before step 3 was built, the owner chose among four proposals:

1. **References in the reading view are buttons under each paragraph.**
   The prose reads normally, with each reference as plain text, and each
   paragraph is followed by a row of buttons, at least 44 by 44 pixels,
   such as "E3 00:00:04", each moving the playhead to that evidence.
   Buttons inline in the text were not chosen, since every line holding a
   reference would become 44 pixels tall.
2. **A report opens in its own window**, not modal, so that it can sit
   beside the player while its references move the playhead. Several can
   be open at once.
3. **Model profiles... opens a window of the profiles and the defaults.**
   It lists each profile with its problems, lets the auditor choose the
   default profile for text and for images, and opens
   `analysis-models.json` in Notepad and reads it again. A full profile
   editor comes later.
4. **Run again queues a new task** of the same kind with the same
   profile. The earlier task and its report stay, so the two can be
   compared.

The rest of step 3 was proposed with them and agreed: a menu bar with one
Analysis menu, holding Summarise this recording, Describe the
participant's capabilities..., Show the Analysis tab, and Model
profiles...; the Analysis tab as the fifth detachable tab; the player
reading the catalog every 3 seconds while a recording is open; a
capability dialog showing when the current text was written and the
earlier versions; and the player starting the worker when a task is
queued, and when it opens with tasks still waiting.

## Decisions (2026-10-14)

From the first run of the recording summary on the target machine, where
the first visit's output stopped at 2,048 tokens three times with the same
text:

1. **Refused output is not rerun at temperature 0**, as the owner chose
   from three proposals: results are to be repeatable, if not
   deterministic. See "8. Failed output is rerun, not repaired". The two
   not chosen were a rerun with the next seed and a higher temperature,
   which is not repeatable from the settings, and a rerun told why the last
   output was refused, which comes close to repairing it.
2. **The profile on this PC writes up to 8,192 tokens**, set in its
   `generation`, not by a new default. Its 2,228-token input and 8,192
   tokens of output fit the 16,384-token context set in its arguments.
3. **The summary's prompts are version 2:** each section holds only its own
   paragraphs, the activity is summarised with repeated actions grouped,
   and the JSON is written on one line. The output had put the purpose and
   the activity inside "page", stated each recorded key one at a time, and
   spent 2,169 of its 7,098 characters on indentation and line breaks. See
   [recording summary](recording-summary.md).

From the second run of the summary with vision on the target machine, the
same day, where the moment prompt was version 2 (decision 6 in
[recording summary](recording-summary.md)): moments 1 to 9 of the first
visit were accepted, using 576 to 1,038 output tokens each, and moment 10,
the visit's last frame, showing the recorder at the end of the recording,
stopped at the output limit of 8,192 tokens. Its 136 statements described
the recorder's controls one by one; statement 40 was the first to repeat an
earlier one word for word, and 90 were repeats, cycling through the same
three at the end.

4. **Repetition is met with a presence penalty, and an answer that repeats
   a statement is stopped**, as the owner chose from three proposals ("A
   with C"), to be tried first on moment 10 alone. The model's card
   suggests, for its non-thinking mode, `presence_penalty=1.5`, and says
   that it may be adjusted "between 0 and 2 to reduce endless
   repetitions. However, using a higher value may occasionally result in
   language mixing and a slight decrease in model performance"
   ([Qwen3.6-35B-A3B](https://huggingface.co/Qwen/Qwen3.6-35B-A3B)). The
   temperature stays 0 and the seed 1. Not chosen: llama.cpp's DRY
   penalty, which penalises a repeated sequence rather than a repeated
   token ([llama.cpp server](https://github.com/ggml-org/llama.cpp/blob/master/tools/server/README.md)).
   In llama.cpp, the penalties look back over `repeat_last_n` tokens, 64
   by default (the same page), and the tokens counted include the
   prompt's as well as the answer's
   ([server-context.cpp](https://raw.githubusercontent.com/ggml-org/llama.cpp/master/tools/server/server-context.cpp)),
   so the window is tried as well as the penalty. The penalty and window
   for the profile are to be chosen from the trials. See "Sampling
   options" and "Trying a step with other sampling options".

The trials of moment 10 alone, on the target machine the same day, with
server build b11607-ff026f8c0, temperature 0, and seed 1:

| Sampling options | Outcome | Statements | Output tokens | Time |
|---|---|---|---|---|
| `presence_penalty` 1.5 | Passes the checks; finish reason "stop" | 6 | 369 | 73.1 s, of which 58.8 s on the prompt |
| None | Stopped at statement 40, the first repeated word for word | 40 | Not given, as stopped | 132.6 s |

The 40 statements without the penalty were the same, in order, as the
first 40 of the run that reached the output limit, so streaming the answer
did not change it. The server's defaults, as recorded, were
`presence_penalty` 0, `repeat_penalty` 1.0, `frequency_penalty` 0,
`repeat_last_n` 64, and `dry_multiplier` 0; with the same server and
preset, the earlier runs had no penalty for repetition. With the penalty,
the answer named the recorder's window with its capture channels and
settings, its timeline, NVDA's Speech Viewer, and the taskbar, said that
the moment was the visit's last frame, and gave what changed at it as
unknown. One moment of one recording shows that the penalty can stop the
repetition, not that it always will.

5. **The penalty is set for "This PC, with images" only**: the owner,
   "Let's be conservative and apply it only to images for now". The
   profile's `generation` has `"sampling": { "presence_penalty": 1.5 }`;
   "This PC", for text, has no sampling options, since its answers have
   not repeated themselves. The start script of the build that follows
   sets it in the settings file on the target machine, keeping the file
   as it was beside it, so that no file is copied by hand.

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
