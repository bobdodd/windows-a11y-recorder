-- The keyboard hook collector's records, on the input.keyboard-hook
-- channel: hook-keyboard, each key its low-level keyboard hook was called
-- with, including the keys a screen reader keeps, which never reach raw
-- input; and hook-installed, each installation of the hook with its
-- reason, the previous installation's keys and longest callback, and, for
-- an installation after a loss, the raw input key that showed it. The
-- evidence tables are not written for a recording that has a recording
-- file (see 0013_recording_files.sql); they keep mapping every payload
-- member so the database writer still accepts every record. See
-- docs/architecture/screen-reader-activity.md, "The keyboard hook".
--
-- Each table is made as 0009_unpartitioned_recording_tables.sql left the
-- other per-recording tables: an ordinary table keyed by recording and
-- event, whose references are listed in recording_references and checked
-- when the recording is completed, as since 0011_bulk_reference_checks.sql,
-- and not by a foreign key.

CREATE TABLE keyboard_hook_keys (
    recording_id uuid NOT NULL,
    event_key bigint NOT NULL,
    installation integer NOT NULL,
    virtual_key integer NOT NULL,
    scan_code integer NOT NULL,
    flags integer NOT NULL,
    up boolean NOT NULL,
    extended boolean NOT NULL,
    injected boolean NOT NULL,
    lower_integrity_injected boolean NOT NULL,
    alt_down boolean NOT NULL,
    extra_information bigint NOT NULL,
    event_time_milliseconds bigint NOT NULL,
    PRIMARY KEY (recording_id, event_key)
);

CREATE TABLE keyboard_hook_installations (
    recording_id uuid NOT NULL,
    event_key bigint NOT NULL,
    installation integer NOT NULL,
    reason text NOT NULL,
    installed boolean NOT NULL,
    problem text,
    previous_installation integer,
    previous_keys bigint,
    previous_max_callback_microseconds double precision,
    keys_dropped bigint NOT NULL,
    last_hook_key_at bigint,
    unmatched_raw_key_at bigint,
    unmatched_scan_code integer,
    PRIMARY KEY (recording_id, event_key)
);

INSERT INTO recording_tables (table_name, table_order)
    SELECT 'keyboard_hook_keys', max(table_order) + 1 FROM recording_tables;
INSERT INTO recording_tables (table_name, table_order)
    SELECT 'keyboard_hook_installations', max(table_order) + 1 FROM recording_tables;

INSERT INTO recording_references (table_name, columns, referenced_table, referenced_columns)
VALUES
    ('keyboard_hook_keys', '{recording_id,event_key}', 'events', '{recording_id,event_key}'),
    ('keyboard_hook_installations', '{recording_id,event_key}', 'events', '{recording_id,event_key}');
