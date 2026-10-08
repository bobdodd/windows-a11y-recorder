-- The Magnifier change records of a recording, on the graphics.magnifier
-- channel (magnifier-changed): one when a desktop frame's reading of the
-- full screen level, position, or color effect differs from the previous
-- frame's, at the later frame's time, with which of them changed, the
-- frame's sequence number, the previous frame's time, and both frames'
-- readings as 0019_desktop_frame_magnification.sql and
-- 0020_desktop_frame_color_effect.sql store them for a desktop frame. The
-- evidence table is not written for a recording that has a recording file
-- (see 0013_recording_files.sql); it keeps mapping every payload member so
-- the database writer still accepts every record. See
-- docs/architecture/accessibility-preferences.md.
--
-- The table is made as 0009_unpartitioned_recording_tables.sql left the
-- other per-recording tables: an ordinary table keyed by recording and
-- event, whose references are listed in recording_references and checked
-- when the recording is completed, as since 0011_bulk_reference_checks.sql,
-- and not by a foreign key.

CREATE TABLE magnifier_changes (
    recording_id uuid NOT NULL,
    event_key bigint NOT NULL,
    frame_sequence bigint NOT NULL,
    previous_frame_at bigint NOT NULL,
    changed_level boolean NOT NULL,
    changed_position boolean NOT NULL,
    changed_color_effect boolean NOT NULL,
    previous_fullscreen_magnification_level double precision,
    previous_fullscreen_magnification_x integer,
    previous_fullscreen_magnification_y integer,
    previous_fullscreen_magnification_problem text,
    previous_fullscreen_color_effect_matrix double precision[],
    previous_fullscreen_color_effect_problem text,
    current_fullscreen_magnification_level double precision,
    current_fullscreen_magnification_x integer,
    current_fullscreen_magnification_y integer,
    current_fullscreen_magnification_problem text,
    current_fullscreen_color_effect_matrix double precision[],
    current_fullscreen_color_effect_problem text,
    PRIMARY KEY (recording_id, event_key)
);

INSERT INTO recording_tables (table_name, table_order)
    SELECT 'magnifier_changes', max(table_order) + 1 FROM recording_tables;

INSERT INTO recording_references (table_name, columns, referenced_table, referenced_columns)
VALUES
    ('magnifier_changes', '{recording_id,event_key}', 'events', '{recording_id,event_key}');
