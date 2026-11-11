# Cloud Models: Claude Through the Anthropic API

## Status

Proposed 2026-11-11, not built. The owner's decisions of that day are in
"Decisions (2026-11-11)". The Android checks, which follow this work,
are planned separately; their one decision so far is recorded here
because it limits what is sent.

Statements below are marked as checked in Anthropic's documentation (with
its address), checked in this repository's source, or inference.

## The problem

The local models tried so far (Qwen3.6 35B A3B and Gemma 4 on the
development PC, see [the model trials](model-trials-2026-10-20.md)) did not
write reports good enough to use with a client (owner, 2026-11-11). The
analysis worker sends requests only to servers on this PC or the local
network: `EndpointPolicy` refuses any other address, and
[the analysis runner](analysis-runner.md), "4. The endpoint", made
sending recordings to an outside service "a separate decision, not a
setting" (checked in source). This plan is that decision, for one
service: Claude through the Anthropic API, with the owner's own API key.

## Decisions (2026-11-11)

1. **Claude first.** The cloud profile is built before the Android
   checks, as it is the smaller change and improves the existing desktop
   reports too.
2. **A Claude profile is the permission.** Choosing a profile that sends
   to Anthropic, when queuing a task, is the permission to send that
   recording's facts and pictures to Anthropic. There is no separate mark
   on the recording and no question per task. The player shows which
   profiles send to Anthropic, so the choice is visible where it is made.
3. **Testing stays AI-free where it can.** "As far as possible testing
   should be AI-free both for cost and repeatability, I expect to use AI
   to help with summary reports and vision-centric tests only" (owner).
   The Android checks are the recorder's own rules; a model is used for
   the summary reports and for tests that need a picture to be looked at,
   decided test by test.
4. **The API key is the owner's.** It is typed into the recorder on the
   PC that runs the worker, and kept there, protected with the Windows
   Data Protection API, as a network profile's key already is (checked
   in source: `CurrentUserApiKeyProtector`). It is never written to the
   repository, a log, a report or an attempt's record.

## What the Anthropic API requires

Checked in Anthropic's documentation on 2026-11-11.

- **Address and headers.** Requests go to `https://api.anthropic.com`,
  `POST /v1/messages`, with the key as `Authorization: Bearer` (or the
  older `x-api-key`), `anthropic-version`, and `content-type:
  application/json`. A key not scoped to a workspace also needs
  `anthropic-workspace-id`. Each response carries a `request-id`
  ([API overview](https://platform.claude.com/docs/en/api/overview)).
- **Version.** The latest `anthropic-version` is `2026-10-21`, which
  removes `temperature`, `top_p` and `top_k`: sending any of them is a
  400 error on every model
  ([versions](https://platform.claude.com/docs/en/api/versioning)).
- **No temperature and no seed.** On Claude Opus 5.5, Sonnet 5.5 and
  Haiku 5.5, a non-default `temperature`, `top_p` or `top_k` is a 400
  error on every request, whatever the version
  ([thinking](https://platform.claude.com/docs/en/build-with-claude/thinking)).
  Anthropic's migration guide adds that `temperature = 0` "never
  guaranteed identical outputs on prior models"
  ([Opus 5.5 migration guide](https://platform.claude.com/docs/en/models/opus-5-5/migration-guide)).
  The Messages API has no seed (inference: none is documented in the
  pages read).
- **Thinking.** Opus 5.5 always thinks (adaptive thinking, which cannot
  be turned off); Sonnet 5.5 and Haiku 5.5 think by default
  ([API primer](https://platform.claude.com/docs/en/claude_api_primer)).
  How much is steered by `output_config.effort`: `low`, `medium`, `high`,
  `xhigh` or `max`, with defaults of `medium` for Opus 5.5 and Haiku 5.5
  and `high` for Sonnet 5.5
  ([effort](https://platform.claude.com/docs/en/build-with-claude/effort)).
  Thinking is returned with an empty `thinking` field unless asked for
  ([Opus 5.5 migration guide](https://platform.claude.com/docs/en/models/opus-5-5/migration-guide)).
- **Structured output.** A JSON schema is sent as `output_config.format`
  with `type: "json_schema"`. Every object must have
  `additionalProperties: false`; `minLength`, `maximum` and similar
  limits are not supported (a 400 error); a request may have at most 24
  optional parameters and 16 with union types. A refusal ends with
  `stop_reason: "refusal"` and may not match the schema; an answer cut
  off ends with `stop_reason: "max_tokens"`
  ([structured outputs](https://platform.claude.com/docs/en/build-with-claude/structured-outputs)).
- **Pictures.** Up to 600 a request (100 on models with a 200k context),
  each at most 8,000 by 8,000 pixels and 10 MB in base64, in a request of
  at most 128 MB. Claude 4.7 and later models take a long edge of up to
  2,576 pixels and 4,784 visual tokens of 28 by 28 pixels before
  shrinking a picture. With more than 20 pictures a stricter size limit
  applies; at most 3,000 pixels on each side stays within it
  ([vision](https://platform.claude.com/docs/en/build-with-claude/vision)).
  The Pixel's 1080 by 2404 pictures are within the long-edge limit and
  are sent at full size (inference from those limits).
- **Models and prices.** Per million tokens, input then output: Opus 5.5
  $4 and $20, Sonnet 5.5 $2 and $10, Haiku 5.5 from $0.10 and $0.50;
  the Batch API halves them
  ([pricing](https://platform.claude.com/docs/en/about-claude/pricing)).
  Each has a 1M-token context and 128K output tokens
  ([models overview](https://platform.claude.com/docs/en/models/overview)).
- **What happens to what is sent.** For API users, Anthropic deletes
  inputs and outputs within 30 days, unless agreed otherwise (zero data
  retention), needed for its usage policy, or required by law
  ([data retention](https://privacy.claude.com/en/articles/7996866-how-long-do-you-store-my-organization-s-data)).
  By default it does not use API inputs or outputs to train its models
  ([model training](https://privacy.claude.com/en/articles/7996868-is-my-data-used-for-model-training)).
  The Fable and Mythos models need 30-day retention, so zero data
  retention is not available for them
  ([API and data retention](https://platform.claude.com/docs/en/manage-claude/api-and-data-retention)).
  Whether a client allows its app's content to be sent is for the owner
  to settle with the client; the recorder does not decide it.

## Proposed design

### 1. A provider in the profile

A profile gains `"provider"`: `"openai-compatible"`, as every profile is
now and the default when absent, or `"anthropic"`. An Anthropic profile
holds the model ID (such as `claude-opus-5-5`), the output limit, the
effort, and the protected key, and no local model file, temperature,
seed or sampling options. The settings file reports any of those on an
Anthropic profile as a problem, as it reports other broken rules, so a
task never runs with a setting that was silently dropped.

### 2. One outside address, only for that provider

`EndpointPolicy` keeps refusing every address outside this PC and the
local network, with one exception: an Anthropic profile, whose endpoint
must be exactly `https://api.anthropic.com`. Any other outside address,
or that address on a profile of another provider, or over plain HTTP, is
refused as now. The exception is in code, not a setting.

### 3. The request

The worker builds the body once, hashes it and sends it exactly as
hashed, as now. For an Anthropic profile it is the Messages API's form:
the instructions as `system`, one user message with the input text and
each picture as a base64 `image` block after a line "Picture
<reference>:", as the chat request does now (checked in source:
`ChatRequest.Build`), `max_tokens`, `output_config.effort`,
`output_config.format` with the step's schema, and `stream: true`.
`anthropic-version` is fixed in the source at `2026-10-21`, so a change
of version is a change of the recorder. The schema is the step's
`ClaimCheck.Schema` with `additionalProperties: false` added to every
object and `minLength` removed; the worker's own check of the answer
still refuses an empty statement, as it does now.

Prompt caching is not asked for. The Batch API is left for later: its
half price suits whole reports, but its answers can take hours.

### 4. The answer

The worker reads the streamed events: the text deltas of the answer,
which it watches for loops as it watches llama.cpp's; the thinking
blocks, kept with the attempt where any text is given; and the final
`stop_reason` and token counts. `end_turn` is a finished answer;
`max_tokens` is the answer that reached the output limit, handled as
now; `refusal` is a failed attempt whose reason is the refusal, not
repaired and not sent again. Each attempt also records the `model`
the response names, its `request-id`, the version header, the effort,
and the input, output and thinking token counts.

A 429, a 529 or a 5xx answer, or a broken connection, is the service
being busy or failing, not the model's answer: the worker waits, for
the time a `retry-after` header gives or 30 seconds, and tries again,
three times, then fails the step saying what it was told. A 400 is the
request being refused and is not sent again, as now.

### 5. Repeatability

Answers from Claude cannot be repeated on request, as there is no seed
and no temperature. What the recorder keeps:

- **Reuse.** A step whose request is unchanged reuses its accepted
  answer, as now (checked in source: the accepted attempt is reused
  when its input SHA-256 matches). Running a report again therefore
  gives the same report, unless the recording, the prompt, the profile
  or the recorder changed. The step's identity for a Claude profile is
  the request's SHA-256, the model ID, the `anthropic-version`, and
  the parameters.
- **Recovery without seeds.** Where an answer loops or fails its
  checks, the worker sends the same request again, up to the same three
  tries the seeds give now, and keeps the first that passes. Each try is
  recorded. Running it again later may give a different answer, and the
  report says which profile wrote it, so a reader knows it was not a
  repeatable result.

### 6. The player

- **Profiles window.** "Add a Claude profile" asks for the model (Opus
  5.5, Sonnet 5.5 or Haiku 5.5, or another model ID typed in), the
  effort, the output limit, and the key, in a password box that is
  never shown again once saved. "Check the profile" sends one short
  request, without a recording, and shows the model's name and the
  tokens used, or Anthropic's error.
- **Choosing a profile.** Profiles that send to Anthropic are listed
  with "(sends to Anthropic)" after their name, wherever a profile is
  chosen.
- **Cost.** Each task's row on the Analysis tab shows its input and
  output tokens and an estimate in US dollars, from prices held in the
  profile, filled in from Anthropic's prices of 2026-11-11 and
  editable, since prices change.
- **The worker's pause.** The worker pauses while a recording runs, as
  now, also for Claude profiles; it keeps the recording's PC quiet, even
  though a Claude request does not use the graphics card.

### 7. Defaults

New Claude profiles have an output limit of 32,000 tokens, since
thinking counts toward it (inference from the effort documentation's
advice to set a large `max_tokens` at higher effort), and the model's
own default effort. Which model writes which step is chosen per task, as
now; the owner compares Opus 5.5 and Sonnet 5.5 on a recording already
reported with Qwen3.6.

## Build order

1. **The provider and the request** (`Recorder.Analysis`): the profile's
   provider, the settings file's rules, the endpoint exception, the
   Messages request and its streamed answer, the retries, and what each
   attempt records. Tested against a fake server in the unit tests; no
   key is needed to build or test it.
2. **The player**: adding and checking a Claude profile, the key box,
   "(sends to Anthropic)", and tokens and cost on the Analysis tab.
3. **On the development PC**: the owner adds the key, checks the
   profile, then runs "Summarise this recording" on a recording already
   summarised with Qwen3.6, with Opus 5.5 and then Sonnet 5.5, and
   compares the reports and their costs.

## Not covered here

- Other cloud services. Each needs its own decision, and code, as this
  one did.
- The Batch API and prompt caching, until the costs of real reports are
  known.
- The Android checks; their plan follows this work.
