-- Retires the transitional event_payloads_unmapped table. Every event type
-- of the recorder's built-in channels now has evidence tables, so the only
-- payloads left without one are those of channels the recorder does not
-- define. The table is kept for them alone, under a name that says so.
--
-- A recording made before its event types had evidence tables still holds
-- built-in payloads in this table. Such a recording is removed, in the way
-- RecordingStore.DeleteRecordingAsync removes one: each of its partitions is
-- detached and dropped, referring tables first, and then its row is
-- deleted. Its session folder is not touched.
DO $$
DECLARE
    removals uuid[];
    removed uuid;
    registered record;
    partition text;
BEGIN
    -- Collected first, because a partition cannot be detached while a query
    -- over its table is open.
    SELECT coalesce(array_agg(DISTINCT p.recording_id), '{}')
    INTO removals
        FROM event_payloads_unmapped p
        JOIN events e ON e.recording_id = p.recording_id AND e.event_key = p.event_key
        JOIN channels c ON c.channel_id = e.channel_id
        WHERE c.name IN (
            'collector.lifecycle', 'session.annotations', 'input.keyboard', 'input.mouse',
            'window.foreground', 'accessibility.uia.events', 'graphics.desktop.frames',
            'audio.microphone', 'audio.system', 'browser.lifecycle', 'browser.listener',
            'browser.dispatch', 'browser.timer', 'browser.scheduler', 'browser.navigation',
            'browser.dom', 'browser.accessibility', 'browser.cookie', 'browser.interaction',
            'browser.layout', 'browser.presentation', 'browser.network');

    FOREACH removed IN ARRAY removals
    LOOP
        FOR registered IN
            SELECT table_name, partition_prefix
            FROM recording_partitioned_tables
            ORDER BY partition_order DESC
        LOOP
            partition := registered.partition_prefix || '_' || replace(removed::text, '-', '');
            IF to_regclass(partition) IS NOT NULL THEN
                EXECUTE format('ALTER TABLE %I DETACH PARTITION %I', registered.table_name, partition);
                EXECUTE format('DROP TABLE %I', partition);
            END IF;
        END LOOP;

        DELETE FROM recordings WHERE recording_id = removed;
    END LOOP;
END
$$;

-- Holds the payload of an event on a channel the recorder does not define.
-- The writer refuses an event of a built-in channel that has no evidence
-- table, so no built-in payload is stored here.
ALTER TABLE event_payloads_unmapped RENAME TO event_payloads_other_channels;

-- The table's partitions and their indexes follow its new prefix, so each
-- name still says which table it belongs to.
DO $$
DECLARE
    existing record;
BEGIN
    FOR existing IN
        SELECT c.relname, c.relkind
        FROM pg_class c
        WHERE c.relname LIKE 'evpu\_%' OR c.relname LIKE 'event\_payloads\_unmapped\_%'
    LOOP
        EXECUTE format(
            CASE WHEN existing.relkind IN ('i', 'I') THEN 'ALTER INDEX %I RENAME TO %I' ELSE 'ALTER TABLE %I RENAME TO %I' END,
            existing.relname,
            CASE
                WHEN existing.relname LIKE 'evpu\_%'
                    THEN 'evpo_' || substr(existing.relname, length('evpu_') + 1)
                ELSE 'event_payloads_other_channels_' || substr(existing.relname, length('event_payloads_unmapped_') + 1)
            END);
    END LOOP;
END
$$;

UPDATE recording_partitioned_tables
SET table_name = 'event_payloads_other_channels', partition_prefix = 'evpo'
WHERE table_name = 'event_payloads_unmapped';
