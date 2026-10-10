-- The assistive technology collector's records, on the
-- system.assistive-technology channel: assistive-technology-watch, what it
-- watches and how often; assistive-technology-process-started and -exited,
-- each process of a watched product, with Windows' own start and exit
-- times; assistive-technology-module-loaded and -unloaded, each of its
-- modules seen in or gone from the instrumented Chromium's processes, in
-- one table, the event type saying which; and assistive-technology-sound-
-- started and -ended, each period of sound from its audio sessions' peak
-- meters. The watch record's lists have a child table each, one row an
-- entry. The evidence tables are not written for a recording that has a
-- recording file (see 0013_recording_files.sql); they keep mapping every
-- payload member so the database writer still accepts every record. See
-- docs/architecture/screen-reader-activity.md, "Tracking NVDA".
--
-- Each table is made as 0009_unpartitioned_recording_tables.sql left the
-- other per-recording tables: an ordinary table keyed by recording and
-- event, whose references are listed in recording_references and checked
-- when the recording is completed, as since 0011_bulk_reference_checks.sql,
-- and not by a foreign key.

CREATE TABLE assistive_technology_watches (
    recording_id uuid NOT NULL,
    event_key bigint NOT NULL,
    process_poll_milliseconds integer NOT NULL,
    module_poll_milliseconds integer NOT NULL,
    sound_sample_milliseconds integer NOT NULL,
    sound_threshold double precision NOT NULL,
    sound_gap_milliseconds integer NOT NULL,
    browser_executable_path text,
    sound_problem text,
    PRIMARY KEY (recording_id, event_key)
);

CREATE TABLE assistive_technology_watch_products (
    recording_id uuid NOT NULL,
    owner_key bigint NOT NULL,
    ordinal_1 integer NOT NULL,
    value text NOT NULL,
    PRIMARY KEY (recording_id, owner_key, ordinal_1)
);

CREATE TABLE assistive_technology_watch_executables (
    recording_id uuid NOT NULL,
    owner_key bigint NOT NULL,
    ordinal_1 integer NOT NULL,
    value text NOT NULL,
    PRIMARY KEY (recording_id, owner_key, ordinal_1)
);

CREATE TABLE assistive_technology_watch_modules (
    recording_id uuid NOT NULL,
    owner_key bigint NOT NULL,
    ordinal_1 integer NOT NULL,
    value text NOT NULL,
    PRIMARY KEY (recording_id, owner_key, ordinal_1)
);

CREATE TABLE assistive_technology_process_starts (
    recording_id uuid NOT NULL,
    event_key bigint NOT NULL,
    product text NOT NULL,
    role text NOT NULL,
    basis text NOT NULL,
    executable_path text,
    file_version text,
    product_version text,
    process_id bigint NOT NULL,
    parent_process_id bigint,
    started_utc timestamptz,
    started_utc_tick smallint,
    running_at_start boolean NOT NULL,
    copy text NOT NULL,
    problem text,
    PRIMARY KEY (recording_id, event_key)
);

CREATE TABLE assistive_technology_process_exits (
    recording_id uuid NOT NULL,
    event_key bigint NOT NULL,
    product text NOT NULL,
    role text NOT NULL,
    process_id bigint NOT NULL,
    started_utc timestamptz,
    started_utc_tick smallint,
    exited_utc timestamptz,
    exited_utc_tick smallint,
    exit_code bigint,
    PRIMARY KEY (recording_id, event_key)
);

CREATE TABLE assistive_technology_module_changes (
    recording_id uuid NOT NULL,
    event_key bigint NOT NULL,
    product text NOT NULL,
    module_name text NOT NULL,
    module_path text NOT NULL,
    file_version text,
    host_process_id bigint NOT NULL,
    host_executable_path text NOT NULL,
    host_exited boolean NOT NULL,
    PRIMARY KEY (recording_id, event_key)
);

CREATE TABLE assistive_technology_sound_starts (
    recording_id uuid NOT NULL,
    event_key bigint NOT NULL,
    product text NOT NULL,
    process_id bigint NOT NULL,
    peak double precision NOT NULL,
    PRIMARY KEY (recording_id, event_key)
);

CREATE TABLE assistive_technology_sound_ends (
    recording_id uuid NOT NULL,
    event_key bigint NOT NULL,
    product text NOT NULL,
    process_id bigint NOT NULL,
    started_at bigint NOT NULL,
    last_sound_at bigint NOT NULL,
    max_peak double precision NOT NULL,
    ended_by text NOT NULL,
    PRIMARY KEY (recording_id, event_key)
);

INSERT INTO recording_tables (table_name, table_order)
    SELECT 'assistive_technology_watches', max(table_order) + 1 FROM recording_tables;
INSERT INTO recording_tables (table_name, table_order)
    SELECT 'assistive_technology_watch_products', max(table_order) + 1 FROM recording_tables;
INSERT INTO recording_tables (table_name, table_order)
    SELECT 'assistive_technology_watch_executables', max(table_order) + 1 FROM recording_tables;
INSERT INTO recording_tables (table_name, table_order)
    SELECT 'assistive_technology_watch_modules', max(table_order) + 1 FROM recording_tables;
INSERT INTO recording_tables (table_name, table_order)
    SELECT 'assistive_technology_process_starts', max(table_order) + 1 FROM recording_tables;
INSERT INTO recording_tables (table_name, table_order)
    SELECT 'assistive_technology_process_exits', max(table_order) + 1 FROM recording_tables;
INSERT INTO recording_tables (table_name, table_order)
    SELECT 'assistive_technology_module_changes', max(table_order) + 1 FROM recording_tables;
INSERT INTO recording_tables (table_name, table_order)
    SELECT 'assistive_technology_sound_starts', max(table_order) + 1 FROM recording_tables;
INSERT INTO recording_tables (table_name, table_order)
    SELECT 'assistive_technology_sound_ends', max(table_order) + 1 FROM recording_tables;

INSERT INTO recording_references (table_name, columns, referenced_table, referenced_columns)
VALUES
    ('assistive_technology_watches', '{recording_id,event_key}', 'events', '{recording_id,event_key}'),
    ('assistive_technology_watch_products', '{recording_id,owner_key}', 'assistive_technology_watches', '{recording_id,event_key}'),
    ('assistive_technology_watch_executables', '{recording_id,owner_key}', 'assistive_technology_watches', '{recording_id,event_key}'),
    ('assistive_technology_watch_modules', '{recording_id,owner_key}', 'assistive_technology_watches', '{recording_id,event_key}'),
    ('assistive_technology_process_starts', '{recording_id,event_key}', 'events', '{recording_id,event_key}'),
    ('assistive_technology_process_exits', '{recording_id,event_key}', 'events', '{recording_id,event_key}'),
    ('assistive_technology_module_changes', '{recording_id,event_key}', 'events', '{recording_id,event_key}'),
    ('assistive_technology_sound_starts', '{recording_id,event_key}', 'events', '{recording_id,event_key}'),
    ('assistive_technology_sound_ends', '{recording_id,event_key}', 'events', '{recording_id,event_key}');
