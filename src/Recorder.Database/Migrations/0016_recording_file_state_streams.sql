-- Since page recreation slice 2, a recording file holds three streams that
-- 0013_recording_files.sql did not allow: browser-state, the records a
-- document's state is rebuilt from; state, the state thread's snapshots; and
-- state-index, its index records. The chunk index of every such file was
-- refused, and the refusal was reported with the database status. See
-- docs/architecture/change-driven-recording.md.
ALTER TABLE recording_file_chunks DROP CONSTRAINT recording_file_chunks_stream_check;

ALTER TABLE recording_file_chunks ADD CONSTRAINT recording_file_chunks_stream_check
    CHECK (stream IN ('browser', 'browser-state', 'desktop', 'media', 'recorder', 'state', 'state-index'));
