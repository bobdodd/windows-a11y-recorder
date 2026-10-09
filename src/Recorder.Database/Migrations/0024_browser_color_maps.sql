-- The color maps each page's view is sent, on the browser.preferences
-- channel (protocol 0.57): color-maps-sent, recorded when a view is created
-- and after each later send that changes a map. Each map present in the
-- record, light, dark, and forced colors, has its colors in a child table
-- of its own, one row a color, by its RendererColorId name in the names
-- table, written "#AARRGGBB"; the has_ columns say which maps the record
-- holds. The evidence tables are not written for a recording that has a
-- recording file (see 0013_recording_files.sql); they keep mapping every
-- payload member so the database writer still accepts every record. See
-- docs/architecture/accessibility-preferences.md, "Stage 3".
--
-- Each table is made as 0009_unpartitioned_recording_tables.sql left the
-- other per-recording tables: an ordinary table keyed by recording and
-- event, whose references are listed in recording_references and checked
-- when the recording is completed, as since 0011_bulk_reference_checks.sql,
-- and not by a foreign key.

CREATE TABLE browser_color_maps_sent (
    recording_id uuid NOT NULL,
    event_key bigint NOT NULL,
    context_key bigint NOT NULL,
    page_frame_tree_node_id integer NOT NULL,
    primary_page boolean NOT NULL,
    renderer_process_id integer NOT NULL,
    view_id text NOT NULL,
    point_name_id integer NOT NULL,
    first boolean NOT NULL,
    has_maps_light boolean NOT NULL,
    has_maps_dark boolean NOT NULL,
    has_maps_forced_colors boolean NOT NULL,
    PRIMARY KEY (recording_id, event_key)
);

CREATE TABLE browser_color_maps_sent_light (
    recording_id uuid NOT NULL,
    owner_key bigint NOT NULL,
    entry_name_id integer NOT NULL,
    value text NOT NULL,
    PRIMARY KEY (recording_id, owner_key, entry_name_id)
);

CREATE TABLE browser_color_maps_sent_dark (
    recording_id uuid NOT NULL,
    owner_key bigint NOT NULL,
    entry_name_id integer NOT NULL,
    value text NOT NULL,
    PRIMARY KEY (recording_id, owner_key, entry_name_id)
);

CREATE TABLE browser_color_maps_sent_forced_colors (
    recording_id uuid NOT NULL,
    owner_key bigint NOT NULL,
    entry_name_id integer NOT NULL,
    value text NOT NULL,
    PRIMARY KEY (recording_id, owner_key, entry_name_id)
);

INSERT INTO recording_tables (table_name, table_order)
    SELECT 'browser_color_maps_sent', max(table_order) + 1 FROM recording_tables;
INSERT INTO recording_tables (table_name, table_order)
    SELECT 'browser_color_maps_sent_light', max(table_order) + 1 FROM recording_tables;
INSERT INTO recording_tables (table_name, table_order)
    SELECT 'browser_color_maps_sent_dark', max(table_order) + 1 FROM recording_tables;
INSERT INTO recording_tables (table_name, table_order)
    SELECT 'browser_color_maps_sent_forced_colors', max(table_order) + 1 FROM recording_tables;

INSERT INTO recording_references (table_name, columns, referenced_table, referenced_columns)
VALUES
    ('browser_color_maps_sent', '{recording_id,event_key}', 'events', '{recording_id,event_key}'),
    ('browser_color_maps_sent', '{recording_id,context_key}', 'browser_contexts', '{recording_id,identity_key}'),
    ('browser_color_maps_sent', '{point_name_id}', 'names', '{name_id}'),
    ('browser_color_maps_sent_light', '{recording_id,owner_key}', 'browser_color_maps_sent', '{recording_id,event_key}'),
    ('browser_color_maps_sent_light', '{entry_name_id}', 'names', '{name_id}'),
    ('browser_color_maps_sent_dark', '{recording_id,owner_key}', 'browser_color_maps_sent', '{recording_id,event_key}'),
    ('browser_color_maps_sent_dark', '{entry_name_id}', 'names', '{name_id}'),
    ('browser_color_maps_sent_forced_colors', '{recording_id,owner_key}', 'browser_color_maps_sent', '{recording_id,event_key}'),
    ('browser_color_maps_sent_forced_colors', '{entry_name_id}', 'names', '{name_id}');
