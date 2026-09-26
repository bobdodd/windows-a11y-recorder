-- Evidence tables. Generated from src/Recorder.Database/Evidence/EvidenceCatalog.cs
-- by EvidenceSql.Migration(); a test requires this file to match. Edit the
-- catalog, not this file. See docs/architecture/session-database.md.

CREATE TABLE browser_event_targets (
    recording_id uuid NOT NULL,
    identity_key bigint NOT NULL,
    kind_name_id integer NOT NULL REFERENCES names (name_id),
    interface_name_name_id integer REFERENCES names (name_id),
    target_id text,
    document_id text,
    node_id bigint,
    backend_node_id text,
    tag_name_name_id integer REFERENCES names (name_id),
    element_id text,
    PRIMARY KEY (recording_id, identity_key)
) PARTITION BY LIST (recording_id);

CREATE TABLE browser_event_target_classes (
    recording_id uuid NOT NULL,
    owner_key bigint NOT NULL,
    ordinal_1 integer NOT NULL,
    value_name_id integer NOT NULL REFERENCES names (name_id),
    PRIMARY KEY (recording_id, owner_key, ordinal_1),
    FOREIGN KEY (recording_id, owner_key)
        REFERENCES browser_event_targets (recording_id, identity_key)
) PARTITION BY LIST (recording_id);

CREATE TABLE script_locations (
    recording_id uuid NOT NULL,
    identity_key bigint NOT NULL,
    script_id text,
    url text,
    line_number integer,
    column_number integer,
    function_name text,
    source_hash text,
    PRIMARY KEY (recording_id, identity_key)
) PARTITION BY LIST (recording_id);

CREATE TABLE execution_worlds (
    recording_id uuid NOT NULL,
    identity_key bigint NOT NULL,
    kind_name_id integer NOT NULL REFERENCES names (name_id),
    blink_world_id integer NOT NULL,
    name text,
    stable_id text,
    PRIMARY KEY (recording_id, identity_key)
) PARTITION BY LIST (recording_id);

CREATE TABLE execution_scopes (
    recording_id uuid NOT NULL,
    identity_key bigint NOT NULL,
    context_kind_name_id integer NOT NULL REFERENCES names (name_id),
    worker_token text,
    global_object_url text,
    PRIMARY KEY (recording_id, identity_key)
) PARTITION BY LIST (recording_id);

CREATE TABLE browser_listener_events (
    recording_id uuid NOT NULL,
    event_key bigint NOT NULL,
    context_key bigint NOT NULL,
    listener_id text NOT NULL,
    event_name_name_id integer NOT NULL REFERENCES names (name_id),
    registration_kind_name_id integer NOT NULL REFERENCES names (name_id),
    target_key bigint NOT NULL,
    capture boolean NOT NULL,
    passive boolean NOT NULL,
    once boolean NOT NULL,
    location_key bigint,
    world_key bigint,
    scope_key bigint,
    PRIMARY KEY (recording_id, event_key),
    FOREIGN KEY (recording_id, event_key)
        REFERENCES events (recording_id, event_key),
    FOREIGN KEY (recording_id, context_key)
        REFERENCES browser_contexts (recording_id, identity_key),
    FOREIGN KEY (recording_id, target_key)
        REFERENCES browser_event_targets (recording_id, identity_key),
    FOREIGN KEY (recording_id, location_key)
        REFERENCES script_locations (recording_id, identity_key),
    FOREIGN KEY (recording_id, world_key)
        REFERENCES execution_worlds (recording_id, identity_key),
    FOREIGN KEY (recording_id, scope_key)
        REFERENCES execution_scopes (recording_id, identity_key)
) PARTITION BY LIST (recording_id);

CREATE TABLE browser_dispatch_events (
    recording_id uuid NOT NULL,
    event_key bigint NOT NULL,
    context_key bigint NOT NULL,
    dispatch_id text NOT NULL,
    event_name_name_id integer NOT NULL REFERENCES names (name_id),
    trusted boolean NOT NULL,
    original_target_key bigint,
    phase_name_id integer NOT NULL REFERENCES names (name_id),
    listener_id text,
    default_prevented boolean NOT NULL,
    propagation_stopped boolean NOT NULL,
    immediate_propagation_stopped boolean NOT NULL,
    default_action_name_id integer REFERENCES names (name_id),
    outcome_name_id integer REFERENCES names (name_id),
    current_target_key bigint,
    scope_key bigint,
    PRIMARY KEY (recording_id, event_key),
    FOREIGN KEY (recording_id, event_key)
        REFERENCES events (recording_id, event_key),
    FOREIGN KEY (recording_id, context_key)
        REFERENCES browser_contexts (recording_id, identity_key),
    FOREIGN KEY (recording_id, original_target_key)
        REFERENCES browser_event_targets (recording_id, identity_key),
    FOREIGN KEY (recording_id, current_target_key)
        REFERENCES browser_event_targets (recording_id, identity_key),
    FOREIGN KEY (recording_id, scope_key)
        REFERENCES execution_scopes (recording_id, identity_key)
) PARTITION BY LIST (recording_id);

CREATE TABLE browser_timer_events (
    recording_id uuid NOT NULL,
    event_key bigint NOT NULL,
    context_key bigint NOT NULL,
    timer_id text NOT NULL,
    timer_kind_name_id integer NOT NULL REFERENCES names (name_id),
    requested_delay_milliseconds double precision,
    effective_delay_milliseconds double precision,
    nesting_level integer NOT NULL,
    throttled boolean,
    page_lifecycle_state_name_id integer NOT NULL REFERENCES names (name_id),
    callback_location_key bigint,
    cancellation_reason_name_id integer REFERENCES names (name_id),
    did_timeout boolean,
    PRIMARY KEY (recording_id, event_key),
    FOREIGN KEY (recording_id, event_key)
        REFERENCES events (recording_id, event_key),
    FOREIGN KEY (recording_id, context_key)
        REFERENCES browser_contexts (recording_id, identity_key),
    FOREIGN KEY (recording_id, callback_location_key)
        REFERENCES script_locations (recording_id, identity_key)
) PARTITION BY LIST (recording_id);

CREATE TABLE browser_scheduler_deferrals (
    recording_id uuid NOT NULL,
    event_key bigint NOT NULL,
    context_key bigint NOT NULL,
    queue_name_name_id integer NOT NULL REFERENCES names (name_id),
    queue_type integer NOT NULL,
    throttling_type_name_id integer NOT NULL REFERENCES names (name_id),
    desired_wake_up_ticks text NOT NULL,
    allowed_wake_up_ticks text NOT NULL,
    deferral_milliseconds double precision NOT NULL,
    has_ready_task boolean NOT NULL,
    block_type_name_id integer NOT NULL REFERENCES names (name_id),
    decision_boundary_name_id integer NOT NULL REFERENCES names (name_id),
    PRIMARY KEY (recording_id, event_key),
    FOREIGN KEY (recording_id, event_key)
        REFERENCES events (recording_id, event_key),
    FOREIGN KEY (recording_id, context_key)
        REFERENCES browser_contexts (recording_id, identity_key)
) PARTITION BY LIST (recording_id);

CREATE TABLE browser_navigations (
    recording_id uuid NOT NULL,
    event_key bigint NOT NULL,
    context_key bigint NOT NULL,
    parent_frame_id text,
    parent_or_outer_document_frame_id text,
    frame_type_name_id integer NOT NULL REFERENCES names (name_id),
    primary_page boolean NOT NULL,
    navigation_id text NOT NULL,
    url_key bigint NOT NULL,
    navigation_kind_name_id integer NOT NULL REFERENCES names (name_id),
    renderer_initiated boolean NOT NULL,
    same_document boolean NOT NULL,
    committed boolean,
    error_page boolean,
    net_error_code integer,
    outcome_name_id integer REFERENCES names (name_id),
    renderer_process_id bigint,
    PRIMARY KEY (recording_id, event_key),
    FOREIGN KEY (recording_id, event_key)
        REFERENCES events (recording_id, event_key),
    FOREIGN KEY (recording_id, context_key)
        REFERENCES browser_contexts (recording_id, identity_key),
    FOREIGN KEY (recording_id, url_key)
        REFERENCES recording_texts (recording_id, identity_key)
) PARTITION BY LIST (recording_id);

CREATE TABLE browser_dispatch_path_targets (
    recording_id uuid NOT NULL,
    owner_key bigint NOT NULL,
    ordinal_1 integer NOT NULL,
    target_key bigint NOT NULL,
    PRIMARY KEY (recording_id, owner_key, ordinal_1),
    FOREIGN KEY (recording_id, owner_key)
        REFERENCES browser_dispatch_events (recording_id, event_key),
    FOREIGN KEY (recording_id, target_key)
        REFERENCES browser_event_targets (recording_id, identity_key)
) PARTITION BY LIST (recording_id);

CREATE TABLE browser_dispatch_path_scopes (
    recording_id uuid NOT NULL,
    owner_key bigint NOT NULL,
    ordinal_1 integer NOT NULL,
    tree_scope_root_node_id bigint,
    shadow_root_mode_name_id integer REFERENCES names (name_id),
    target_node_id bigint,
    related_target_node_id bigint,
    unmatched_visible_target_count integer NOT NULL,
    PRIMARY KEY (recording_id, owner_key, ordinal_1),
    FOREIGN KEY (recording_id, owner_key)
        REFERENCES browser_dispatch_events (recording_id, event_key)
) PARTITION BY LIST (recording_id);

CREATE TABLE browser_dispatch_path_scope_visible_indexes (
    recording_id uuid NOT NULL,
    owner_key bigint NOT NULL,
    ordinal_1 integer NOT NULL,
    ordinal_2 integer NOT NULL,
    value integer NOT NULL,
    PRIMARY KEY (recording_id, owner_key, ordinal_1, ordinal_2),
    FOREIGN KEY (recording_id, owner_key, ordinal_1)
        REFERENCES browser_dispatch_path_scopes (recording_id, owner_key, ordinal_1)
) PARTITION BY LIST (recording_id);

INSERT INTO recording_partitioned_tables (table_name, partition_prefix, partition_order) VALUES
    ('browser_event_targets', 'pt30', 30),
    ('browser_event_target_classes', 'pt31', 31),
    ('script_locations', 'pt32', 32),
    ('execution_worlds', 'pt33', 33),
    ('execution_scopes', 'pt34', 34),
    ('browser_listener_events', 'pt35', 35),
    ('browser_dispatch_events', 'pt36', 36),
    ('browser_timer_events', 'pt37', 37),
    ('browser_scheduler_deferrals', 'pt38', 38),
    ('browser_navigations', 'pt39', 39),
    ('browser_dispatch_path_targets', 'pt40', 40),
    ('browser_dispatch_path_scopes', 'pt41', 41),
    ('browser_dispatch_path_scope_visible_indexes', 'pt42', 42);
