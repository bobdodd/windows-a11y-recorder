-- Each recording's events are stored in a recording file of its own, in the
-- MCAP container format, beside its frames and audio. The database keeps the
-- file's location and an index of its chunks; the file is the evidence and
-- the index can be rebuilt from it. See
-- docs/architecture/change-driven-recording.md.
--
-- The evidence tables of the earlier migrations are left in place and are
-- not written for a recording that has a recording file.

CREATE TABLE recording_files (
    recording_id uuid PRIMARY KEY REFERENCES recordings (recording_id)
        ON DELETE CASCADE,
    path text NOT NULL CHECK (length(path) > 0),
    format text NOT NULL CHECK (format = 'mcap'),
    -- Set when the file's summary and footer have been written.
    finished boolean NOT NULL DEFAULT false,
    byte_length bigint CHECK (byte_length >= 0),
    message_count bigint CHECK (message_count >= 0),
    chunk_count integer CHECK (chunk_count >= 0),
    message_start_time bigint,
    message_end_time bigint
);

-- One row per chunk of a recording file, in file order. A chunk holds the
-- messages of one stream. Times are session nanoseconds.
CREATE TABLE recording_file_chunks (
    recording_id uuid NOT NULL REFERENCES recording_files (recording_id)
        ON DELETE CASCADE,
    chunk_ordinal integer NOT NULL CHECK (chunk_ordinal >= 0),
    stream text NOT NULL CHECK (stream IN ('browser', 'desktop', 'media', 'recorder')),
    message_start_time bigint NOT NULL,
    message_end_time bigint NOT NULL CHECK (message_end_time >= message_start_time),
    message_count bigint NOT NULL CHECK (message_count >= 0),
    chunk_offset bigint NOT NULL CHECK (chunk_offset >= 0),
    chunk_length bigint NOT NULL CHECK (chunk_length > 0),
    compressed_size bigint NOT NULL CHECK (compressed_size >= 0),
    uncompressed_size bigint NOT NULL CHECK (uncompressed_size >= 0),
    PRIMARY KEY (recording_id, chunk_ordinal)
);

CREATE INDEX recording_file_chunks_time
    ON recording_file_chunks (recording_id, stream, message_start_time);
