-- The phone's frames, on graphics.android.frames: each picture decoded from
-- the stored stream android/screen.h264 whose pixels differ from the
-- picture before it, written as a PNG under frames/android (device-frame),
-- and why frames stopped being made while the stream went on being
-- recorded (frames-unavailable). The evidence tables are not written for a
-- recording that has a recording file (see 0013_recording_files.sql); they
-- keep mapping every payload member so the database writer still accepts
-- every record. See docs/architecture/android-implementation-plan.md,
-- slice 3, step 3b.
--
-- Each table is made as 0039_android_screen.sql made its tables: keyed by
-- recording and event, with its reference listed in recording_references
-- and checked when the recording is completed.

CREATE TABLE android_device_frames (
    recording_id uuid NOT NULL,
    event_key bigint NOT NULL,
    path text NOT NULL,
    width integer NOT NULL,
    height integer NOT NULL,
    session integer NOT NULL,
    packet_byte_offset bigint NOT NULL,
    presentation_microseconds bigint NOT NULL,
    byte_length bigint NOT NULL,
    pixel_format text NOT NULL,
    encoded_format text NOT NULL,
    colour_matrix text NOT NULL,
    colour_range text NOT NULL,
    chroma_upsampling text NOT NULL,
    decoder text NOT NULL,
    decoded_width integer NOT NULL,
    decoded_height integer NOT NULL,
    repeats_before bigint NOT NULL,
    written_at_nanoseconds bigint NOT NULL,
    PRIMARY KEY (recording_id, event_key)
);

CREATE TABLE android_frame_unavailabilities (
    recording_id uuid NOT NULL,
    event_key bigint NOT NULL,
    reason text NOT NULL,
    PRIMARY KEY (recording_id, event_key)
);

INSERT INTO recording_tables (table_name, table_order)
    SELECT 'android_device_frames', max(table_order) + 1 FROM recording_tables;
INSERT INTO recording_tables (table_name, table_order)
    SELECT 'android_frame_unavailabilities', max(table_order) + 1 FROM recording_tables;

INSERT INTO recording_references (table_name, columns, referenced_table, referenced_columns)
VALUES
    ('android_device_frames', '{recording_id,event_key}', 'events', '{recording_id,event_key}'),
    ('android_frame_unavailabilities', '{recording_id,event_key}', 'events', '{recording_id,event_key}');
