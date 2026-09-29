-- Protocol 0.35 walks a document only for a reason of its own, recorded as
-- walkReason on DOM and layout checkpoint starts, and presents or snapshots a
-- rendering update that was not walked after its layout change set. The
-- evidence tables are not written for a recording that has a recording file
-- (see 0013_recording_files.sql); they keep mapping every payload member so
-- the database writer still accepts every record. See
-- docs/architecture/change-driven-recording.md.

-- As since 0011_bulk_reference_checks.sql, a reference to names is listed
-- in recording_references and checked when the recording is completed, not
-- by a foreign key.
ALTER TABLE browser_dom_checkpoint_starts ADD COLUMN walk_reason_name_id integer;

ALTER TABLE browser_layout_checkpoint_starts ADD COLUMN walk_reason_name_id integer;

INSERT INTO recording_references (table_name, columns, referenced_table, referenced_columns)
VALUES
    ('browser_dom_checkpoint_starts', '{walk_reason_name_id}', 'names', '{name_id}'),
    ('browser_layout_checkpoint_starts', '{walk_reason_name_id}', 'names', '{name_id}');

ALTER TABLE browser_interaction_checkpoint_starts
    ALTER COLUMN source_checkpoint_id DROP NOT NULL,
    ADD COLUMN source_change_set_id text;

ALTER TABLE browser_presentation_requests
    ALTER COLUMN layout_checkpoint_id DROP NOT NULL,
    ADD COLUMN layout_change_set_id text;
