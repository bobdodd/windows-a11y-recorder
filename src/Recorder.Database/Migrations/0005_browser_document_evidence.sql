-- Evidence tables. Generated from src/Recorder.Database/Evidence/EvidenceCatalog.cs
-- by EvidenceSql.Migration(); a test requires this file to match. Edit the
-- catalog, not this file. See docs/architecture/session-database.md.

CREATE TABLE browser_accessibility_checkpoint_starts (
    recording_id uuid NOT NULL,
    event_key bigint NOT NULL,
    context_key bigint NOT NULL,
    checkpoint_id text NOT NULL,
    reason_name_id integer NOT NULL REFERENCES names (name_id),
    maximum_nodes integer NOT NULL,
    update_count integer NOT NULL,
    event_count integer NOT NULL,
    PRIMARY KEY (recording_id, event_key),
    FOREIGN KEY (recording_id, event_key)
        REFERENCES events (recording_id, event_key),
    FOREIGN KEY (recording_id, context_key)
        REFERENCES browser_contexts (recording_id, identity_key)
) PARTITION BY LIST (recording_id);

CREATE TABLE browser_accessibility_checkpoint_nodes (
    recording_id uuid NOT NULL,
    event_key bigint NOT NULL,
    context_key bigint NOT NULL,
    checkpoint_id text NOT NULL,
    node_index integer NOT NULL,
    accessibility_node_id bigint NOT NULL,
    parent_accessibility_node_id bigint,
    dom_node_id bigint,
    role integer NOT NULL,
    role_name_name_id integer NOT NULL REFERENCES names (name_id),
    name text NOT NULL,
    description text NOT NULL,
    serialized_properties text NOT NULL,
    focused boolean NOT NULL,
    PRIMARY KEY (recording_id, event_key),
    FOREIGN KEY (recording_id, event_key)
        REFERENCES events (recording_id, event_key),
    FOREIGN KEY (recording_id, context_key)
        REFERENCES browser_contexts (recording_id, identity_key)
) PARTITION BY LIST (recording_id);

CREATE TABLE browser_accessibility_checkpoint_completions (
    recording_id uuid NOT NULL,
    event_key bigint NOT NULL,
    context_key bigint NOT NULL,
    checkpoint_id text NOT NULL,
    reason_name_id integer NOT NULL REFERENCES names (name_id),
    node_count integer NOT NULL,
    truncated boolean NOT NULL,
    maximum_nodes integer NOT NULL,
    update_count integer NOT NULL,
    event_count integer NOT NULL,
    PRIMARY KEY (recording_id, event_key),
    FOREIGN KEY (recording_id, event_key)
        REFERENCES events (recording_id, event_key),
    FOREIGN KEY (recording_id, context_key)
        REFERENCES browser_contexts (recording_id, identity_key)
) PARTITION BY LIST (recording_id);

CREATE TABLE browser_dom_checkpoint_starts (
    recording_id uuid NOT NULL,
    event_key bigint NOT NULL,
    context_key bigint NOT NULL,
    checkpoint_id text NOT NULL,
    reason_name_id integer NOT NULL REFERENCES names (name_id),
    maximum_nodes integer NOT NULL,
    PRIMARY KEY (recording_id, event_key),
    FOREIGN KEY (recording_id, event_key)
        REFERENCES events (recording_id, event_key),
    FOREIGN KEY (recording_id, context_key)
        REFERENCES browser_contexts (recording_id, identity_key)
) PARTITION BY LIST (recording_id);

CREATE TABLE browser_dom_checkpoint_nodes (
    recording_id uuid NOT NULL,
    event_key bigint NOT NULL,
    context_key bigint NOT NULL,
    checkpoint_id text NOT NULL,
    node_index integer NOT NULL,
    node_id bigint NOT NULL,
    parent_node_id bigint,
    node_type_name_id integer NOT NULL REFERENCES names (name_id),
    node_name_name_id integer NOT NULL REFERENCES names (name_id),
    PRIMARY KEY (recording_id, event_key),
    FOREIGN KEY (recording_id, event_key)
        REFERENCES events (recording_id, event_key),
    FOREIGN KEY (recording_id, context_key)
        REFERENCES browser_contexts (recording_id, identity_key)
) PARTITION BY LIST (recording_id);

CREATE TABLE browser_dom_checkpoint_attributes (
    recording_id uuid NOT NULL,
    event_key bigint NOT NULL,
    context_key bigint NOT NULL,
    checkpoint_id text NOT NULL,
    node_id bigint NOT NULL,
    attribute_index integer NOT NULL,
    attribute_namespace_name_id integer REFERENCES names (name_id),
    attribute_name_name_id integer NOT NULL REFERENCES names (name_id),
    attribute_value text NOT NULL,
    attribute_value_length integer NOT NULL,
    attribute_value_truncated boolean NOT NULL,
    maximum_value_length integer NOT NULL,
    PRIMARY KEY (recording_id, event_key),
    FOREIGN KEY (recording_id, event_key)
        REFERENCES events (recording_id, event_key),
    FOREIGN KEY (recording_id, context_key)
        REFERENCES browser_contexts (recording_id, identity_key)
) PARTITION BY LIST (recording_id);

CREATE TABLE browser_dom_checkpoint_shadow_roots (
    recording_id uuid NOT NULL,
    event_key bigint NOT NULL,
    context_key bigint NOT NULL,
    checkpoint_id text NOT NULL,
    node_id bigint NOT NULL,
    host_node_id bigint NOT NULL,
    mode_name_id integer NOT NULL REFERENCES names (name_id),
    delegates_focus boolean NOT NULL,
    slot_assignment_name_id integer NOT NULL REFERENCES names (name_id),
    clonable boolean NOT NULL,
    serializable boolean NOT NULL,
    declarative boolean NOT NULL,
    available_to_element_internals boolean NOT NULL,
    reference_target text,
    PRIMARY KEY (recording_id, event_key),
    FOREIGN KEY (recording_id, event_key)
        REFERENCES events (recording_id, event_key),
    FOREIGN KEY (recording_id, context_key)
        REFERENCES browser_contexts (recording_id, identity_key)
) PARTITION BY LIST (recording_id);

CREATE TABLE browser_dom_checkpoint_slot_assignments (
    recording_id uuid NOT NULL,
    event_key bigint NOT NULL,
    context_key bigint NOT NULL,
    checkpoint_id text NOT NULL,
    node_id bigint NOT NULL,
    assigned_node_count integer NOT NULL,
    assigned_nodes_truncated boolean NOT NULL,
    maximum_assigned_nodes integer NOT NULL,
    assignment_current boolean NOT NULL,
    PRIMARY KEY (recording_id, event_key),
    FOREIGN KEY (recording_id, event_key)
        REFERENCES events (recording_id, event_key),
    FOREIGN KEY (recording_id, context_key)
        REFERENCES browser_contexts (recording_id, identity_key)
) PARTITION BY LIST (recording_id);

CREATE TABLE browser_dom_checkpoint_completions (
    recording_id uuid NOT NULL,
    event_key bigint NOT NULL,
    context_key bigint NOT NULL,
    checkpoint_id text NOT NULL,
    reason_name_id integer NOT NULL REFERENCES names (name_id),
    node_count integer NOT NULL,
    truncated boolean NOT NULL,
    maximum_nodes integer NOT NULL,
    attribute_count integer NOT NULL,
    attributes_truncated boolean NOT NULL,
    maximum_attributes_per_node integer NOT NULL,
    maximum_value_length integer NOT NULL,
    covered_transition_count integer NOT NULL,
    covered_transition_first_id text,
    covered_transition_last_id text,
    shadow_root_count integer NOT NULL,
    slot_count integer NOT NULL,
    PRIMARY KEY (recording_id, event_key),
    FOREIGN KEY (recording_id, event_key)
        REFERENCES events (recording_id, event_key),
    FOREIGN KEY (recording_id, context_key)
        REFERENCES browser_contexts (recording_id, identity_key)
) PARTITION BY LIST (recording_id);

CREATE TABLE browser_dom_attribute_changes (
    recording_id uuid NOT NULL,
    event_key bigint NOT NULL,
    context_key bigint NOT NULL,
    transition_id text NOT NULL,
    node_id bigint NOT NULL,
    node_name_name_id integer NOT NULL REFERENCES names (name_id),
    attribute_namespace_name_id integer REFERENCES names (name_id),
    attribute_name_name_id integer NOT NULL REFERENCES names (name_id),
    change_type_name_id integer NOT NULL REFERENCES names (name_id),
    attribute_value text,
    attribute_value_length integer,
    attribute_value_truncated boolean NOT NULL,
    previous_attribute_value text,
    previous_attribute_value_length integer,
    previous_attribute_value_truncated boolean NOT NULL,
    maximum_value_length integer NOT NULL,
    PRIMARY KEY (recording_id, event_key),
    FOREIGN KEY (recording_id, event_key)
        REFERENCES events (recording_id, event_key),
    FOREIGN KEY (recording_id, context_key)
        REFERENCES browser_contexts (recording_id, identity_key)
) PARTITION BY LIST (recording_id);

CREATE TABLE browser_dom_character_data_changes (
    recording_id uuid NOT NULL,
    event_key bigint NOT NULL,
    context_key bigint NOT NULL,
    transition_id text NOT NULL,
    node_id bigint NOT NULL,
    parent_node_id bigint,
    node_type_name_id integer NOT NULL REFERENCES names (name_id),
    text text NOT NULL,
    text_length integer NOT NULL,
    text_truncated boolean NOT NULL,
    previous_text text NOT NULL,
    previous_text_length integer NOT NULL,
    previous_text_truncated boolean NOT NULL,
    maximum_value_length integer NOT NULL,
    PRIMARY KEY (recording_id, event_key),
    FOREIGN KEY (recording_id, event_key)
        REFERENCES events (recording_id, event_key),
    FOREIGN KEY (recording_id, context_key)
        REFERENCES browser_contexts (recording_id, identity_key)
) PARTITION BY LIST (recording_id);

CREATE TABLE browser_focus_changes (
    recording_id uuid NOT NULL,
    event_key bigint NOT NULL,
    context_key bigint NOT NULL,
    previous_node_id bigint,
    requested_node_id bigint,
    focused_node_id bigint,
    outcome_name_id integer NOT NULL REFERENCES names (name_id),
    active_descendant_node_id bigint,
    focus_type_name_id integer NOT NULL REFERENCES names (name_id),
    focus_trigger_name_id integer NOT NULL REFERENCES names (name_id),
    prevent_scroll boolean NOT NULL,
    focus_visible boolean,
    location_key bigint,
    world_key bigint,
    PRIMARY KEY (recording_id, event_key),
    FOREIGN KEY (recording_id, event_key)
        REFERENCES events (recording_id, event_key),
    FOREIGN KEY (recording_id, context_key)
        REFERENCES browser_contexts (recording_id, identity_key),
    FOREIGN KEY (recording_id, location_key)
        REFERENCES script_locations (recording_id, identity_key),
    FOREIGN KEY (recording_id, world_key)
        REFERENCES execution_worlds (recording_id, identity_key)
) PARTITION BY LIST (recording_id);

CREATE TABLE browser_selection_changes (
    recording_id uuid NOT NULL,
    event_key bigint NOT NULL,
    context_key bigint NOT NULL,
    set_by_name_id integer NOT NULL REFERENCES names (name_id),
    selection_type_name_id integer NOT NULL REFERENCES names (name_id),
    anchor_node_id bigint,
    anchor_offset integer,
    focus_node_id bigint,
    focus_offset integer,
    directional boolean NOT NULL,
    text_control_node_id bigint,
    text_control_selection_start integer,
    text_control_selection_end integer,
    text_control_selection_direction_name_id integer REFERENCES names (name_id),
    location_key bigint,
    world_key bigint,
    PRIMARY KEY (recording_id, event_key),
    FOREIGN KEY (recording_id, event_key)
        REFERENCES events (recording_id, event_key),
    FOREIGN KEY (recording_id, context_key)
        REFERENCES browser_contexts (recording_id, identity_key),
    FOREIGN KEY (recording_id, location_key)
        REFERENCES script_locations (recording_id, identity_key),
    FOREIGN KEY (recording_id, world_key)
        REFERENCES execution_worlds (recording_id, identity_key)
) PARTITION BY LIST (recording_id);

CREATE TABLE browser_text_control_value_changes (
    recording_id uuid NOT NULL,
    event_key bigint NOT NULL,
    context_key bigint NOT NULL,
    node_id bigint NOT NULL,
    control_type_name_id integer NOT NULL REFERENCES names (name_id),
    source_name_id integer NOT NULL REFERENCES names (name_id),
    value text NOT NULL,
    value_length integer NOT NULL,
    value_truncated boolean NOT NULL,
    maximum_value_length integer NOT NULL,
    selection_start integer NOT NULL,
    selection_end integer NOT NULL,
    selection_direction_name_id integer NOT NULL REFERENCES names (name_id),
    location_key bigint,
    world_key bigint,
    PRIMARY KEY (recording_id, event_key),
    FOREIGN KEY (recording_id, event_key)
        REFERENCES events (recording_id, event_key),
    FOREIGN KEY (recording_id, context_key)
        REFERENCES browser_contexts (recording_id, identity_key),
    FOREIGN KEY (recording_id, location_key)
        REFERENCES script_locations (recording_id, identity_key),
    FOREIGN KEY (recording_id, world_key)
        REFERENCES execution_worlds (recording_id, identity_key)
) PARTITION BY LIST (recording_id);

CREATE TABLE browser_active_descendant_references (
    recording_id uuid NOT NULL,
    event_key bigint NOT NULL,
    context_key bigint NOT NULL,
    node_id bigint NOT NULL,
    referenced_node_id bigint NOT NULL,
    location_key bigint,
    world_key bigint,
    PRIMARY KEY (recording_id, event_key),
    FOREIGN KEY (recording_id, event_key)
        REFERENCES events (recording_id, event_key),
    FOREIGN KEY (recording_id, context_key)
        REFERENCES browser_contexts (recording_id, identity_key),
    FOREIGN KEY (recording_id, location_key)
        REFERENCES script_locations (recording_id, identity_key),
    FOREIGN KEY (recording_id, world_key)
        REFERENCES execution_worlds (recording_id, identity_key)
) PARTITION BY LIST (recording_id);

CREATE TABLE browser_interaction_checkpoint_starts (
    recording_id uuid NOT NULL,
    event_key bigint NOT NULL,
    context_key bigint NOT NULL,
    checkpoint_id text NOT NULL,
    source_checkpoint_id text NOT NULL,
    source_channel_name_id integer NOT NULL REFERENCES names (name_id),
    reason_name_id integer NOT NULL REFERENCES names (name_id),
    document_has_focus boolean NOT NULL,
    focused_node_id bigint,
    focus_visible boolean NOT NULL,
    active_descendant_node_id bigint,
    last_focus_type_name_id integer NOT NULL REFERENCES names (name_id),
    selection_type_name_id integer NOT NULL REFERENCES names (name_id),
    anchor_node_id bigint,
    anchor_offset integer,
    focus_node_id bigint,
    focus_offset integer,
    directional boolean NOT NULL,
    maximum_text_controls integer NOT NULL,
    maximum_value_length integer NOT NULL,
    PRIMARY KEY (recording_id, event_key),
    FOREIGN KEY (recording_id, event_key)
        REFERENCES events (recording_id, event_key),
    FOREIGN KEY (recording_id, context_key)
        REFERENCES browser_contexts (recording_id, identity_key)
) PARTITION BY LIST (recording_id);

CREATE TABLE browser_interaction_checkpoint_text_controls (
    recording_id uuid NOT NULL,
    event_key bigint NOT NULL,
    context_key bigint NOT NULL,
    checkpoint_id text NOT NULL,
    text_control_index integer NOT NULL,
    node_id bigint NOT NULL,
    control_type_name_id integer NOT NULL REFERENCES names (name_id),
    value text NOT NULL,
    value_length integer NOT NULL,
    value_truncated boolean NOT NULL,
    selection_start integer NOT NULL,
    selection_end integer NOT NULL,
    selection_direction_name_id integer NOT NULL REFERENCES names (name_id),
    PRIMARY KEY (recording_id, event_key),
    FOREIGN KEY (recording_id, event_key)
        REFERENCES events (recording_id, event_key),
    FOREIGN KEY (recording_id, context_key)
        REFERENCES browser_contexts (recording_id, identity_key)
) PARTITION BY LIST (recording_id);

CREATE TABLE browser_interaction_checkpoint_completions (
    recording_id uuid NOT NULL,
    event_key bigint NOT NULL,
    context_key bigint NOT NULL,
    checkpoint_id text NOT NULL,
    text_control_count integer NOT NULL,
    truncated boolean NOT NULL,
    maximum_text_controls integer NOT NULL,
    PRIMARY KEY (recording_id, event_key),
    FOREIGN KEY (recording_id, event_key)
        REFERENCES events (recording_id, event_key),
    FOREIGN KEY (recording_id, context_key)
        REFERENCES browser_contexts (recording_id, identity_key)
) PARTITION BY LIST (recording_id);

CREATE TABLE browser_dom_checkpoint_slot_assigned_nodes (
    recording_id uuid NOT NULL,
    owner_key bigint NOT NULL,
    ordinal_1 integer NOT NULL,
    value bigint,
    PRIMARY KEY (recording_id, owner_key, ordinal_1),
    FOREIGN KEY (recording_id, owner_key)
        REFERENCES browser_dom_checkpoint_slot_assignments (recording_id, event_key)
) PARTITION BY LIST (recording_id);

INSERT INTO recording_partitioned_tables (table_name, partition_prefix, partition_order) VALUES
    ('browser_accessibility_checkpoint_starts', 'pt43', 43),
    ('browser_accessibility_checkpoint_nodes', 'pt44', 44),
    ('browser_accessibility_checkpoint_completions', 'pt45', 45),
    ('browser_dom_checkpoint_starts', 'pt46', 46),
    ('browser_dom_checkpoint_nodes', 'pt47', 47),
    ('browser_dom_checkpoint_attributes', 'pt48', 48),
    ('browser_dom_checkpoint_shadow_roots', 'pt49', 49),
    ('browser_dom_checkpoint_slot_assignments', 'pt50', 50),
    ('browser_dom_checkpoint_completions', 'pt51', 51),
    ('browser_dom_attribute_changes', 'pt52', 52),
    ('browser_dom_character_data_changes', 'pt53', 53),
    ('browser_focus_changes', 'pt54', 54),
    ('browser_selection_changes', 'pt55', 55),
    ('browser_text_control_value_changes', 'pt56', 56),
    ('browser_active_descendant_references', 'pt57', 57),
    ('browser_interaction_checkpoint_starts', 'pt58', 58),
    ('browser_interaction_checkpoint_text_controls', 'pt59', 59),
    ('browser_interaction_checkpoint_completions', 'pt60', 60),
    ('browser_dom_checkpoint_slot_assigned_nodes', 'pt61', 61);
