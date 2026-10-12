-- Protocol 0.60's accessibility request records, on the
-- browser.accessibility channel, written by the instrumented Chromium:
-- accessibility-hit-test, a hit test asked of the browser process through
-- the platform accessibility API and the object it answered with at once;
-- accessibility-hit-test-result, the page's precise result as the browser
-- process keeps it for the next hit test; accessibility-action-sent, an
-- action as the browser process sends it to the page;
-- accessibility-action-received, the action as the renderer receives it;
-- and accessibility-hit-test-computed, a precise hit test as the renderer
-- computes it. The evidence tables are not written for a recording that has
-- a recording file (see 0013_recording_files.sql); they keep mapping every
-- payload member so the database writer still accepts every record. See
-- docs/architecture/accessibility-requests.md.
--
-- Each table is made as 0009_unpartitioned_recording_tables.sql left the
-- other per-recording tables: an ordinary table keyed by recording and
-- event, whose references are listed in recording_references and checked
-- when the recording is completed, as since 0011_bulk_reference_checks.sql,
-- and not by a foreign key.

CREATE TABLE browser_accessibility_hit_tests (
    recording_id uuid NOT NULL,
    event_key bigint NOT NULL,
    context_key bigint NOT NULL,
    root_tree_id text NOT NULL,
    point_x integer NOT NULL,
    point_y integer NOT NULL,
    basis_name_id integer NOT NULL,
    precise_requested boolean NOT NULL,
    has_answer boolean NOT NULL,
    answer_tree_id text,
    answer_node_id bigint,
    answer_role_name_id integer,
    answer_name text,
    answer_bounds_x integer,
    answer_bounds_y integer,
    answer_bounds_width integer,
    answer_bounds_height integer,
    PRIMARY KEY (recording_id, event_key)
);

CREATE TABLE browser_accessibility_hit_test_results (
    recording_id uuid NOT NULL,
    event_key bigint NOT NULL,
    context_key bigint NOT NULL,
    result_tree_id text NOT NULL,
    result_node_id bigint NOT NULL,
    result_role_name_id integer NOT NULL,
    result_name text NOT NULL,
    result_bounds_x integer NOT NULL,
    result_bounds_y integer NOT NULL,
    result_bounds_width integer NOT NULL,
    result_bounds_height integer NOT NULL,
    PRIMARY KEY (recording_id, event_key)
);

CREATE TABLE browser_accessibility_actions_sent (
    recording_id uuid NOT NULL,
    event_key bigint NOT NULL,
    context_key bigint NOT NULL,
    tree_id text NOT NULL,
    action_name_id integer NOT NULL,
    target_node_id bigint,
    has_target_node boolean NOT NULL,
    target_node_tree_id text,
    target_node_node_id bigint,
    target_node_role_name_id integer,
    target_node_name text,
    target_node_bounds_x integer,
    target_node_bounds_y integer,
    target_node_bounds_width integer,
    target_node_bounds_height integer,
    has_target_rect boolean NOT NULL,
    target_rect_x integer,
    target_rect_y integer,
    target_rect_width integer,
    target_rect_height integer,
    has_target_point boolean NOT NULL,
    target_point_x integer,
    target_point_y integer,
    horizontal_scroll_alignment_name_id integer NOT NULL,
    vertical_scroll_alignment_name_id integer NOT NULL,
    scroll_behavior_name_id integer NOT NULL,
    has_selection boolean NOT NULL,
    selection_anchor_node_id bigint,
    selection_anchor_offset integer,
    selection_focus_node_id bigint,
    selection_focus_offset integer,
    PRIMARY KEY (recording_id, event_key)
);

CREATE TABLE browser_accessibility_actions_received (
    recording_id uuid NOT NULL,
    event_key bigint NOT NULL,
    context_key bigint NOT NULL,
    tree_id text NOT NULL,
    action_name_id integer NOT NULL,
    target_node_id bigint,
    anchor_node_id bigint,
    focus_node_id bigint,
    PRIMARY KEY (recording_id, event_key)
);

CREATE TABLE browser_accessibility_hit_tests_computed (
    recording_id uuid NOT NULL,
    event_key bigint NOT NULL,
    context_key bigint NOT NULL,
    tree_id text NOT NULL,
    point_x integer NOT NULL,
    point_y integer NOT NULL,
    node_id bigint,
    child_tree boolean NOT NULL,
    event_to_fire_name_id integer NOT NULL,
    PRIMARY KEY (recording_id, event_key)
);

INSERT INTO recording_tables (table_name, table_order)
    SELECT 'browser_accessibility_hit_tests', max(table_order) + 1 FROM recording_tables;
INSERT INTO recording_tables (table_name, table_order)
    SELECT 'browser_accessibility_hit_test_results', max(table_order) + 1 FROM recording_tables;
INSERT INTO recording_tables (table_name, table_order)
    SELECT 'browser_accessibility_actions_sent', max(table_order) + 1 FROM recording_tables;
INSERT INTO recording_tables (table_name, table_order)
    SELECT 'browser_accessibility_actions_received', max(table_order) + 1 FROM recording_tables;
INSERT INTO recording_tables (table_name, table_order)
    SELECT 'browser_accessibility_hit_tests_computed', max(table_order) + 1 FROM recording_tables;

INSERT INTO recording_references (table_name, columns, referenced_table, referenced_columns)
VALUES
    ('browser_accessibility_hit_tests', '{recording_id,event_key}', 'events', '{recording_id,event_key}'),
    ('browser_accessibility_hit_tests', '{recording_id,context_key}', 'browser_contexts', '{recording_id,identity_key}'),
    ('browser_accessibility_hit_tests', '{basis_name_id}', 'names', '{name_id}'),
    ('browser_accessibility_hit_tests', '{answer_role_name_id}', 'names', '{name_id}'),
    ('browser_accessibility_hit_test_results', '{recording_id,event_key}', 'events', '{recording_id,event_key}'),
    ('browser_accessibility_hit_test_results', '{recording_id,context_key}', 'browser_contexts', '{recording_id,identity_key}'),
    ('browser_accessibility_hit_test_results', '{result_role_name_id}', 'names', '{name_id}'),
    ('browser_accessibility_actions_sent', '{recording_id,event_key}', 'events', '{recording_id,event_key}'),
    ('browser_accessibility_actions_sent', '{recording_id,context_key}', 'browser_contexts', '{recording_id,identity_key}'),
    ('browser_accessibility_actions_sent', '{action_name_id}', 'names', '{name_id}'),
    ('browser_accessibility_actions_sent', '{target_node_role_name_id}', 'names', '{name_id}'),
    ('browser_accessibility_actions_sent', '{horizontal_scroll_alignment_name_id}', 'names', '{name_id}'),
    ('browser_accessibility_actions_sent', '{vertical_scroll_alignment_name_id}', 'names', '{name_id}'),
    ('browser_accessibility_actions_sent', '{scroll_behavior_name_id}', 'names', '{name_id}'),
    ('browser_accessibility_actions_received', '{recording_id,event_key}', 'events', '{recording_id,event_key}'),
    ('browser_accessibility_actions_received', '{recording_id,context_key}', 'browser_contexts', '{recording_id,identity_key}'),
    ('browser_accessibility_actions_received', '{action_name_id}', 'names', '{name_id}'),
    ('browser_accessibility_hit_tests_computed', '{recording_id,event_key}', 'events', '{recording_id,event_key}'),
    ('browser_accessibility_hit_tests_computed', '{recording_id,context_key}', 'browser_contexts', '{recording_id,identity_key}'),
    ('browser_accessibility_hit_tests_computed', '{event_to_fire_name_id}', 'names', '{name_id}');
