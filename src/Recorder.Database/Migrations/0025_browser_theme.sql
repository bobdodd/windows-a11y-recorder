-- Protocol 0.59 (the browser theme, agreed with the owner 2026-10-10).
-- The listed browser preferences gain the browser's theme color, its color
-- style, its grayscale setting, and the identity of an installed theme,
-- which a recording before 0.59 does not hold, so each is optional in the
-- snapshot, with a has_ column false for the rows already written. A later
-- color-maps-sent record names the colors of each map in it that changed,
-- each map's names in a child table of its own. The evidence tables are not
-- written for a recording that has a recording file (see
-- 0013_recording_files.sql); they keep mapping every payload member so the
-- database writer still accepts every record. See
-- docs/architecture/accessibility-preferences.md, "The browser theme".
--
-- The child tables are made as 0009_unpartitioned_recording_tables.sql left
-- the other per-recording tables, with their references listed in
-- recording_references, as since 0011_bulk_reference_checks.sql.

ALTER TABLE browser_preference_snapshots
    ADD COLUMN has_preferences_user_color boolean NOT NULL DEFAULT false,
    ADD COLUMN preferences_user_color_value bigint,
    ADD COLUMN preferences_user_color_is_default boolean,
    ADD COLUMN preferences_user_color_problem text,
    ADD COLUMN has_preferences_color_variant boolean NOT NULL DEFAULT false,
    ADD COLUMN preferences_color_variant_value bigint,
    ADD COLUMN preferences_color_variant_is_default boolean,
    ADD COLUMN preferences_color_variant_problem text,
    ADD COLUMN has_preferences_grayscale_theme boolean NOT NULL DEFAULT false,
    ADD COLUMN preferences_grayscale_theme_value boolean,
    ADD COLUMN preferences_grayscale_theme_is_default boolean,
    ADD COLUMN preferences_grayscale_theme_problem text,
    ADD COLUMN has_preferences_theme_id boolean NOT NULL DEFAULT false,
    ADD COLUMN preferences_theme_id_value text,
    ADD COLUMN preferences_theme_id_is_default boolean,
    ADD COLUMN preferences_theme_id_problem text;
ALTER TABLE browser_preference_snapshots
    ALTER COLUMN has_preferences_user_color DROP DEFAULT,
    ALTER COLUMN has_preferences_color_variant DROP DEFAULT,
    ALTER COLUMN has_preferences_grayscale_theme DROP DEFAULT,
    ALTER COLUMN has_preferences_theme_id DROP DEFAULT;

ALTER TABLE browser_preference_changes
    ADD COLUMN has_previous_user_color boolean NOT NULL DEFAULT false,
    ADD COLUMN previous_user_color_value bigint,
    ADD COLUMN previous_user_color_is_default boolean,
    ADD COLUMN previous_user_color_problem text,
    ADD COLUMN has_previous_color_variant boolean NOT NULL DEFAULT false,
    ADD COLUMN previous_color_variant_value bigint,
    ADD COLUMN previous_color_variant_is_default boolean,
    ADD COLUMN previous_color_variant_problem text,
    ADD COLUMN has_previous_grayscale_theme boolean NOT NULL DEFAULT false,
    ADD COLUMN previous_grayscale_theme_value boolean,
    ADD COLUMN previous_grayscale_theme_is_default boolean,
    ADD COLUMN previous_grayscale_theme_problem text,
    ADD COLUMN has_previous_theme_id boolean NOT NULL DEFAULT false,
    ADD COLUMN previous_theme_id_value text,
    ADD COLUMN previous_theme_id_is_default boolean,
    ADD COLUMN previous_theme_id_problem text,
    ADD COLUMN has_current_user_color boolean NOT NULL DEFAULT false,
    ADD COLUMN current_user_color_value bigint,
    ADD COLUMN current_user_color_is_default boolean,
    ADD COLUMN current_user_color_problem text,
    ADD COLUMN has_current_color_variant boolean NOT NULL DEFAULT false,
    ADD COLUMN current_color_variant_value bigint,
    ADD COLUMN current_color_variant_is_default boolean,
    ADD COLUMN current_color_variant_problem text,
    ADD COLUMN has_current_grayscale_theme boolean NOT NULL DEFAULT false,
    ADD COLUMN current_grayscale_theme_value boolean,
    ADD COLUMN current_grayscale_theme_is_default boolean,
    ADD COLUMN current_grayscale_theme_problem text,
    ADD COLUMN has_current_theme_id boolean NOT NULL DEFAULT false,
    ADD COLUMN current_theme_id_value text,
    ADD COLUMN current_theme_id_is_default boolean,
    ADD COLUMN current_theme_id_problem text;
ALTER TABLE browser_preference_changes
    ALTER COLUMN has_previous_user_color DROP DEFAULT,
    ALTER COLUMN has_previous_color_variant DROP DEFAULT,
    ALTER COLUMN has_previous_grayscale_theme DROP DEFAULT,
    ALTER COLUMN has_previous_theme_id DROP DEFAULT,
    ALTER COLUMN has_current_user_color DROP DEFAULT,
    ALTER COLUMN has_current_color_variant DROP DEFAULT,
    ALTER COLUMN has_current_grayscale_theme DROP DEFAULT,
    ALTER COLUMN has_current_theme_id DROP DEFAULT;

ALTER TABLE browser_color_maps_sent
    ADD COLUMN has_changed_colors boolean NOT NULL DEFAULT false,
    ADD COLUMN has_changed_colors_light boolean,
    ADD COLUMN has_changed_colors_dark boolean,
    ADD COLUMN has_changed_colors_forced_colors boolean;
ALTER TABLE browser_color_maps_sent
    ALTER COLUMN has_changed_colors DROP DEFAULT;

CREATE TABLE browser_color_maps_changed_light (
    recording_id uuid NOT NULL,
    owner_key bigint NOT NULL,
    ordinal_1 integer NOT NULL,
    value text NOT NULL,
    PRIMARY KEY (recording_id, owner_key, ordinal_1)
);

CREATE TABLE browser_color_maps_changed_dark (
    recording_id uuid NOT NULL,
    owner_key bigint NOT NULL,
    ordinal_1 integer NOT NULL,
    value text NOT NULL,
    PRIMARY KEY (recording_id, owner_key, ordinal_1)
);

CREATE TABLE browser_color_maps_changed_forced_colors (
    recording_id uuid NOT NULL,
    owner_key bigint NOT NULL,
    ordinal_1 integer NOT NULL,
    value text NOT NULL,
    PRIMARY KEY (recording_id, owner_key, ordinal_1)
);

INSERT INTO recording_tables (table_name, table_order)
    SELECT 'browser_color_maps_changed_light', max(table_order) + 1 FROM recording_tables;
INSERT INTO recording_tables (table_name, table_order)
    SELECT 'browser_color_maps_changed_dark', max(table_order) + 1 FROM recording_tables;
INSERT INTO recording_tables (table_name, table_order)
    SELECT 'browser_color_maps_changed_forced_colors', max(table_order) + 1 FROM recording_tables;

INSERT INTO recording_references (table_name, columns, referenced_table, referenced_columns)
VALUES
    ('browser_color_maps_changed_light', '{recording_id,owner_key}', 'browser_color_maps_sent', '{recording_id,event_key}'),
    ('browser_color_maps_changed_dark', '{recording_id,owner_key}', 'browser_color_maps_sent', '{recording_id,event_key}'),
    ('browser_color_maps_changed_forced_colors', '{recording_id,owner_key}', 'browser_color_maps_sent', '{recording_id,event_key}');
