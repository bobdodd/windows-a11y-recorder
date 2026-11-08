-- The Android companion collector's records: android.companion, the
-- companion app's connection (companion-connected, with its greeting) and
-- why it could not be reached or was lost (companion-unavailable);
-- clock.android, each clock request and reply (clock-sample), from which
-- the phone's CLOCK_MONOTONIC is placed on the session clock; and
-- android.display, the phone display's rotation and size (display-state).
-- The evidence tables are not written for a recording that has a recording
-- file (see 0013_recording_files.sql); they keep mapping every payload
-- member so the database writer still accepts every record. See
-- docs/architecture/android-implementation-plan.md, slice 2.
--
-- Each table is made as 0009_unpartitioned_recording_tables.sql left the
-- other per-recording tables: an ordinary table keyed by recording and
-- event, whose references are listed in recording_references and checked
-- when the recording is completed, as since 0011_bulk_reference_checks.sql,
-- and not by a foreign key.

CREATE TABLE android_companion_connections (
    recording_id uuid NOT NULL,
    event_key bigint NOT NULL,
    serial text NOT NULL,
    protocol integer NOT NULL,
    version_name text NOT NULL,
    version_code bigint NOT NULL,
    android_release text NOT NULL,
    sdk integer NOT NULL,
    PRIMARY KEY (recording_id, event_key)
);

CREATE TABLE android_companion_unavailabilities (
    recording_id uuid NOT NULL,
    event_key bigint NOT NULL,
    serial text NOT NULL,
    reason text NOT NULL,
    connection_lost boolean NOT NULL,
    PRIMARY KEY (recording_id, event_key)
);

CREATE TABLE android_clock_samples (
    recording_id uuid NOT NULL,
    event_key bigint NOT NULL,
    segment integer NOT NULL,
    discontinuity boolean NOT NULL,
    recorder_send_nanoseconds bigint NOT NULL,
    phone_receive_nanoseconds bigint NOT NULL,
    phone_send_nanoseconds bigint NOT NULL,
    recorder_receive_nanoseconds bigint NOT NULL,
    error_bound_nanoseconds bigint NOT NULL,
    PRIMARY KEY (recording_id, event_key)
);

CREATE TABLE android_display_states (
    recording_id uuid NOT NULL,
    event_key bigint NOT NULL,
    reason text NOT NULL,
    rotation integer NOT NULL,
    mode_width integer NOT NULL,
    mode_height integer NOT NULL,
    PRIMARY KEY (recording_id, event_key)
);

INSERT INTO recording_tables (table_name, table_order)
    SELECT 'android_companion_connections', max(table_order) + 1 FROM recording_tables;
INSERT INTO recording_tables (table_name, table_order)
    SELECT 'android_companion_unavailabilities', max(table_order) + 1 FROM recording_tables;
INSERT INTO recording_tables (table_name, table_order)
    SELECT 'android_clock_samples', max(table_order) + 1 FROM recording_tables;
INSERT INTO recording_tables (table_name, table_order)
    SELECT 'android_display_states', max(table_order) + 1 FROM recording_tables;

INSERT INTO recording_references (table_name, columns, referenced_table, referenced_columns)
VALUES
    ('android_companion_connections', '{recording_id,event_key}', 'events', '{recording_id,event_key}'),
    ('android_companion_unavailabilities', '{recording_id,event_key}', 'events', '{recording_id,event_key}'),
    ('android_clock_samples', '{recording_id,event_key}', 'events', '{recording_id,event_key}'),
    ('android_display_states', '{recording_id,event_key}', 'events', '{recording_id,event_key}');
