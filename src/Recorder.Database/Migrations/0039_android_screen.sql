-- The Android screen collector's records, on graphics.android.video: the
-- screen stream's start (video-stream), each capture session, a new one at
-- each change of the picture's size, as when the phone turns
-- (video-session), each packet's place in the stream file
-- android/screen.h264 with its flags and phone time (video-packet), and why
-- the phone's screen is missing from part or all of a recording
-- (video-unavailable). The evidence tables are not written for a recording
-- that has a recording file (see 0013_recording_files.sql); they keep
-- mapping every payload member so the database writer still accepts every
-- record. See docs/architecture/android-implementation-plan.md, slice 3.
--
-- Each table is made as 0009_unpartitioned_recording_tables.sql left the
-- other per-recording tables: an ordinary table keyed by recording and
-- event, whose references are listed in recording_references and checked
-- when the recording is completed, as since 0011_bulk_reference_checks.sql,
-- and not by a foreign key.

CREATE TABLE android_video_streams (
    recording_id uuid NOT NULL,
    event_key bigint NOT NULL,
    serial text NOT NULL,
    server_version text NOT NULL,
    codec text NOT NULL,
    video_bit_rate integer NOT NULL,
    path text NOT NULL,
    PRIMARY KEY (recording_id, event_key)
);

CREATE TABLE android_video_sessions (
    recording_id uuid NOT NULL,
    event_key bigint NOT NULL,
    session integer NOT NULL,
    width integer NOT NULL,
    height integer NOT NULL,
    client_resized boolean NOT NULL,
    byte_offset bigint NOT NULL,
    PRIMARY KEY (recording_id, event_key)
);

CREATE TABLE android_video_packets (
    recording_id uuid NOT NULL,
    event_key bigint NOT NULL,
    session integer NOT NULL,
    byte_offset bigint NOT NULL,
    size integer NOT NULL,
    presentation_microseconds bigint,
    config boolean NOT NULL,
    key_frame boolean NOT NULL,
    received_at_nanoseconds bigint NOT NULL,
    PRIMARY KEY (recording_id, event_key)
);

CREATE TABLE android_video_unavailabilities (
    recording_id uuid NOT NULL,
    event_key bigint NOT NULL,
    serial text NOT NULL,
    reason text NOT NULL,
    connection_lost boolean NOT NULL,
    PRIMARY KEY (recording_id, event_key)
);

INSERT INTO recording_tables (table_name, table_order)
    SELECT 'android_video_streams', max(table_order) + 1 FROM recording_tables;
INSERT INTO recording_tables (table_name, table_order)
    SELECT 'android_video_sessions', max(table_order) + 1 FROM recording_tables;
INSERT INTO recording_tables (table_name, table_order)
    SELECT 'android_video_packets', max(table_order) + 1 FROM recording_tables;
INSERT INTO recording_tables (table_name, table_order)
    SELECT 'android_video_unavailabilities', max(table_order) + 1 FROM recording_tables;

INSERT INTO recording_references (table_name, columns, referenced_table, referenced_columns)
VALUES
    ('android_video_streams', '{recording_id,event_key}', 'events', '{recording_id,event_key}'),
    ('android_video_sessions', '{recording_id,event_key}', 'events', '{recording_id,event_key}'),
    ('android_video_packets', '{recording_id,event_key}', 'events', '{recording_id,event_key}'),
    ('android_video_unavailabilities', '{recording_id,event_key}', 'events', '{recording_id,event_key}');
