-- Evidence tables. Generated from src/Recorder.Database/Evidence/EvidenceCatalog.cs
-- by EvidenceSql.Migration(); a test requires this file to match. Edit the
-- catalog, not this file. See docs/architecture/session-database.md.

-- Strings from small or recurring vocabularies, stored once and referenced
-- by key from every evidence table.
CREATE TABLE names (
    name_id integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    name text NOT NULL UNIQUE
);

CREATE TABLE windows (
    recording_id uuid NOT NULL,
    identity_key bigint NOT NULL,
    window_handle bigint NOT NULL,
    process_id bigint NOT NULL,
    thread_id bigint NOT NULL,
    process_name_name_id integer REFERENCES names (name_id),
    process_path text,
    class_name_name_id integer REFERENCES names (name_id),
    PRIMARY KEY (recording_id, identity_key)
) PARTITION BY LIST (recording_id);

CREATE TABLE monitors (
    recording_id uuid NOT NULL,
    identity_key bigint NOT NULL,
    device_name text NOT NULL,
    bounds_x integer NOT NULL,
    bounds_y integer NOT NULL,
    bounds_width integer NOT NULL,
    bounds_height integer NOT NULL,
    work_area_x integer NOT NULL,
    work_area_y integer NOT NULL,
    work_area_width integer NOT NULL,
    work_area_height integer NOT NULL,
    is_primary boolean NOT NULL,
    PRIMARY KEY (recording_id, identity_key)
) PARTITION BY LIST (recording_id);

CREATE TABLE uia_elements (
    recording_id uuid NOT NULL,
    identity_key bigint NOT NULL,
    process_id bigint,
    native_window_handle bigint,
    automation_id text,
    name text,
    class_name_name_id integer REFERENCES names (name_id),
    framework_id_name_id integer REFERENCES names (name_id),
    control_type_name_id integer REFERENCES names (name_id),
    localized_control_type_name_id integer REFERENCES names (name_id),
    has_keyboard_focus boolean,
    is_keyboard_focusable boolean,
    is_enabled boolean,
    is_offscreen boolean,
    has_bounding_rectangle boolean NOT NULL,
    bounding_rectangle_x double precision,
    bounding_rectangle_y double precision,
    bounding_rectangle_width double precision,
    bounding_rectangle_height double precision,
    property_source_name_id integer REFERENCES names (name_id),
    PRIMARY KEY (recording_id, identity_key)
) PARTITION BY LIST (recording_id);

CREATE TABLE uia_element_quality_flags (
    recording_id uuid NOT NULL,
    owner_key bigint NOT NULL,
    ordinal_1 integer NOT NULL,
    value_name_id integer NOT NULL REFERENCES names (name_id),
    PRIMARY KEY (recording_id, owner_key, ordinal_1),
    FOREIGN KEY (recording_id, owner_key)
        REFERENCES uia_elements (recording_id, identity_key)
) PARTITION BY LIST (recording_id);

CREATE TABLE recording_texts (
    recording_id uuid NOT NULL,
    identity_key bigint NOT NULL,
    value text NOT NULL,
    PRIMARY KEY (recording_id, identity_key)
) PARTITION BY LIST (recording_id);

CREATE TABLE browser_contexts (
    recording_id uuid NOT NULL,
    identity_key bigint NOT NULL,
    browser_instance_id text NOT NULL,
    process_id bigint NOT NULL,
    process_type_name_id integer NOT NULL REFERENCES names (name_id),
    profile_id text,
    browser_context_id text,
    page_id text,
    frame_id text,
    document_id text,
    execution_world_id text,
    document_token text,
    PRIMARY KEY (recording_id, identity_key)
) PARTITION BY LIST (recording_id);

CREATE TABLE collector_lifecycle_events (
    recording_id uuid NOT NULL,
    event_key bigint NOT NULL,
    action_name_id integer NOT NULL REFERENCES names (name_id),
    state_name_id integer NOT NULL REFERENCES names (name_id),
    utc timestamptz NOT NULL,
    utc_tick smallint NOT NULL,
    PRIMARY KEY (recording_id, event_key),
    FOREIGN KEY (recording_id, event_key)
        REFERENCES events (recording_id, event_key)
) PARTITION BY LIST (recording_id);

CREATE TABLE session_markers (
    recording_id uuid NOT NULL,
    event_key bigint NOT NULL,
    note text,
    PRIMARY KEY (recording_id, event_key),
    FOREIGN KEY (recording_id, event_key)
        REFERENCES events (recording_id, event_key)
) PARTITION BY LIST (recording_id);

CREATE TABLE raw_keyboard_events (
    recording_id uuid NOT NULL,
    event_key bigint NOT NULL,
    device_handle bigint NOT NULL,
    make_code integer NOT NULL,
    flags integer NOT NULL,
    virtual_key integer NOT NULL,
    message integer NOT NULL,
    extra_information bigint NOT NULL,
    PRIMARY KEY (recording_id, event_key),
    FOREIGN KEY (recording_id, event_key)
        REFERENCES events (recording_id, event_key)
) PARTITION BY LIST (recording_id);

CREATE TABLE raw_mouse_events (
    recording_id uuid NOT NULL,
    event_key bigint NOT NULL,
    device_handle bigint NOT NULL,
    movement_mode_name_id integer NOT NULL REFERENCES names (name_id),
    delta_x integer NOT NULL,
    delta_y integer NOT NULL,
    button_flags integer NOT NULL,
    button_data integer NOT NULL,
    raw_buttons bigint NOT NULL,
    extra_information bigint NOT NULL,
    cursor_x integer NOT NULL,
    cursor_y integer NOT NULL,
    foreground_process_id bigint NOT NULL,
    PRIMARY KEY (recording_id, event_key),
    FOREIGN KEY (recording_id, event_key)
        REFERENCES events (recording_id, event_key)
) PARTITION BY LIST (recording_id);

CREATE TABLE foreground_windows (
    recording_id uuid NOT NULL,
    event_key bigint NOT NULL,
    reason_name_id integer NOT NULL REFERENCES names (name_id),
    event_thread_id bigint,
    native_event_time_milliseconds bigint,
    window_key bigint NOT NULL,
    title text,
    is_visible boolean NOT NULL,
    is_minimized boolean NOT NULL,
    is_maximized boolean NOT NULL,
    is_cloaked boolean,
    dpi integer,
    has_bounds boolean NOT NULL,
    bounds_x integer,
    bounds_y integer,
    bounds_width integer,
    bounds_height integer,
    monitor_key bigint,
    PRIMARY KEY (recording_id, event_key),
    FOREIGN KEY (recording_id, event_key)
        REFERENCES events (recording_id, event_key),
    FOREIGN KEY (recording_id, window_key)
        REFERENCES windows (recording_id, identity_key),
    FOREIGN KEY (recording_id, monitor_key)
        REFERENCES monitors (recording_id, identity_key)
) PARTITION BY LIST (recording_id);

CREATE TABLE desktop_frames (
    recording_id uuid NOT NULL,
    event_key bigint NOT NULL,
    path text NOT NULL,
    x integer NOT NULL,
    y integer NOT NULL,
    width integer NOT NULL,
    height integer NOT NULL,
    stride integer NOT NULL,
    pixel_format_name_id integer NOT NULL REFERENCES names (name_id),
    encoded_format_name_id integer NOT NULL REFERENCES names (name_id),
    byte_length bigint NOT NULL,
    capture_duration_nanoseconds bigint NOT NULL,
    frames_per_second integer NOT NULL,
    backend_name_id integer NOT NULL REFERENCES names (name_id),
    monitor_count integer,
    fallback_reason text,
    gdi_fallback_frame_count bigint NOT NULL,
    frame_selection_name_id integer REFERENCES names (name_id),
    has_monitor_frames boolean NOT NULL,
    PRIMARY KEY (recording_id, event_key),
    FOREIGN KEY (recording_id, event_key)
        REFERENCES events (recording_id, event_key)
) PARTITION BY LIST (recording_id);

CREATE TABLE browser_connections (
    recording_id uuid NOT NULL,
    event_key bigint NOT NULL,
    protocol_version_name_id integer NOT NULL REFERENCES names (name_id),
    browser_instance_id text NOT NULL,
    process_id bigint NOT NULL,
    process_type_name_id integer NOT NULL REFERENCES names (name_id),
    chromium_version_name_id integer NOT NULL REFERENCES names (name_id),
    parent_process_id bigint,
    child_process_id bigint,
    PRIMARY KEY (recording_id, event_key),
    FOREIGN KEY (recording_id, event_key)
        REFERENCES events (recording_id, event_key)
) PARTITION BY LIST (recording_id);

CREATE TABLE browser_exits (
    recording_id uuid NOT NULL,
    event_key bigint NOT NULL,
    browser_instance_id text NOT NULL,
    process_id bigint NOT NULL,
    exit_code bigint NOT NULL,
    exit_code_hex text NOT NULL,
    exited_utc timestamptz,
    exited_utc_tick smallint,
    requested_by_recorder boolean NOT NULL,
    PRIMARY KEY (recording_id, event_key),
    FOREIGN KEY (recording_id, event_key)
        REFERENCES events (recording_id, event_key)
) PARTITION BY LIST (recording_id);

CREATE TABLE browser_clock_synchronizations (
    recording_id uuid NOT NULL,
    event_key bigint NOT NULL,
    protocol_version_name_id integer NOT NULL REFERENCES names (name_id),
    browser_instance_id text NOT NULL,
    process_id bigint NOT NULL,
    process_type_name_id integer NOT NULL REFERENCES names (name_id),
    parent_process_id bigint,
    child_process_id bigint,
    clock_mapping_id text NOT NULL,
    monotonic_frequency text NOT NULL,
    uncertainty_nanoseconds bigint NOT NULL,
    PRIMARY KEY (recording_id, event_key),
    FOREIGN KEY (recording_id, event_key)
        REFERENCES events (recording_id, event_key)
) PARTITION BY LIST (recording_id);

CREATE TABLE uia_events (
    recording_id uuid NOT NULL,
    event_key bigint NOT NULL,
    event_id_name_id integer NOT NULL REFERENCES names (name_id),
    change_type_name_id integer REFERENCES names (name_id),
    has_runtime_id boolean NOT NULL,
    new_value text,
    element_key bigint NOT NULL,
    PRIMARY KEY (recording_id, event_key),
    FOREIGN KEY (recording_id, event_key)
        REFERENCES events (recording_id, event_key),
    FOREIGN KEY (recording_id, element_key)
        REFERENCES uia_elements (recording_id, identity_key)
) PARTITION BY LIST (recording_id);

CREATE TABLE audio_stream_events (
    recording_id uuid NOT NULL,
    event_key bigint NOT NULL,
    stream_name_id integer NOT NULL REFERENCES names (name_id),
    path text NOT NULL,
    device text NOT NULL,
    endpoint_volume_scalar double precision,
    format_encoding_name_id integer NOT NULL REFERENCES names (name_id),
    format_sample_rate integer NOT NULL,
    format_channels integer NOT NULL,
    format_bits_per_sample integer NOT NULL,
    format_block_align integer NOT NULL,
    format_average_bytes_per_second integer NOT NULL,
    data_bytes bigint NOT NULL,
    buffers_observed bigint NOT NULL,
    buffers_dropped bigint NOT NULL,
    peak_amplitude double precision,
    peak_dbfs double precision,
    rms_amplitude double precision,
    rms_dbfs double precision,
    PRIMARY KEY (recording_id, event_key),
    FOREIGN KEY (recording_id, event_key)
        REFERENCES events (recording_id, event_key)
) PARTITION BY LIST (recording_id);

CREATE TABLE audio_buffers (
    recording_id uuid NOT NULL,
    event_key bigint NOT NULL,
    stream_name_id integer NOT NULL REFERENCES names (name_id),
    path_key bigint NOT NULL,
    buffer_sequence bigint NOT NULL,
    data_byte_offset bigint NOT NULL,
    byte_length bigint NOT NULL,
    sample_frames bigint NOT NULL,
    duration_nanoseconds bigint NOT NULL,
    estimated_first_sample_monotonic_nanoseconds bigint NOT NULL,
    callback_monotonic_nanoseconds bigint NOT NULL,
    PRIMARY KEY (recording_id, event_key),
    FOREIGN KEY (recording_id, event_key)
        REFERENCES events (recording_id, event_key),
    FOREIGN KEY (recording_id, path_key)
        REFERENCES recording_texts (recording_id, identity_key)
) PARTITION BY LIST (recording_id);

CREATE TABLE audio_stream_errors (
    recording_id uuid NOT NULL,
    event_key bigint NOT NULL,
    stream_name_id integer NOT NULL REFERENCES names (name_id),
    error_type_name_id integer REFERENCES names (name_id),
    message text,
    PRIMARY KEY (recording_id, event_key),
    FOREIGN KEY (recording_id, event_key)
        REFERENCES events (recording_id, event_key)
) PARTITION BY LIST (recording_id);

CREATE TABLE collector_omissions (
    recording_id uuid NOT NULL,
    event_key bigint NOT NULL,
    reason_name_id integer NOT NULL REFERENCES names (name_id),
    count bigint,
    stream_name_id integer REFERENCES names (name_id),
    first_dropped_at_nanoseconds bigint,
    last_dropped_at_nanoseconds bigint,
    has_dropped_by_observation_type boolean NOT NULL,
    context_key bigint,
    PRIMARY KEY (recording_id, event_key),
    FOREIGN KEY (recording_id, event_key)
        REFERENCES events (recording_id, event_key),
    FOREIGN KEY (recording_id, context_key)
        REFERENCES browser_contexts (recording_id, identity_key)
) PARTITION BY LIST (recording_id);

CREATE TABLE desktop_frame_monitors (
    recording_id uuid NOT NULL,
    owner_key bigint NOT NULL,
    ordinal_1 integer NOT NULL,
    monitor_handle bigint NOT NULL,
    x integer NOT NULL,
    y integer NOT NULL,
    width integer NOT NULL,
    height integer NOT NULL,
    system_relative_time_ticks bigint,
    composited_at_nanoseconds bigint,
    dequeued_at_nanoseconds bigint,
    try_get_next_frame_attempts integer,
    superseded_frame_count bigint,
    reused_previous_image boolean,
    PRIMARY KEY (recording_id, owner_key, ordinal_1),
    FOREIGN KEY (recording_id, owner_key)
        REFERENCES desktop_frames (recording_id, event_key)
) PARTITION BY LIST (recording_id);

CREATE TABLE uia_event_runtime_ids (
    recording_id uuid NOT NULL,
    owner_key bigint NOT NULL,
    ordinal_1 integer NOT NULL,
    value bigint NOT NULL,
    PRIMARY KEY (recording_id, owner_key, ordinal_1),
    FOREIGN KEY (recording_id, owner_key)
        REFERENCES uia_events (recording_id, event_key)
) PARTITION BY LIST (recording_id);

CREATE TABLE collector_omission_dropped_counts (
    recording_id uuid NOT NULL,
    owner_key bigint NOT NULL,
    entry_name_id integer NOT NULL REFERENCES names (name_id),
    value bigint NOT NULL,
    PRIMARY KEY (recording_id, owner_key, entry_name_id),
    FOREIGN KEY (recording_id, owner_key)
        REFERENCES collector_omissions (recording_id, event_key)
) PARTITION BY LIST (recording_id);

INSERT INTO recording_partitioned_tables (table_name, partition_prefix, partition_order) VALUES
    ('windows', 'pt7', 7),
    ('monitors', 'pt8', 8),
    ('uia_elements', 'pt9', 9),
    ('uia_element_quality_flags', 'pt10', 10),
    ('recording_texts', 'pt11', 11),
    ('browser_contexts', 'pt12', 12),
    ('collector_lifecycle_events', 'pt13', 13),
    ('session_markers', 'pt14', 14),
    ('raw_keyboard_events', 'pt15', 15),
    ('raw_mouse_events', 'pt16', 16),
    ('foreground_windows', 'pt17', 17),
    ('desktop_frames', 'pt18', 18),
    ('browser_connections', 'pt19', 19),
    ('browser_exits', 'pt20', 20),
    ('browser_clock_synchronizations', 'pt21', 21),
    ('uia_events', 'pt22', 22),
    ('audio_stream_events', 'pt23', 23),
    ('audio_buffers', 'pt24', 24),
    ('audio_stream_errors', 'pt25', 25),
    ('collector_omissions', 'pt26', 26),
    ('desktop_frame_monitors', 'pt27', 27),
    ('uia_event_runtime_ids', 'pt28', 28),
    ('collector_omission_dropped_counts', 'pt29', 29);
