-- The mouse hook's records, on the input.mouse-hook channel, written by
-- the keyboard hook collector from its low-level mouse hook: hook-mouse,
-- each event the hook was called with, moves included; and
-- hook-mouse-installed, each installation of the hook with its reason, the
-- previous installation's events and longest callback, and, for an
-- installation after a loss, the raw input button that showed it. And
-- NVDA's [mouse] settings, added to its settings record. The evidence
-- tables are not written for a recording that has a recording file (see
-- 0013_recording_files.sql); they keep mapping every payload member so the
-- database writer still accepts every record. See
-- docs/architecture/screen-reader-activity.md, "The mouse hook".
--
-- Each table is made as 0009_unpartitioned_recording_tables.sql left the
-- other per-recording tables: an ordinary table keyed by recording and
-- event, whose references are listed in recording_references and checked
-- when the recording is completed, as since 0011_bulk_reference_checks.sql,
-- and not by a foreign key.

CREATE TABLE mouse_hook_events (
    recording_id uuid NOT NULL,
    event_key bigint NOT NULL,
    installation integer NOT NULL,
    message integer NOT NULL,
    kind text NOT NULL,
    button text,
    x integer NOT NULL,
    y integer NOT NULL,
    mouse_data integer NOT NULL,
    wheel_delta integer,
    flags integer NOT NULL,
    injected boolean NOT NULL,
    lower_integrity_injected boolean NOT NULL,
    pen_or_touch text,
    extra_information bigint NOT NULL,
    event_time_milliseconds bigint NOT NULL,
    PRIMARY KEY (recording_id, event_key)
);

CREATE TABLE mouse_hook_installations (
    recording_id uuid NOT NULL,
    event_key bigint NOT NULL,
    installation integer NOT NULL,
    reason text NOT NULL,
    installed boolean NOT NULL,
    problem text,
    previous_installation integer,
    previous_events bigint,
    previous_max_callback_microseconds double precision,
    events_dropped bigint NOT NULL,
    last_hook_event_at bigint,
    unmatched_raw_button_at bigint,
    unmatched_button integer,
    PRIMARY KEY (recording_id, event_key)
);

INSERT INTO recording_tables (table_name, table_order)
    SELECT 'mouse_hook_events', max(table_order) + 1 FROM recording_tables;
INSERT INTO recording_tables (table_name, table_order)
    SELECT 'mouse_hook_installations', max(table_order) + 1 FROM recording_tables;

INSERT INTO recording_references (table_name, columns, referenced_table, referenced_columns)
VALUES
    ('mouse_hook_events', '{recording_id,event_key}', 'events', '{recording_id,event_key}'),
    ('mouse_hook_installations', '{recording_id,event_key}', 'events', '{recording_id,event_key}');

ALTER TABLE assistive_technology_settings
    ADD COLUMN enable_mouse_tracking text,
    ADD COLUMN ignore_injected_mouse_input text,
    ADD COLUMN mouse_text_unit text,
    ADD COLUMN report_object_role_on_mouse_enter text,
    ADD COLUMN audio_coordinates_on_mouse_move text,
    ADD COLUMN report_mouse_shape_changes text;
