-- Protocol 0.55 records, in a DOM walk, the frame each frame owner element
-- holds (dom-checkpoint-frame-owner), and each frame an owner is given or
-- loses (dom-frame-owner-changed). 0017_dom_walk_frames.sql added the started
-- record's new members but no tables for these two records, so the database
-- writer refused them as unmapped. The evidence tables are not written for a
-- recording that has a recording file (see 0013_recording_files.sql); they
-- keep mapping every payload member so the database writer still accepts
-- every record. See docs/architecture/page-recreation.md, "Slice 5".
--
-- Each table is made as 0009_unpartitioned_recording_tables.sql left the
-- other per-recording tables: an ordinary table keyed by recording and
-- event, whose references are listed in recording_references and checked
-- when the recording is completed, as since 0011_bulk_reference_checks.sql,
-- and not by a foreign key.

CREATE TABLE browser_dom_checkpoint_frame_owners (
    recording_id uuid NOT NULL,
    event_key bigint NOT NULL,
    context_key bigint NOT NULL,
    checkpoint_id text NOT NULL,
    owner_node_id bigint NOT NULL,
    frame_token text NOT NULL,
    frame_location_name_id integer NOT NULL,
    PRIMARY KEY (recording_id, event_key)
);

CREATE TABLE browser_dom_frame_owner_changes (
    recording_id uuid NOT NULL,
    event_key bigint NOT NULL,
    context_key bigint NOT NULL,
    owner_node_id bigint NOT NULL,
    frame_token text,
    frame_location_name_id integer,
    PRIMARY KEY (recording_id, event_key)
);

INSERT INTO recording_tables (table_name, table_order)
    SELECT 'browser_dom_checkpoint_frame_owners', max(table_order) + 1 FROM recording_tables;
INSERT INTO recording_tables (table_name, table_order)
    SELECT 'browser_dom_frame_owner_changes', max(table_order) + 1 FROM recording_tables;

INSERT INTO recording_references (table_name, columns, referenced_table, referenced_columns)
VALUES
    ('browser_dom_checkpoint_frame_owners', '{recording_id,event_key}', 'events', '{recording_id,event_key}'),
    ('browser_dom_checkpoint_frame_owners', '{recording_id,context_key}', 'browser_contexts', '{recording_id,identity_key}'),
    ('browser_dom_checkpoint_frame_owners', '{frame_location_name_id}', 'names', '{name_id}'),
    ('browser_dom_frame_owner_changes', '{recording_id,event_key}', 'events', '{recording_id,event_key}'),
    ('browser_dom_frame_owner_changes', '{recording_id,context_key}', 'browser_contexts', '{recording_id,identity_key}'),
    ('browser_dom_frame_owner_changes', '{frame_location_name_id}', 'names', '{name_id}');
