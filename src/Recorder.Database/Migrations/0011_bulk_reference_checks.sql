-- Stores the visible path indexes of a dispatch path scope as one array
-- column, and checks the references between per-recording tables once per
-- recording instead of once per row.
--
-- On a 69 second browser recording on the target Windows machine, the
-- writer stored about 3,000 events a second while the browser produced
-- more. Of the time its batches spent writing, copying rows took about 45
-- seconds and committing took 178 seconds, most of it the foreign key
-- checks the writer defers to commit: PostgreSQL checks a foreign key with
-- one lookup of the referenced row for each inserted row. About 6.7 million
-- of the 8.9 million rows stored were visible path indexes, one row per
-- index. See docs/architecture/session-database.md.
--
-- 1. browser_dispatch_path_scopes.visible_path_indexes holds each scope's
--    visible path indexes in array order, and the table of one row per
--    index is dropped. The payload read back is the payload stored.
-- 2. recording_references lists each reference between per-recording
--    tables, and from them to names, that a foreign key checked. Those
--    foreign keys are dropped. RecordingStore.CheckReferencesAsync checks
--    every listed reference of a recording with one set-based query each
--    when the recording is completed, and a recording with a row that
--    refers to a missing row is stored as failed, with the reason. Foreign
--    keys to the lookup tables, such as channels and event types, and to
--    recordings, are kept.

ALTER TABLE browser_dispatch_path_scopes ADD COLUMN visible_path_indexes integer[];

UPDATE browser_dispatch_path_scopes s
    SET visible_path_indexes = v.indexes
    FROM (
        SELECT recording_id, owner_key, ordinal_1, array_agg(value ORDER BY ordinal_2) AS indexes
        FROM browser_dispatch_path_scope_visible_indexes
        GROUP BY recording_id, owner_key, ordinal_1
    ) v
    WHERE v.recording_id = s.recording_id AND v.owner_key = s.owner_key AND v.ordinal_1 = s.ordinal_1;

-- A scope with no visible indexes had no rows.
UPDATE browser_dispatch_path_scopes SET visible_path_indexes = '{}' WHERE visible_path_indexes IS NULL;
ALTER TABLE browser_dispatch_path_scopes ALTER COLUMN visible_path_indexes SET NOT NULL;

DROP TABLE browser_dispatch_path_scope_visible_indexes;
DELETE FROM recording_tables WHERE table_name = 'browser_dispatch_path_scope_visible_indexes';

-- Each reference a per-recording table makes to another per-recording
-- table or to names: the referring columns, and the referenced table and
-- columns, in the same order. A table removed from recording_tables takes
-- its references with it.
CREATE TABLE recording_references (
    table_name text NOT NULL REFERENCES recording_tables (table_name) ON DELETE CASCADE,
    columns text[] NOT NULL,
    referenced_table text NOT NULL,
    referenced_columns text[] NOT NULL,
    PRIMARY KEY (table_name, columns, referenced_table),
    CHECK (cardinality(columns) = cardinality(referenced_columns))
);

INSERT INTO recording_references (table_name, columns, referenced_table, referenced_columns)
    SELECT c.conrelid::regclass::text,
           (SELECT array_agg(a.attname::text ORDER BY k.ordinality)
            FROM unnest(c.conkey) WITH ORDINALITY k(attnum, ordinality)
            JOIN pg_attribute a ON a.attrelid = c.conrelid AND a.attnum = k.attnum),
           c.confrelid::regclass::text,
           (SELECT array_agg(a.attname::text ORDER BY k.ordinality)
            FROM unnest(c.confkey) WITH ORDINALITY k(attnum, ordinality)
            JOIN pg_attribute a ON a.attrelid = c.confrelid AND a.attnum = k.attnum)
    FROM pg_constraint c
    WHERE c.contype = 'f'
      AND c.conrelid::regclass::text IN (SELECT table_name FROM recording_tables)
      AND (c.confrelid::regclass::text IN (SELECT table_name FROM recording_tables)
           OR c.confrelid = 'names'::regclass);

DO $$
DECLARE
    saved record;
BEGIN
    FOR saved IN
        SELECT c.conrelid::regclass::text AS table_name, c.conname AS constraint_name
        FROM pg_constraint c
        WHERE c.contype = 'f'
          AND c.conrelid::regclass::text IN (SELECT table_name FROM recording_tables)
          AND (c.confrelid::regclass::text IN (SELECT table_name FROM recording_tables)
               OR c.confrelid = 'names'::regclass)
    LOOP
        EXECUTE format('ALTER TABLE %I DROP CONSTRAINT %I', saved.table_name, saved.constraint_name);
    END LOOP;
END
$$;
