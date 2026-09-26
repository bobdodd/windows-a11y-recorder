-- Replaces the per-recording partitions with ordinary tables.
--
-- Each recording had its own partition of every per-recording table, 135
-- tables in all. Every foreign key check on a row written to a partitioned
-- table grew slower with each recording in the database, until the writer
-- could not keep up with a recording. In an ordinary table each check is one
-- index lookup whatever the number of recordings.
--
-- This migration creates each per-recording table again as an ordinary
-- table with the same name, columns, checks, keys, indexes, and foreign
-- keys, under the same constraint and index names. The partitioned table is
-- renamed legacy_<order> and keeps its rows. Locking and dropping every
-- partition in one transaction needs more lock slots than the server has,
-- so the rows are moved afterwards, one recording per transaction, by
-- LegacyPartitions.MoveAsync, which DatabaseMigrator runs after the
-- migrations, and which then drops the legacy tables.
DO $$
DECLARE
    registered record;
    saved record;
    legacy text;
BEGIN
    CREATE TEMPORARY TABLE saved_foreign_keys ON COMMIT DROP AS
        SELECT c.conrelid::regclass::text AS table_name,
               c.conname AS constraint_name,
               pg_get_constraintdef(c.oid) AS definition
        FROM pg_constraint c
        WHERE c.contype = 'f'
          AND c.conparentid = 0
          AND c.conrelid::regclass::text IN (SELECT table_name FROM recording_partitioned_tables);

    -- A table outside the per-recording tables would keep referring to the
    -- legacy table, so none may refer to one.
    IF EXISTS (
        SELECT 1 FROM pg_constraint c
        WHERE c.contype = 'f'
          AND c.conparentid = 0
          AND c.confrelid::regclass::text IN (SELECT table_name FROM recording_partitioned_tables)
          AND c.conrelid::regclass::text NOT IN (SELECT table_name FROM recording_partitioned_tables)) THEN
        RAISE EXCEPTION 'A table outside recording_partitioned_tables refers to one inside it.';
    END IF;

    CREATE TEMPORARY TABLE saved_keys ON COMMIT DROP AS
        SELECT c.conrelid::regclass::text AS table_name,
               c.conname AS constraint_name,
               pg_get_constraintdef(c.oid) AS definition,
               c.contype = 'p' AS is_primary
        FROM pg_constraint c
        WHERE c.contype IN ('p', 'u')
          AND c.conparentid = 0
          AND c.conrelid::regclass::text IN (SELECT table_name FROM recording_partitioned_tables);

    CREATE TEMPORARY TABLE saved_indexes ON COMMIT DROP AS
        SELECT t.relname::text AS table_name,
               replace(pg_get_indexdef(i.indexrelid), ' ON ONLY ', ' ON ') AS definition
        FROM pg_index i
        JOIN pg_class t ON t.oid = i.indrelid
        WHERE t.relname::text IN (SELECT table_name FROM recording_partitioned_tables)
          AND NOT EXISTS (SELECT 1 FROM pg_constraint c WHERE c.conindid = i.indexrelid AND c.conrelid = i.indrelid);

    FOR registered IN
        SELECT table_name, partition_order FROM recording_partitioned_tables ORDER BY partition_order
    LOOP
        legacy := 'legacy_' || registered.partition_order;

        -- Index names are unique in the schema, so the legacy table's
        -- indexes give theirs up to the new table.
        FOR saved IN
            SELECT i.indexrelid AS index_oid, i.indexrelid::regclass::text AS index_name
            FROM pg_index i
            WHERE i.indrelid = registered.table_name::regclass
        LOOP
            EXECUTE format('ALTER INDEX %I RENAME TO %I', saved.index_name, 'legacy_index_' || saved.index_oid);
        END LOOP;

        EXECUTE format('ALTER TABLE %I RENAME TO %I', registered.table_name, legacy);
        EXECUTE format(
            'CREATE TABLE %I (LIKE %I INCLUDING DEFAULTS INCLUDING CONSTRAINTS INCLUDING STORAGE INCLUDING COMMENTS)',
            registered.table_name, legacy);

        FOR saved IN
            SELECT * FROM saved_keys WHERE table_name = registered.table_name ORDER BY is_primary DESC, constraint_name
        LOOP
            EXECUTE format('ALTER TABLE %I ADD CONSTRAINT %I %s', saved.table_name, saved.constraint_name, saved.definition);
        END LOOP;

        FOR saved IN SELECT * FROM saved_indexes WHERE table_name = registered.table_name
        LOOP
            EXECUTE saved.definition;
        END LOOP;
    END LOOP;

    -- Each foreign key names its tables, which now name the new tables.
    FOR saved IN SELECT * FROM saved_foreign_keys
    LOOP
        EXECUTE format('ALTER TABLE %I ADD CONSTRAINT %I %s', saved.table_name, saved.constraint_name, saved.definition);
    END LOOP;
END
$$;

-- The legacy tables still to be emptied, with the partition prefix of each.
-- LegacyPartitions.MoveAsync drops this table with the last of them.
ALTER TABLE recording_partitioned_tables RENAME TO legacy_partitioned_tables;
ALTER TABLE legacy_partitioned_tables ADD COLUMN legacy_table_name text;
UPDATE legacy_partitioned_tables SET legacy_table_name = 'legacy_' || partition_order;
ALTER TABLE legacy_partitioned_tables ALTER COLUMN legacy_table_name SET NOT NULL;

-- The per-recording tables, in an order that puts a referenced table before
-- the tables that refer to it. RecordingStore.DeleteRecordingAsync removes a
-- recording's rows in the reverse order.
CREATE TABLE recording_tables (
    table_name text PRIMARY KEY,
    table_order smallint NOT NULL UNIQUE
);

INSERT INTO recording_tables (table_name, table_order)
    SELECT table_name, partition_order FROM legacy_partitioned_tables;
