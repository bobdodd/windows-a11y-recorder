-- The record, on the system.assistive-technology channel, of NVDA's
-- settings that its key commands depend on, read without change from its
-- configuration folder when its main process is seen: the keyboard layout,
-- the NVDA modifier keys, the multiple press timeout, the browse mode
-- options, each as written or null where NVDA's default applies; the names
-- of its configuration profiles, whether profile triggers are set, and the
-- custom key commands of gestures.ini. The evidence tables are not written
-- for a recording that has a recording file (see 0013_recording_files.sql);
-- they keep mapping every payload member so the database writer still
-- accepts every record. See docs/architecture/screen-reader-activity.md,
-- "2c-1, the commands".
--
-- Each table is made as 0009_unpartitioned_recording_tables.sql left the
-- other per-recording tables: an ordinary table keyed by recording and
-- event, whose references are listed in recording_references and checked
-- when the recording is completed, as since 0011_bulk_reference_checks.sql,
-- and not by a foreign key.

CREATE TABLE assistive_technology_settings (
    recording_id uuid NOT NULL,
    event_key bigint NOT NULL,
    product text NOT NULL,
    process_id bigint NOT NULL,
    copy text NOT NULL,
    config_folder text,
    read boolean NOT NULL,
    problem text,
    keyboard_layout text,
    nvda_modifier_keys text,
    multi_press_timeout text,
    auto_pass_through_on_focus_change text,
    auto_pass_through_on_caret_move text,
    trap_non_command_gestures text,
    enable_on_page_load text,
    profile_triggers boolean NOT NULL,
    gestures_problem text,
    PRIMARY KEY (recording_id, event_key)
);

CREATE TABLE assistive_technology_setting_profiles (
    recording_id uuid NOT NULL,
    owner_key bigint NOT NULL,
    ordinal_1 integer NOT NULL,
    value text NOT NULL,
    PRIMARY KEY (recording_id, owner_key, ordinal_1)
);

CREATE TABLE assistive_technology_setting_gestures (
    recording_id uuid NOT NULL,
    owner_key bigint NOT NULL,
    ordinal_1 integer NOT NULL,
    section text NOT NULL,
    script text NOT NULL,
    gesture text NOT NULL,
    PRIMARY KEY (recording_id, owner_key, ordinal_1)
);

INSERT INTO recording_tables (table_name, table_order)
    SELECT 'assistive_technology_settings', max(table_order) + 1 FROM recording_tables;
INSERT INTO recording_tables (table_name, table_order)
    SELECT 'assistive_technology_setting_profiles', max(table_order) + 1 FROM recording_tables;
INSERT INTO recording_tables (table_name, table_order)
    SELECT 'assistive_technology_setting_gestures', max(table_order) + 1 FROM recording_tables;

INSERT INTO recording_references (table_name, columns, referenced_table, referenced_columns)
VALUES
    ('assistive_technology_settings', '{recording_id,event_key}', 'events', '{recording_id,event_key}'),
    ('assistive_technology_setting_profiles', '{recording_id,owner_key}', 'assistive_technology_settings', '{recording_id,event_key}'),
    ('assistive_technology_setting_gestures', '{recording_id,owner_key}', 'assistive_technology_settings', '{recording_id,event_key}');
