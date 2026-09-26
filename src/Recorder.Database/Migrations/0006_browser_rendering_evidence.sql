-- Evidence tables. Generated from src/Recorder.Database/Evidence/EvidenceCatalog.cs
-- by EvidenceSql.Migration(); a test requires this file to match. Edit the
-- catalog, not this file. See docs/architecture/session-database.md.

CREATE TABLE browser_layout_checkpoint_starts (
    recording_id uuid NOT NULL,
    event_key bigint NOT NULL,
    context_key bigint NOT NULL,
    checkpoint_id text NOT NULL,
    reason_name_id integer NOT NULL REFERENCES names (name_id),
    previous_checkpoint_id text,
    style_resolution_count integer NOT NULL,
    layout_count integer NOT NULL,
    viewport_width double precision NOT NULL,
    viewport_height double precision NOT NULL,
    scroll_offset_x double precision NOT NULL,
    scroll_offset_y double precision NOT NULL,
    device_pixel_ratio double precision NOT NULL,
    layout_zoom_factor double precision NOT NULL,
    maximum_nodes integer NOT NULL,
    PRIMARY KEY (recording_id, event_key),
    FOREIGN KEY (recording_id, event_key)
        REFERENCES events (recording_id, event_key),
    FOREIGN KEY (recording_id, context_key)
        REFERENCES browser_contexts (recording_id, identity_key)
) PARTITION BY LIST (recording_id);

CREATE TABLE browser_layout_checkpoint_nodes (
    recording_id uuid NOT NULL,
    event_key bigint NOT NULL,
    context_key bigint NOT NULL,
    checkpoint_id text NOT NULL,
    node_index integer NOT NULL,
    node_id bigint NOT NULL,
    node_type_name_id integer NOT NULL REFERENCES names (name_id),
    node_name_name_id integer NOT NULL REFERENCES names (name_id),
    layout_object_present boolean NOT NULL,
    display_locked boolean NOT NULL,
    has_bounding_client_rect boolean NOT NULL,
    bounding_client_rect_x double precision,
    bounding_client_rect_y double precision,
    bounding_client_rect_width double precision,
    bounding_client_rect_height double precision,
    has_computed_style boolean NOT NULL,
    has_pseudo_element boolean NOT NULL,
    pseudo_element_originating_node_id bigint,
    pseudo_element_pseudo_type_name_id integer REFERENCES names (name_id),
    pseudo_element_generated_text text,
    pseudo_element_generated_text_length integer,
    pseudo_element_generated_text_truncated boolean,
    shadow_host_node_id bigint,
    shadow_root_mode_name_id integer REFERENCES names (name_id),
    PRIMARY KEY (recording_id, event_key),
    FOREIGN KEY (recording_id, event_key)
        REFERENCES events (recording_id, event_key),
    FOREIGN KEY (recording_id, context_key)
        REFERENCES browser_contexts (recording_id, identity_key)
) PARTITION BY LIST (recording_id);

CREATE TABLE browser_layout_checkpoint_completions (
    recording_id uuid NOT NULL,
    event_key bigint NOT NULL,
    context_key bigint NOT NULL,
    checkpoint_id text NOT NULL,
    reason_name_id integer NOT NULL REFERENCES names (name_id),
    node_count integer NOT NULL,
    truncated boolean NOT NULL,
    maximum_nodes integer NOT NULL,
    pseudo_element_count integer NOT NULL,
    shadow_root_count integer NOT NULL,
    PRIMARY KEY (recording_id, event_key),
    FOREIGN KEY (recording_id, event_key)
        REFERENCES events (recording_id, event_key),
    FOREIGN KEY (recording_id, context_key)
        REFERENCES browser_contexts (recording_id, identity_key)
) PARTITION BY LIST (recording_id);

CREATE TABLE browser_presentation_requests (
    recording_id uuid NOT NULL,
    event_key bigint NOT NULL,
    context_key bigint NOT NULL,
    request_id text NOT NULL,
    frame_sink_id text,
    local_root_frame_token text,
    layout_checkpoint_id text NOT NULL,
    queued boolean NOT NULL,
    not_queued_reason_name_id integer REFERENCES names (name_id),
    source_frame_number bigint,
    is_main_frame_widget boolean,
    high_resolution_ticks boolean NOT NULL,
    maximum_not_swapped_records integer NOT NULL,
    PRIMARY KEY (recording_id, event_key),
    FOREIGN KEY (recording_id, event_key)
        REFERENCES events (recording_id, event_key),
    FOREIGN KEY (recording_id, context_key)
        REFERENCES browser_contexts (recording_id, identity_key)
) PARTITION BY LIST (recording_id);

CREATE TABLE browser_presentations_not_swapped (
    recording_id uuid NOT NULL,
    event_key bigint NOT NULL,
    context_key bigint NOT NULL,
    request_id text NOT NULL,
    frame_sink_id text NOT NULL,
    local_root_frame_token text NOT NULL,
    reason_name_id integer NOT NULL REFERENCES names (name_id),
    action_name_id integer NOT NULL REFERENCES names (name_id),
    not_swapped_index integer NOT NULL,
    not_swapped_count integer NOT NULL,
    timestamp_ticks text,
    timestamp_time_ticks_microseconds text,
    PRIMARY KEY (recording_id, event_key),
    FOREIGN KEY (recording_id, event_key)
        REFERENCES events (recording_id, event_key),
    FOREIGN KEY (recording_id, context_key)
        REFERENCES browser_contexts (recording_id, identity_key)
) PARTITION BY LIST (recording_id);

CREATE TABLE browser_presentation_swaps (
    recording_id uuid NOT NULL,
    event_key bigint NOT NULL,
    context_key bigint NOT NULL,
    request_id text NOT NULL,
    frame_sink_id text NOT NULL,
    local_root_frame_token text NOT NULL,
    frame_token text NOT NULL,
    not_swapped_count integer NOT NULL,
    PRIMARY KEY (recording_id, event_key),
    FOREIGN KEY (recording_id, event_key)
        REFERENCES events (recording_id, event_key),
    FOREIGN KEY (recording_id, context_key)
        REFERENCES browser_contexts (recording_id, identity_key)
) PARTITION BY LIST (recording_id);

CREATE TABLE browser_presentation_feedback (
    recording_id uuid NOT NULL,
    event_key bigint NOT NULL,
    context_key bigint NOT NULL,
    request_id text NOT NULL,
    frame_sink_id text NOT NULL,
    local_root_frame_token text NOT NULL,
    frame_token text NOT NULL,
    presented_ticks text,
    presented_time_ticks_microseconds text,
    interval_microseconds text NOT NULL,
    received_compositor_frame_ticks text,
    draw_start_ticks text,
    swap_start_ticks text,
    swap_end_ticks text,
    high_resolution_ticks boolean NOT NULL,
    not_swapped_count integer NOT NULL,
    PRIMARY KEY (recording_id, event_key),
    FOREIGN KEY (recording_id, event_key)
        REFERENCES events (recording_id, event_key),
    FOREIGN KEY (recording_id, context_key)
        REFERENCES browser_contexts (recording_id, identity_key)
) PARTITION BY LIST (recording_id);

CREATE TABLE browser_layout_checkpoint_style_properties (
    recording_id uuid NOT NULL,
    owner_key bigint NOT NULL,
    ordinal_1 integer NOT NULL,
    value_name_id integer NOT NULL REFERENCES names (name_id),
    PRIMARY KEY (recording_id, owner_key, ordinal_1),
    FOREIGN KEY (recording_id, owner_key)
        REFERENCES browser_layout_checkpoint_starts (recording_id, event_key)
) PARTITION BY LIST (recording_id);

CREATE TABLE browser_layout_checkpoint_computed_styles (
    recording_id uuid NOT NULL,
    owner_key bigint NOT NULL,
    entry_name_id integer NOT NULL REFERENCES names (name_id),
    value text,
    PRIMARY KEY (recording_id, owner_key, entry_name_id),
    FOREIGN KEY (recording_id, owner_key)
        REFERENCES browser_layout_checkpoint_nodes (recording_id, event_key)
) PARTITION BY LIST (recording_id);

CREATE TABLE browser_presentation_feedback_flags (
    recording_id uuid NOT NULL,
    owner_key bigint NOT NULL,
    ordinal_1 integer NOT NULL,
    value_name_id integer NOT NULL REFERENCES names (name_id),
    PRIMARY KEY (recording_id, owner_key, ordinal_1),
    FOREIGN KEY (recording_id, owner_key)
        REFERENCES browser_presentation_feedback (recording_id, event_key)
) PARTITION BY LIST (recording_id);

INSERT INTO recording_partitioned_tables (table_name, partition_prefix, partition_order) VALUES
    ('browser_layout_checkpoint_starts', 'pt62', 62),
    ('browser_layout_checkpoint_nodes', 'pt63', 63),
    ('browser_layout_checkpoint_completions', 'pt64', 64),
    ('browser_presentation_requests', 'pt65', 65),
    ('browser_presentations_not_swapped', 'pt66', 66),
    ('browser_presentation_swaps', 'pt67', 67),
    ('browser_presentation_feedback', 'pt68', 68),
    ('browser_layout_checkpoint_style_properties', 'pt69', 69),
    ('browser_layout_checkpoint_computed_styles', 'pt70', 70),
    ('browser_presentation_feedback_flags', 'pt71', 71);
