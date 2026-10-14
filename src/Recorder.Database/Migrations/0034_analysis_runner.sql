-- The analysis runner's queue and results. A task is one piece of analysis
-- of one recording, queued by the player and run by the analysis worker.
-- Each request a task makes to a model is a step, and each try at a step is
-- an attempt, kept with what produced it. A step's accepted attempt holds
-- its output, and its claims are kept one to a row with the evidence
-- references they cite. A person's review of a claim is its own row and
-- never changes the attempt. A report is a file the task wrote. Removing a
-- recording removes its analysis. See docs/architecture/analysis-runner.md.

CREATE TABLE analysis_tasks (
    task_id uuid PRIMARY KEY DEFAULT uuidv7(),
    recording_id uuid NOT NULL REFERENCES recordings (recording_id) ON DELETE CASCADE,
    kind text NOT NULL CHECK (length(kind) > 0),
    kind_version integer NOT NULL CHECK (kind_version > 0),
    -- The part of the recording the task covers, in nanoseconds from the
    -- recording's start; both null for the whole recording.
    scope_start_nanoseconds bigint CHECK (scope_start_nanoseconds >= 0),
    scope_end_nanoseconds bigint CHECK (scope_end_nanoseconds >= 0),
    -- A task that runs only after another has finished.
    after_task_id uuid REFERENCES analysis_tasks (task_id) ON DELETE CASCADE,
    -- The model profile chosen for each capability the task's model stages
    -- need, such as {"text": "This PC"}, by profile name.
    stage_profiles jsonb NOT NULL DEFAULT '{}'::jsonb,
    state text NOT NULL DEFAULT 'queued'
        CHECK (state IN ('queued', 'running', 'done', 'failed', 'cancelled')),
    cancel_requested boolean NOT NULL DEFAULT false,
    queued_utc timestamptz NOT NULL DEFAULT now(),
    started_utc timestamptz,
    finished_utc timestamptz,
    failure text,
    -- When the player showed the finished task; null while unread.
    read_utc timestamptz
);

CREATE INDEX analysis_tasks_recording ON analysis_tasks (recording_id, queued_utc);
CREATE INDEX analysis_tasks_open ON analysis_tasks (queued_utc)
    WHERE state IN ('queued', 'running');

CREATE TABLE analysis_attempts (
    attempt_id uuid PRIMARY KEY DEFAULT uuidv7(),
    task_id uuid NOT NULL REFERENCES analysis_tasks (task_id) ON DELETE CASCADE,
    step_key text NOT NULL CHECK (length(step_key) > 0),
    attempt_number integer NOT NULL CHECK (attempt_number > 0),
    started_utc timestamptz NOT NULL,
    finished_utc timestamptz,
    outcome text NOT NULL
        CHECK (outcome IN ('running', 'accepted', 'failed', 'stopped-for-recording', 'cancelled')),
    failure text,
    -- What produced the attempt.
    profile_name text NOT NULL,
    profile jsonb NOT NULL,
    endpoint text NOT NULL,
    model text NOT NULL,
    model_file text,
    quantization text,
    model_sha256 text,
    -- hashed: computed from the file by the worker; stated: as the profile
    -- states it, not checked; unknown: neither.
    model_sha256_basis text NOT NULL CHECK (model_sha256_basis IN ('hashed', 'stated', 'unknown')),
    server_build text,
    parameters jsonb NOT NULL,
    prompt_template text NOT NULL,
    prompt_template_version integer NOT NULL,
    input_sha256 text NOT NULL,
    input_references text[] NOT NULL,
    prompt_tokens integer,
    completion_tokens integer,
    prompt_milliseconds double precision,
    completion_milliseconds double precision,
    finish_reason text,
    -- The model's output exactly as the server returned it.
    raw_output text,
    -- The parsed output of an accepted attempt.
    output jsonb,
    UNIQUE (task_id, step_key, attempt_number)
);

CREATE UNIQUE INDEX analysis_attempts_accepted ON analysis_attempts (task_id, step_key)
    WHERE outcome = 'accepted';

CREATE TABLE analysis_claims (
    claim_id uuid PRIMARY KEY DEFAULT uuidv7(),
    attempt_id uuid NOT NULL REFERENCES analysis_attempts (attempt_id) ON DELETE CASCADE,
    section text NOT NULL,
    paragraph integer NOT NULL CHECK (paragraph >= 0),
    ordinal integer NOT NULL CHECK (ordinal >= 0),
    -- claim: a statement resting on the evidence it cites; unknown: what the
    -- evidence does not say.
    claim_kind text NOT NULL CHECK (claim_kind IN ('claim', 'unknown')),
    text text NOT NULL,
    evidence text[] NOT NULL,
    UNIQUE (attempt_id, section, paragraph, ordinal)
);

CREATE TABLE analysis_reviews (
    review_id uuid PRIMARY KEY DEFAULT uuidv7(),
    claim_id uuid NOT NULL REFERENCES analysis_claims (claim_id) ON DELETE CASCADE,
    decision text NOT NULL CHECK (decision IN ('accepted', 'rejected', 'corrected')),
    corrected_text text,
    reviewer text NOT NULL,
    reviewed_utc timestamptz NOT NULL DEFAULT now(),
    CHECK ((decision = 'corrected') = (corrected_text IS NOT NULL))
);

CREATE TABLE analysis_reports (
    report_id uuid PRIMARY KEY DEFAULT uuidv7(),
    task_id uuid NOT NULL REFERENCES analysis_tasks (task_id) ON DELETE CASCADE,
    file_name text NOT NULL,
    -- The file's path under the session folder.
    relative_path text NOT NULL,
    sha256 text NOT NULL,
    written_utc timestamptz NOT NULL DEFAULT now()
);

-- Model files the worker has hashed, so a file is hashed again only when
-- its size or change time is different.
CREATE TABLE analysis_model_file_hashes (
    path text PRIMARY KEY,
    length bigint NOT NULL,
    last_write_utc timestamptz NOT NULL,
    sha256 text NOT NULL,
    hashed_utc timestamptz NOT NULL DEFAULT now()
);
