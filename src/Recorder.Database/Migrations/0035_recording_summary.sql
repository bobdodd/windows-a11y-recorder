-- The recording summary, the analysis runner's first task kind. See
-- docs/architecture/recording-summary.md.

-- The session folder the task reads the recording from and writes its
-- reports to, as the player gave it when queuing the task. The catalog
-- holds each recording by its session key, not by its folder.
ALTER TABLE analysis_tasks ADD COLUMN session_directory text;

-- The auditor's description of the participant's capabilities, an input to
-- the analysis, labelled reported when given to a model. A change adds a
-- row, so the earlier text is kept; a task uses the latest when it runs,
-- and its report records the text it used.
CREATE TABLE participant_capabilities (
    capability_id uuid PRIMARY KEY DEFAULT uuidv7(),
    recording_id uuid NOT NULL REFERENCES recordings (recording_id) ON DELETE CASCADE,
    description text NOT NULL CHECK (length(btrim(description)) > 0),
    written_utc timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX participant_capabilities_by_recording
    ON participant_capabilities (recording_id, written_utc DESC);
