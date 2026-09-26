-- Stores each distinct computed style once per recording, and lets the
-- writer check foreign keys when a transaction commits.
--
-- A layout checkpoint reports the computed style of every element, about
-- 340 properties each, and elements may have equal styles. Stored as one
-- row per property of every element, a 30 second recording produced more
-- rows than the writer could store as they arrived.
-- A computed style is now an identity of the recording, like a browser
-- context: browser_computed_styles holds one row per distinct style, with
-- its properties in browser_computed_style_entries, and each layout node
-- refers to its style by key. A node without a style has no key; a node
-- with an empty style refers to a style with no entries. The payload read
-- back is the payload stored.
--
-- Styles already stored are moved into the new tables, each distinct style
-- of a recording once, and the old table is dropped.
--
-- Every foreign key of the per-recording tables becomes DEFERRABLE,
-- INITIALLY IMMEDIATE. Every other transaction checks them as before. The
-- writer defers them in its own transactions, so that several transactions
-- can store one recording's events at once, each checked when it commits.
-- See docs/architecture/session-database.md.

CREATE TABLE browser_computed_styles (
    recording_id uuid NOT NULL,
    identity_key bigint NOT NULL,
    PRIMARY KEY (recording_id, identity_key)
);

CREATE TABLE browser_computed_style_entries (
    recording_id uuid NOT NULL,
    owner_key bigint NOT NULL,
    entry_name_id integer NOT NULL REFERENCES names (name_id),
    value text,
    PRIMARY KEY (recording_id, owner_key, entry_name_id),
    FOREIGN KEY (recording_id, owner_key)
        REFERENCES browser_computed_styles (recording_id, identity_key)
);

ALTER TABLE browser_layout_checkpoint_nodes ADD COLUMN computed_style_key bigint;

-- Each node's style, as text that is equal for equal styles: its entries in
-- name order, each value quoted, or null for a null value.
CREATE TEMPORARY TABLE node_styles ON COMMIT DROP AS
    SELECT n.recording_id,
           n.event_key,
           sha256(convert_to(coalesce(string_agg(
               s.entry_name_id::text || '=' || coalesce(quote_literal(s.value), 'null'),
               ',' ORDER BY s.entry_name_id), ''), 'UTF8')) AS digest
    FROM browser_layout_checkpoint_nodes n
    LEFT JOIN browser_layout_checkpoint_computed_styles s
        ON s.recording_id = n.recording_id AND s.owner_key = n.event_key
    WHERE n.has_computed_style
    GROUP BY n.recording_id, n.event_key;

-- One key per distinct style of a recording, and the first node holding it.
CREATE TEMPORARY TABLE distinct_styles ON COMMIT DROP AS
    SELECT recording_id,
           digest,
           dense_rank() OVER (PARTITION BY recording_id ORDER BY min(event_key)) - 1 AS identity_key,
           min(event_key) AS first_event_key
    FROM node_styles
    GROUP BY recording_id, digest;

INSERT INTO browser_computed_styles (recording_id, identity_key)
    SELECT recording_id, identity_key FROM distinct_styles;

INSERT INTO browser_computed_style_entries (recording_id, owner_key, entry_name_id, value)
    SELECT d.recording_id, d.identity_key, s.entry_name_id, s.value
    FROM distinct_styles d
    JOIN browser_layout_checkpoint_computed_styles s
        ON s.recording_id = d.recording_id AND s.owner_key = d.first_event_key;

UPDATE browser_layout_checkpoint_nodes n
    SET computed_style_key = d.identity_key
    FROM node_styles s
    JOIN distinct_styles d ON d.recording_id = s.recording_id AND d.digest = s.digest
    WHERE s.recording_id = n.recording_id AND s.event_key = n.event_key;

ALTER TABLE browser_layout_checkpoint_nodes
    ADD FOREIGN KEY (recording_id, computed_style_key)
        REFERENCES browser_computed_styles (recording_id, identity_key);

DROP TABLE browser_layout_checkpoint_computed_styles;
ALTER TABLE browser_layout_checkpoint_nodes DROP COLUMN has_computed_style;

-- recording_tables puts a referenced table before the tables that refer to
-- it. The orders are doubled, which leaves a free order before each table,
-- and the styles take the one before the layout nodes that refer to them.
DELETE FROM recording_tables WHERE table_name = 'browser_layout_checkpoint_computed_styles';
UPDATE recording_tables SET table_order = -2 * table_order;
UPDATE recording_tables SET table_order = -table_order;
INSERT INTO recording_tables (table_name, table_order)
    SELECT 'browser_computed_styles', table_order - 1
    FROM recording_tables WHERE table_name = 'browser_layout_checkpoint_nodes';
INSERT INTO recording_tables (table_name, table_order)
    SELECT 'browser_computed_style_entries', max(table_order) + 1 FROM recording_tables;

DO $$
DECLARE
    saved record;
BEGIN
    FOR saved IN
        SELECT c.conrelid::regclass::text AS table_name, c.conname AS constraint_name
        FROM pg_constraint c
        WHERE c.contype = 'f'
          AND c.conrelid::regclass::text IN (SELECT table_name FROM recording_tables)
    LOOP
        EXECUTE format(
            'ALTER TABLE %I ALTER CONSTRAINT %I DEFERRABLE INITIALLY IMMEDIATE',
            saved.table_name, saved.constraint_name);
    END LOOP;
END
$$;
