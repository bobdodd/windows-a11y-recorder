-- Playback reads the timeline one event at a time, in time order and then
-- event key order, for each channel. This index holds that order, so each
-- lookup reads one index entry per channel instead of sorting every event
-- in a time range.
DROP INDEX events_channel_time;
CREATE INDEX events_channel_order
    ON events (recording_id, channel_id, monotonic_nanoseconds, event_key);
