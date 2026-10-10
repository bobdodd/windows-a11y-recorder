-- The records, on the window.foreground channel, of when keyboard and
-- mouse input could not be recorded: recorder-integrity, the recorder's own
-- integrity level, once at the start; foreground-integrity, the foreground
-- window's process's integrity level, written with each foreground-window
-- record, and whether input to it can be recorded; and input-desktop, the
-- desktop that receives input, at the start and at each switch. Windows
-- gives input to a window of a higher integrity level than the recorder's,
-- and on another desktop such as the secure desktop, to neither the
-- recorder's keyboard hook nor its raw input. The evidence tables are not
-- written for a recording that has a recording file (see
-- 0013_recording_files.sql); they keep mapping every payload member so the
-- database writer still accepts every record. See
-- docs/architecture/screen-reader-activity.md, "Input the recorder cannot
-- receive".
--
-- Each table is made as 0009_unpartitioned_recording_tables.sql left the
-- other per-recording tables: an ordinary table keyed by recording and
-- event, whose references are listed in recording_references and checked
-- when the recording is completed, as since 0011_bulk_reference_checks.sql,
-- and not by a foreign key.

CREATE TABLE recorder_integrities (
    recording_id uuid NOT NULL,
    event_key bigint NOT NULL,
    process_id bigint NOT NULL,
    integrity_level text,
    integrity_rid integer,
    ui_access boolean,
    problem text,
    PRIMARY KEY (recording_id, event_key)
);

CREATE TABLE foreground_integrities (
    recording_id uuid NOT NULL,
    event_key bigint NOT NULL,
    window_handle bigint NOT NULL,
    process_id bigint NOT NULL,
    process_name text,
    integrity_level text,
    integrity_rid integer,
    ui_access boolean,
    problem text,
    input_recordable boolean,
    PRIMARY KEY (recording_id, event_key)
);

CREATE TABLE input_desktops (
    recording_id uuid NOT NULL,
    event_key bigint NOT NULL,
    reason text NOT NULL,
    desktop_name text,
    problem text,
    input_recordable boolean NOT NULL,
    PRIMARY KEY (recording_id, event_key)
);

INSERT INTO recording_tables (table_name, table_order)
    SELECT 'recorder_integrities', max(table_order) + 1 FROM recording_tables;
INSERT INTO recording_tables (table_name, table_order)
    SELECT 'foreground_integrities', max(table_order) + 1 FROM recording_tables;
INSERT INTO recording_tables (table_name, table_order)
    SELECT 'input_desktops', max(table_order) + 1 FROM recording_tables;

INSERT INTO recording_references (table_name, columns, referenced_table, referenced_columns)
VALUES
    ('recorder_integrities', '{recording_id,event_key}', 'events', '{recording_id,event_key}'),
    ('foreground_integrities', '{recording_id,event_key}', 'events', '{recording_id,event_key}'),
    ('input_desktops', '{recording_id,event_key}', 'events', '{recording_id,event_key}');
