-- Evidence tables. Generated from src/Recorder.Database/Evidence/EvidenceCatalog.cs
-- by EvidenceSql.Migration(); a test requires this file to match. Edit the
-- catalog, not this file. See docs/architecture/session-database.md.

CREATE TABLE browser_network_scopes (
    recording_id uuid NOT NULL,
    identity_key bigint NOT NULL,
    context_kind_name_id integer NOT NULL REFERENCES names (name_id),
    worker_token text,
    global_object_url text,
    PRIMARY KEY (recording_id, identity_key)
) PARTITION BY LIST (recording_id);

CREATE TABLE browser_document_cookie_reads (
    recording_id uuid NOT NULL,
    event_key bigint NOT NULL,
    context_key bigint NOT NULL,
    access_id text NOT NULL,
    cookie_url text,
    outcome_name_id integer NOT NULL REFERENCES names (name_id),
    served_from_name_id integer REFERENCES names (name_id),
    cookie_count integer NOT NULL,
    cookie_names_truncated boolean NOT NULL,
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

CREATE TABLE browser_document_cookie_writes (
    recording_id uuid NOT NULL,
    event_key bigint NOT NULL,
    context_key bigint NOT NULL,
    access_id text NOT NULL,
    cookie_url text,
    outcome_name_id integer NOT NULL REFERENCES names (name_id),
    name text NOT NULL,
    attributes_domain text,
    attributes_path text,
    attributes_same_site_name_id integer REFERENCES names (name_id),
    attributes_partitioned boolean NOT NULL,
    attributes_expires_present boolean NOT NULL,
    attributes_secure boolean NOT NULL,
    attributes_http_only boolean NOT NULL,
    attributes_max_age_present boolean NOT NULL,
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

CREATE TABLE browser_cookie_store_requests (
    recording_id uuid NOT NULL,
    event_key bigint NOT NULL,
    context_key bigint NOT NULL,
    request_id text NOT NULL,
    method_name_id integer NOT NULL REFERENCES names (name_id),
    context_kind_name_id integer NOT NULL REFERENCES names (name_id),
    outcome_name_id integer NOT NULL REFERENCES names (name_id),
    name text,
    url text,
    has_attributes boolean NOT NULL,
    attributes_domain text,
    attributes_path text,
    attributes_same_site_name_id integer REFERENCES names (name_id),
    attributes_partitioned boolean,
    attributes_expires_present boolean,
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

CREATE TABLE browser_cookie_store_results (
    recording_id uuid NOT NULL,
    event_key bigint NOT NULL,
    context_key bigint NOT NULL,
    request_id text NOT NULL,
    method_name_id integer NOT NULL REFERENCES names (name_id),
    outcome_name_id integer NOT NULL REFERENCES names (name_id),
    success boolean,
    cookie_count integer,
    has_cookie_names boolean NOT NULL,
    cookie_names_truncated boolean,
    PRIMARY KEY (recording_id, event_key),
    FOREIGN KEY (recording_id, event_key)
        REFERENCES events (recording_id, event_key),
    FOREIGN KEY (recording_id, context_key)
        REFERENCES browser_contexts (recording_id, identity_key)
) PARTITION BY LIST (recording_id);

CREATE TABLE browser_cookie_store_changes (
    recording_id uuid NOT NULL,
    event_key bigint NOT NULL,
    context_key bigint NOT NULL,
    context_kind_name_id integer NOT NULL REFERENCES names (name_id),
    name text NOT NULL,
    domain text NOT NULL,
    path text NOT NULL,
    cause_name_id integer NOT NULL REFERENCES names (name_id),
    dispatched boolean NOT NULL,
    PRIMARY KEY (recording_id, event_key),
    FOREIGN KEY (recording_id, event_key)
        REFERENCES events (recording_id, event_key),
    FOREIGN KEY (recording_id, context_key)
        REFERENCES browser_contexts (recording_id, identity_key)
) PARTITION BY LIST (recording_id);

CREATE TABLE browser_cookie_accesses (
    recording_id uuid NOT NULL,
    event_key bigint NOT NULL,
    context_key bigint NOT NULL,
    observer_name_id integer NOT NULL REFERENCES names (name_id),
    navigation_id text,
    renderer_process_id bigint,
    access_type_name_id integer NOT NULL REFERENCES names (name_id),
    url text NOT NULL,
    frame_origin text,
    top_frame_origin text,
    request_id text,
    ad_tagged boolean NOT NULL,
    cookie_count integer NOT NULL,
    cookies_truncated boolean NOT NULL,
    PRIMARY KEY (recording_id, event_key),
    FOREIGN KEY (recording_id, event_key)
        REFERENCES events (recording_id, event_key),
    FOREIGN KEY (recording_id, context_key)
        REFERENCES browser_contexts (recording_id, identity_key)
) PARTITION BY LIST (recording_id);

CREATE TABLE browser_network_requests (
    recording_id uuid NOT NULL,
    event_key bigint NOT NULL,
    context_key bigint NOT NULL,
    scope_key bigint NOT NULL,
    request_inspector_id text NOT NULL,
    request_request_id text,
    request_url text NOT NULL,
    request_method_name_id integer NOT NULL REFERENCES names (name_id),
    request_resource_type_name_id integer NOT NULL REFERENCES names (name_id),
    request_initiator_type_name_id integer REFERENCES names (name_id),
    request_initiator_url text,
    request_initiator_line_number integer,
    request_initiator_column_number integer,
    request_initiator_link_preload boolean NOT NULL,
    request_internal boolean NOT NULL,
    request_destination_name_id integer NOT NULL REFERENCES names (name_id),
    request_mode_name_id integer NOT NULL REFERENCES names (name_id),
    request_credentials_mode_name_id integer NOT NULL REFERENCES names (name_id),
    request_redirect_mode_name_id integer NOT NULL REFERENCES names (name_id),
    request_cache_mode_name_id integer NOT NULL REFERENCES names (name_id),
    request_priority_name_id integer NOT NULL REFERENCES names (name_id),
    request_initial_priority_name_id integer NOT NULL REFERENCES names (name_id),
    request_fetch_priority_hint_name_id integer NOT NULL REFERENCES names (name_id),
    request_render_blocking_name_id integer NOT NULL REFERENCES names (name_id),
    request_referrer text,
    request_referrer_policy_name_id integer NOT NULL REFERENCES names (name_id),
    request_keepalive boolean NOT NULL,
    request_user_gesture boolean NOT NULL,
    request_ad_resource boolean NOT NULL,
    request_form_submission boolean NOT NULL,
    request_header_count integer NOT NULL,
    request_headers_truncated boolean NOT NULL,
    redirect boolean NOT NULL,
    has_redirect boolean NOT NULL,
    redirect_url text,
    redirect_response_url text,
    redirect_status integer,
    redirect_status_text text,
    redirect_mime_type_name_id integer REFERENCES names (name_id),
    redirect_charset_name_id integer REFERENCES names (name_id),
    redirect_alpn_protocol_name_id integer REFERENCES names (name_id),
    redirect_connection_info_name_id integer REFERENCES names (name_id),
    has_redirect_remote_address boolean,
    redirect_remote_address_ip text,
    redirect_remote_address_port integer,
    redirect_connection_id double precision,
    redirect_connection_reused boolean,
    redirect_was_cached boolean,
    redirect_fetched_via_service_worker boolean,
    redirect_service_worker_response_source_name_id integer REFERENCES names (name_id),
    redirect_in_prefetch_cache boolean,
    redirect_network_accessed boolean,
    redirect_from_archive boolean,
    redirect_cookie_in_request boolean,
    redirect_response_type_name_id integer REFERENCES names (name_id),
    redirect_encoded_data_length double precision,
    redirect_expected_content_length double precision,
    redirect_header_count integer,
    redirect_headers_truncated boolean,
    has_redirect_timing boolean,
    redirect_timing_request_start_before_record_milliseconds double precision,
    redirect_timing_proxy_start double precision,
    redirect_timing_proxy_end double precision,
    redirect_timing_domain_lookup_start double precision,
    redirect_timing_domain_lookup_end double precision,
    redirect_timing_connect_start double precision,
    redirect_timing_connect_end double precision,
    redirect_timing_ssl_start double precision,
    redirect_timing_ssl_end double precision,
    redirect_timing_worker_start double precision,
    redirect_timing_worker_ready double precision,
    redirect_timing_worker_fetch_start double precision,
    redirect_timing_worker_respond_with_settled double precision,
    redirect_timing_worker_router_evaluation_start double precision,
    redirect_timing_worker_cache_lookup_start double precision,
    redirect_timing_send_start double precision,
    redirect_timing_send_end double precision,
    redirect_timing_receive_headers_start double precision,
    redirect_timing_receive_headers_end double precision,
    redirect_timing_receive_non_informational_headers_start double precision,
    redirect_timing_receive_early_hints_start double precision,
    redirect_timing_push_start double precision,
    redirect_timing_push_end double precision,
    redirect_timing_response_end double precision,
    location_key bigint,
    world_key bigint,
    PRIMARY KEY (recording_id, event_key),
    FOREIGN KEY (recording_id, event_key)
        REFERENCES events (recording_id, event_key),
    FOREIGN KEY (recording_id, context_key)
        REFERENCES browser_contexts (recording_id, identity_key),
    FOREIGN KEY (recording_id, scope_key)
        REFERENCES browser_network_scopes (recording_id, identity_key),
    FOREIGN KEY (recording_id, location_key)
        REFERENCES script_locations (recording_id, identity_key),
    FOREIGN KEY (recording_id, world_key)
        REFERENCES execution_worlds (recording_id, identity_key)
) PARTITION BY LIST (recording_id);

CREATE TABLE browser_network_responses (
    recording_id uuid NOT NULL,
    event_key bigint NOT NULL,
    context_key bigint NOT NULL,
    scope_key bigint NOT NULL,
    inspector_id text NOT NULL,
    request_id text,
    response_source_name_id integer NOT NULL REFERENCES names (name_id),
    response_url text NOT NULL,
    response_response_url text,
    response_status integer NOT NULL,
    response_status_text text NOT NULL,
    response_mime_type_name_id integer NOT NULL REFERENCES names (name_id),
    response_charset_name_id integer REFERENCES names (name_id),
    response_alpn_protocol_name_id integer REFERENCES names (name_id),
    response_connection_info_name_id integer REFERENCES names (name_id),
    has_response_remote_address boolean NOT NULL,
    response_remote_address_ip text,
    response_remote_address_port integer,
    response_connection_id double precision NOT NULL,
    response_connection_reused boolean NOT NULL,
    response_was_cached boolean NOT NULL,
    response_fetched_via_service_worker boolean NOT NULL,
    response_service_worker_response_source_name_id integer NOT NULL REFERENCES names (name_id),
    response_in_prefetch_cache boolean NOT NULL,
    response_network_accessed boolean NOT NULL,
    response_from_archive boolean NOT NULL,
    response_cookie_in_request boolean NOT NULL,
    response_response_type_name_id integer NOT NULL REFERENCES names (name_id),
    response_encoded_data_length double precision,
    response_expected_content_length double precision NOT NULL,
    response_header_count integer NOT NULL,
    response_headers_truncated boolean NOT NULL,
    has_response_timing boolean NOT NULL,
    response_timing_request_start_before_record_milliseconds double precision,
    response_timing_proxy_start double precision,
    response_timing_proxy_end double precision,
    response_timing_domain_lookup_start double precision,
    response_timing_domain_lookup_end double precision,
    response_timing_connect_start double precision,
    response_timing_connect_end double precision,
    response_timing_ssl_start double precision,
    response_timing_ssl_end double precision,
    response_timing_worker_start double precision,
    response_timing_worker_ready double precision,
    response_timing_worker_fetch_start double precision,
    response_timing_worker_respond_with_settled double precision,
    response_timing_worker_router_evaluation_start double precision,
    response_timing_worker_cache_lookup_start double precision,
    response_timing_send_start double precision,
    response_timing_send_end double precision,
    response_timing_receive_headers_start double precision,
    response_timing_receive_headers_end double precision,
    response_timing_receive_non_informational_headers_start double precision,
    response_timing_receive_early_hints_start double precision,
    response_timing_push_start double precision,
    response_timing_push_end double precision,
    response_timing_response_end double precision,
    PRIMARY KEY (recording_id, event_key),
    FOREIGN KEY (recording_id, event_key)
        REFERENCES events (recording_id, event_key),
    FOREIGN KEY (recording_id, context_key)
        REFERENCES browser_contexts (recording_id, identity_key),
    FOREIGN KEY (recording_id, scope_key)
        REFERENCES browser_network_scopes (recording_id, identity_key)
) PARTITION BY LIST (recording_id);

CREATE TABLE browser_network_request_finishes (
    recording_id uuid NOT NULL,
    event_key bigint NOT NULL,
    context_key bigint NOT NULL,
    scope_key bigint NOT NULL,
    inspector_id text NOT NULL,
    encoded_data_length double precision,
    decoded_body_length double precision NOT NULL,
    finish_before_record_milliseconds double precision,
    PRIMARY KEY (recording_id, event_key),
    FOREIGN KEY (recording_id, event_key)
        REFERENCES events (recording_id, event_key),
    FOREIGN KEY (recording_id, context_key)
        REFERENCES browser_contexts (recording_id, identity_key),
    FOREIGN KEY (recording_id, scope_key)
        REFERENCES browser_network_scopes (recording_id, identity_key)
) PARTITION BY LIST (recording_id);

CREATE TABLE browser_network_request_failures (
    recording_id uuid NOT NULL,
    event_key bigint NOT NULL,
    context_key bigint NOT NULL,
    scope_key bigint NOT NULL,
    inspector_id text NOT NULL,
    url text NOT NULL,
    net_error integer NOT NULL,
    net_error_name_name_id integer REFERENCES names (name_id),
    cancellation boolean NOT NULL,
    timeout boolean NOT NULL,
    access_check boolean NOT NULL,
    blocked_by_response boolean NOT NULL,
    blocked_by_orb boolean NOT NULL,
    has_copy_in_cache boolean NOT NULL,
    cancelled_from_http_error boolean NOT NULL,
    internal boolean NOT NULL,
    blocked_reason_name_id integer REFERENCES names (name_id),
    has_cors_error boolean NOT NULL,
    cors_error_error_name_id integer REFERENCES names (name_id),
    cors_error_failed_parameter text,
    PRIMARY KEY (recording_id, event_key),
    FOREIGN KEY (recording_id, event_key)
        REFERENCES events (recording_id, event_key),
    FOREIGN KEY (recording_id, context_key)
        REFERENCES browser_contexts (recording_id, identity_key),
    FOREIGN KEY (recording_id, scope_key)
        REFERENCES browser_network_scopes (recording_id, identity_key)
) PARTITION BY LIST (recording_id);

CREATE TABLE browser_network_memory_cache_hits (
    recording_id uuid NOT NULL,
    event_key bigint NOT NULL,
    context_key bigint NOT NULL,
    scope_key bigint NOT NULL,
    static_data boolean NOT NULL,
    request_inspector_id text NOT NULL,
    request_request_id text,
    request_url text NOT NULL,
    request_method_name_id integer NOT NULL REFERENCES names (name_id),
    request_resource_type_name_id integer NOT NULL REFERENCES names (name_id),
    request_initiator_type_name_id integer REFERENCES names (name_id),
    request_initiator_url text,
    request_initiator_line_number integer,
    request_initiator_column_number integer,
    request_initiator_link_preload boolean NOT NULL,
    request_internal boolean NOT NULL,
    request_destination_name_id integer NOT NULL REFERENCES names (name_id),
    request_mode_name_id integer NOT NULL REFERENCES names (name_id),
    request_credentials_mode_name_id integer NOT NULL REFERENCES names (name_id),
    request_redirect_mode_name_id integer NOT NULL REFERENCES names (name_id),
    request_cache_mode_name_id integer NOT NULL REFERENCES names (name_id),
    request_priority_name_id integer NOT NULL REFERENCES names (name_id),
    request_initial_priority_name_id integer NOT NULL REFERENCES names (name_id),
    request_fetch_priority_hint_name_id integer NOT NULL REFERENCES names (name_id),
    request_render_blocking_name_id integer NOT NULL REFERENCES names (name_id),
    request_referrer text,
    request_referrer_policy_name_id integer NOT NULL REFERENCES names (name_id),
    request_keepalive boolean NOT NULL,
    request_user_gesture boolean NOT NULL,
    request_ad_resource boolean NOT NULL,
    request_form_submission boolean NOT NULL,
    request_header_count integer NOT NULL,
    request_headers_truncated boolean NOT NULL,
    response_url text NOT NULL,
    response_response_url text,
    response_status integer NOT NULL,
    response_status_text text NOT NULL,
    response_mime_type_name_id integer NOT NULL REFERENCES names (name_id),
    response_charset_name_id integer REFERENCES names (name_id),
    response_alpn_protocol_name_id integer REFERENCES names (name_id),
    response_connection_info_name_id integer REFERENCES names (name_id),
    has_response_remote_address boolean NOT NULL,
    response_remote_address_ip text,
    response_remote_address_port integer,
    response_connection_id double precision NOT NULL,
    response_connection_reused boolean NOT NULL,
    response_was_cached boolean NOT NULL,
    response_fetched_via_service_worker boolean NOT NULL,
    response_service_worker_response_source_name_id integer NOT NULL REFERENCES names (name_id),
    response_in_prefetch_cache boolean NOT NULL,
    response_network_accessed boolean NOT NULL,
    response_from_archive boolean NOT NULL,
    response_cookie_in_request boolean NOT NULL,
    response_response_type_name_id integer NOT NULL REFERENCES names (name_id),
    response_encoded_data_length double precision,
    response_expected_content_length double precision NOT NULL,
    response_header_count integer NOT NULL,
    response_headers_truncated boolean NOT NULL,
    has_response_timing boolean NOT NULL,
    response_timing_request_start_before_record_milliseconds double precision,
    response_timing_proxy_start double precision,
    response_timing_proxy_end double precision,
    response_timing_domain_lookup_start double precision,
    response_timing_domain_lookup_end double precision,
    response_timing_connect_start double precision,
    response_timing_connect_end double precision,
    response_timing_ssl_start double precision,
    response_timing_ssl_end double precision,
    response_timing_worker_start double precision,
    response_timing_worker_ready double precision,
    response_timing_worker_fetch_start double precision,
    response_timing_worker_respond_with_settled double precision,
    response_timing_worker_router_evaluation_start double precision,
    response_timing_worker_cache_lookup_start double precision,
    response_timing_send_start double precision,
    response_timing_send_end double precision,
    response_timing_receive_headers_start double precision,
    response_timing_receive_headers_end double precision,
    response_timing_receive_non_informational_headers_start double precision,
    response_timing_receive_early_hints_start double precision,
    response_timing_push_start double precision,
    response_timing_push_end double precision,
    response_timing_response_end double precision,
    PRIMARY KEY (recording_id, event_key),
    FOREIGN KEY (recording_id, event_key)
        REFERENCES events (recording_id, event_key),
    FOREIGN KEY (recording_id, context_key)
        REFERENCES browser_contexts (recording_id, identity_key),
    FOREIGN KEY (recording_id, scope_key)
        REFERENCES browser_network_scopes (recording_id, identity_key)
) PARTITION BY LIST (recording_id);

CREATE TABLE browser_network_request_headers_sent (
    recording_id uuid NOT NULL,
    event_key bigint NOT NULL,
    context_key bigint NOT NULL,
    devtools_agent_id text,
    request_id text NOT NULL,
    header_count integer NOT NULL,
    headers_truncated boolean NOT NULL,
    cookie_count integer NOT NULL,
    cookies_truncated boolean NOT NULL,
    sent_before_record_milliseconds double precision,
    PRIMARY KEY (recording_id, event_key),
    FOREIGN KEY (recording_id, event_key)
        REFERENCES events (recording_id, event_key),
    FOREIGN KEY (recording_id, context_key)
        REFERENCES browser_contexts (recording_id, identity_key)
) PARTITION BY LIST (recording_id);

CREATE TABLE browser_network_response_headers_received (
    recording_id uuid NOT NULL,
    event_key bigint NOT NULL,
    context_key bigint NOT NULL,
    devtools_agent_id text,
    request_id text NOT NULL,
    header_count integer NOT NULL,
    headers_truncated boolean NOT NULL,
    cookie_count integer NOT NULL,
    cookies_truncated boolean NOT NULL,
    status integer NOT NULL,
    PRIMARY KEY (recording_id, event_key),
    FOREIGN KEY (recording_id, event_key)
        REFERENCES events (recording_id, event_key),
    FOREIGN KEY (recording_id, context_key)
        REFERENCES browser_contexts (recording_id, identity_key)
) PARTITION BY LIST (recording_id);

CREATE TABLE browser_network_navigation_responses (
    recording_id uuid NOT NULL,
    event_key bigint NOT NULL,
    context_key bigint NOT NULL,
    navigation_id text NOT NULL,
    request_id text,
    url text NOT NULL,
    method_name_id integer NOT NULL REFERENCES names (name_id),
    committed boolean NOT NULL,
    error_page boolean NOT NULL,
    same_document boolean NOT NULL,
    download boolean NOT NULL,
    back_forward_cache boolean NOT NULL,
    net_error integer NOT NULL,
    net_error_name_name_id integer REFERENCES names (name_id),
    request_header_count integer NOT NULL,
    request_headers_truncated boolean NOT NULL,
    has_response boolean NOT NULL,
    response_status integer,
    response_status_text text,
    response_mime_type_name_id integer REFERENCES names (name_id),
    response_was_cached boolean,
    has_response_remote_address boolean,
    response_remote_address_ip text,
    response_remote_address_port integer,
    response_connection_info_name_id integer REFERENCES names (name_id),
    response_header_count integer,
    response_headers_truncated boolean,
    has_timing boolean NOT NULL,
    timing_navigation_start_before_record_milliseconds double precision,
    timing_loader_start double precision,
    timing_first_request_start double precision,
    timing_first_response_start double precision,
    timing_first_loader_callback double precision,
    timing_final_request_start double precision,
    timing_final_response_start double precision,
    timing_final_non_informational_response_start double precision,
    timing_final_loader_callback double precision,
    timing_request_failed double precision,
    timing_commit_sent double precision,
    timing_commit_received double precision,
    timing_commit_reply_sent double precision,
    timing_did_commit double precision,
    timing_final_request_domain_lookup_start double precision,
    timing_final_request_domain_lookup_end double precision,
    timing_final_request_connect_start double precision,
    timing_final_request_connect_end double precision,
    timing_final_request_ssl_start double precision,
    PRIMARY KEY (recording_id, event_key),
    FOREIGN KEY (recording_id, event_key)
        REFERENCES events (recording_id, event_key),
    FOREIGN KEY (recording_id, context_key)
        REFERENCES browser_contexts (recording_id, identity_key)
) PARTITION BY LIST (recording_id);

CREATE TABLE browser_websocket_creations (
    recording_id uuid NOT NULL,
    event_key bigint NOT NULL,
    context_key bigint NOT NULL,
    scope_key bigint NOT NULL,
    location_key bigint,
    world_key bigint,
    inspector_id text NOT NULL,
    url text NOT NULL,
    requested_protocols text,
    PRIMARY KEY (recording_id, event_key),
    FOREIGN KEY (recording_id, event_key)
        REFERENCES events (recording_id, event_key),
    FOREIGN KEY (recording_id, context_key)
        REFERENCES browser_contexts (recording_id, identity_key),
    FOREIGN KEY (recording_id, scope_key)
        REFERENCES browser_network_scopes (recording_id, identity_key),
    FOREIGN KEY (recording_id, location_key)
        REFERENCES script_locations (recording_id, identity_key),
    FOREIGN KEY (recording_id, world_key)
        REFERENCES execution_worlds (recording_id, identity_key)
) PARTITION BY LIST (recording_id);

CREATE TABLE browser_websocket_handshake_requests (
    recording_id uuid NOT NULL,
    event_key bigint NOT NULL,
    context_key bigint NOT NULL,
    scope_key bigint NOT NULL,
    inspector_id text NOT NULL,
    url text NOT NULL,
    header_count integer NOT NULL,
    headers_truncated boolean NOT NULL,
    PRIMARY KEY (recording_id, event_key),
    FOREIGN KEY (recording_id, event_key)
        REFERENCES events (recording_id, event_key),
    FOREIGN KEY (recording_id, context_key)
        REFERENCES browser_contexts (recording_id, identity_key),
    FOREIGN KEY (recording_id, scope_key)
        REFERENCES browser_network_scopes (recording_id, identity_key)
) PARTITION BY LIST (recording_id);

CREATE TABLE browser_websocket_handshake_responses (
    recording_id uuid NOT NULL,
    event_key bigint NOT NULL,
    context_key bigint NOT NULL,
    scope_key bigint NOT NULL,
    inspector_id text NOT NULL,
    url text,
    http_version_name_id integer REFERENCES names (name_id),
    status integer NOT NULL,
    status_text text,
    has_remote_address boolean NOT NULL,
    remote_address_ip text,
    remote_address_port integer,
    selected_protocol text,
    header_count integer NOT NULL,
    headers_truncated boolean NOT NULL,
    extensions text,
    PRIMARY KEY (recording_id, event_key),
    FOREIGN KEY (recording_id, event_key)
        REFERENCES events (recording_id, event_key),
    FOREIGN KEY (recording_id, context_key)
        REFERENCES browser_contexts (recording_id, identity_key),
    FOREIGN KEY (recording_id, scope_key)
        REFERENCES browser_network_scopes (recording_id, identity_key)
) PARTITION BY LIST (recording_id);

CREATE TABLE browser_websocket_messages_sent (
    recording_id uuid NOT NULL,
    event_key bigint NOT NULL,
    context_key bigint NOT NULL,
    scope_key bigint NOT NULL,
    location_key bigint,
    world_key bigint,
    inspector_id text NOT NULL,
    opcode_name_id integer NOT NULL REFERENCES names (name_id),
    payload_length double precision NOT NULL,
    has_payload boolean NOT NULL,
    payload_text text,
    payload_truncated boolean,
    PRIMARY KEY (recording_id, event_key),
    FOREIGN KEY (recording_id, event_key)
        REFERENCES events (recording_id, event_key),
    FOREIGN KEY (recording_id, context_key)
        REFERENCES browser_contexts (recording_id, identity_key),
    FOREIGN KEY (recording_id, scope_key)
        REFERENCES browser_network_scopes (recording_id, identity_key),
    FOREIGN KEY (recording_id, location_key)
        REFERENCES script_locations (recording_id, identity_key),
    FOREIGN KEY (recording_id, world_key)
        REFERENCES execution_worlds (recording_id, identity_key)
) PARTITION BY LIST (recording_id);

CREATE TABLE browser_websocket_messages_received (
    recording_id uuid NOT NULL,
    event_key bigint NOT NULL,
    context_key bigint NOT NULL,
    scope_key bigint NOT NULL,
    inspector_id text NOT NULL,
    opcode_name_id integer NOT NULL REFERENCES names (name_id),
    payload_length double precision NOT NULL,
    has_payload boolean NOT NULL,
    payload_text text,
    payload_truncated boolean,
    PRIMARY KEY (recording_id, event_key),
    FOREIGN KEY (recording_id, event_key)
        REFERENCES events (recording_id, event_key),
    FOREIGN KEY (recording_id, context_key)
        REFERENCES browser_contexts (recording_id, identity_key),
    FOREIGN KEY (recording_id, scope_key)
        REFERENCES browser_network_scopes (recording_id, identity_key)
) PARTITION BY LIST (recording_id);

CREATE TABLE browser_websocket_close_requests (
    recording_id uuid NOT NULL,
    event_key bigint NOT NULL,
    context_key bigint NOT NULL,
    scope_key bigint NOT NULL,
    location_key bigint,
    world_key bigint,
    inspector_id text NOT NULL,
    code integer,
    reason_text text NOT NULL,
    reason_truncated boolean NOT NULL,
    PRIMARY KEY (recording_id, event_key),
    FOREIGN KEY (recording_id, event_key)
        REFERENCES events (recording_id, event_key),
    FOREIGN KEY (recording_id, context_key)
        REFERENCES browser_contexts (recording_id, identity_key),
    FOREIGN KEY (recording_id, scope_key)
        REFERENCES browser_network_scopes (recording_id, identity_key),
    FOREIGN KEY (recording_id, location_key)
        REFERENCES script_locations (recording_id, identity_key),
    FOREIGN KEY (recording_id, world_key)
        REFERENCES execution_worlds (recording_id, identity_key)
) PARTITION BY LIST (recording_id);

CREATE TABLE browser_websocket_errors (
    recording_id uuid NOT NULL,
    event_key bigint NOT NULL,
    context_key bigint NOT NULL,
    scope_key bigint NOT NULL,
    inspector_id text NOT NULL,
    message text NOT NULL,
    PRIMARY KEY (recording_id, event_key),
    FOREIGN KEY (recording_id, event_key)
        REFERENCES events (recording_id, event_key),
    FOREIGN KEY (recording_id, context_key)
        REFERENCES browser_contexts (recording_id, identity_key),
    FOREIGN KEY (recording_id, scope_key)
        REFERENCES browser_network_scopes (recording_id, identity_key)
) PARTITION BY LIST (recording_id);

CREATE TABLE browser_websocket_closures (
    recording_id uuid NOT NULL,
    event_key bigint NOT NULL,
    context_key bigint NOT NULL,
    scope_key bigint NOT NULL,
    inspector_id text NOT NULL,
    cause_name_id integer NOT NULL REFERENCES names (name_id),
    was_clean boolean,
    code integer,
    has_reason boolean NOT NULL,
    reason_text text,
    reason_truncated boolean,
    PRIMARY KEY (recording_id, event_key),
    FOREIGN KEY (recording_id, event_key)
        REFERENCES events (recording_id, event_key),
    FOREIGN KEY (recording_id, context_key)
        REFERENCES browser_contexts (recording_id, identity_key),
    FOREIGN KEY (recording_id, scope_key)
        REFERENCES browser_network_scopes (recording_id, identity_key)
) PARTITION BY LIST (recording_id);

CREATE TABLE browser_event_source_messages (
    recording_id uuid NOT NULL,
    event_key bigint NOT NULL,
    context_key bigint NOT NULL,
    scope_key bigint NOT NULL,
    inspector_id text NOT NULL,
    url text NOT NULL,
    event_type text NOT NULL,
    last_event_id_text text NOT NULL,
    last_event_id_truncated boolean NOT NULL,
    data_length double precision NOT NULL,
    data_text text NOT NULL,
    data_truncated boolean NOT NULL,
    PRIMARY KEY (recording_id, event_key),
    FOREIGN KEY (recording_id, event_key)
        REFERENCES events (recording_id, event_key),
    FOREIGN KEY (recording_id, context_key)
        REFERENCES browser_contexts (recording_id, identity_key),
    FOREIGN KEY (recording_id, scope_key)
        REFERENCES browser_network_scopes (recording_id, identity_key)
) PARTITION BY LIST (recording_id);

CREATE TABLE browser_web_transport_creations (
    recording_id uuid NOT NULL,
    event_key bigint NOT NULL,
    context_key bigint NOT NULL,
    scope_key bigint NOT NULL,
    location_key bigint,
    world_key bigint,
    transport_id text NOT NULL,
    url text NOT NULL,
    PRIMARY KEY (recording_id, event_key),
    FOREIGN KEY (recording_id, event_key)
        REFERENCES events (recording_id, event_key),
    FOREIGN KEY (recording_id, context_key)
        REFERENCES browser_contexts (recording_id, identity_key),
    FOREIGN KEY (recording_id, scope_key)
        REFERENCES browser_network_scopes (recording_id, identity_key),
    FOREIGN KEY (recording_id, location_key)
        REFERENCES script_locations (recording_id, identity_key),
    FOREIGN KEY (recording_id, world_key)
        REFERENCES execution_worlds (recording_id, identity_key)
) PARTITION BY LIST (recording_id);

CREATE TABLE browser_web_transport_establishments (
    recording_id uuid NOT NULL,
    event_key bigint NOT NULL,
    context_key bigint NOT NULL,
    scope_key bigint NOT NULL,
    transport_id text NOT NULL,
    url text,
    http_version_name_id integer REFERENCES names (name_id),
    status integer NOT NULL,
    status_text text,
    has_remote_address boolean NOT NULL,
    remote_address_ip text,
    remote_address_port integer,
    selected_protocol text,
    header_count integer NOT NULL,
    headers_truncated boolean NOT NULL,
    max_datagram_size double precision,
    PRIMARY KEY (recording_id, event_key),
    FOREIGN KEY (recording_id, event_key)
        REFERENCES events (recording_id, event_key),
    FOREIGN KEY (recording_id, context_key)
        REFERENCES browser_contexts (recording_id, identity_key),
    FOREIGN KEY (recording_id, scope_key)
        REFERENCES browser_network_scopes (recording_id, identity_key)
) PARTITION BY LIST (recording_id);

CREATE TABLE browser_web_transport_close_requests (
    recording_id uuid NOT NULL,
    event_key bigint NOT NULL,
    context_key bigint NOT NULL,
    scope_key bigint NOT NULL,
    location_key bigint,
    world_key bigint,
    transport_id text NOT NULL,
    code double precision,
    has_reason boolean NOT NULL,
    reason_text text,
    reason_truncated boolean,
    PRIMARY KEY (recording_id, event_key),
    FOREIGN KEY (recording_id, event_key)
        REFERENCES events (recording_id, event_key),
    FOREIGN KEY (recording_id, context_key)
        REFERENCES browser_contexts (recording_id, identity_key),
    FOREIGN KEY (recording_id, scope_key)
        REFERENCES browser_network_scopes (recording_id, identity_key),
    FOREIGN KEY (recording_id, location_key)
        REFERENCES script_locations (recording_id, identity_key),
    FOREIGN KEY (recording_id, world_key)
        REFERENCES execution_worlds (recording_id, identity_key)
) PARTITION BY LIST (recording_id);

CREATE TABLE browser_web_transport_closures (
    recording_id uuid NOT NULL,
    event_key bigint NOT NULL,
    context_key bigint NOT NULL,
    scope_key bigint NOT NULL,
    transport_id text NOT NULL,
    abrupt boolean NOT NULL,
    code double precision,
    has_reason boolean NOT NULL,
    reason_text text,
    reason_truncated boolean,
    PRIMARY KEY (recording_id, event_key),
    FOREIGN KEY (recording_id, event_key)
        REFERENCES events (recording_id, event_key),
    FOREIGN KEY (recording_id, context_key)
        REFERENCES browser_contexts (recording_id, identity_key),
    FOREIGN KEY (recording_id, scope_key)
        REFERENCES browser_network_scopes (recording_id, identity_key)
) PARTITION BY LIST (recording_id);

CREATE TABLE browser_document_cookie_read_names (
    recording_id uuid NOT NULL,
    owner_key bigint NOT NULL,
    ordinal_1 integer NOT NULL,
    value text NOT NULL,
    PRIMARY KEY (recording_id, owner_key, ordinal_1),
    FOREIGN KEY (recording_id, owner_key)
        REFERENCES browser_document_cookie_reads (recording_id, event_key)
) PARTITION BY LIST (recording_id);

CREATE TABLE browser_document_cookie_write_attribute_names (
    recording_id uuid NOT NULL,
    owner_key bigint NOT NULL,
    ordinal_1 integer NOT NULL,
    value_name_id integer NOT NULL REFERENCES names (name_id),
    PRIMARY KEY (recording_id, owner_key, ordinal_1),
    FOREIGN KEY (recording_id, owner_key)
        REFERENCES browser_document_cookie_writes (recording_id, event_key)
) PARTITION BY LIST (recording_id);

CREATE TABLE browser_cookie_store_result_names (
    recording_id uuid NOT NULL,
    owner_key bigint NOT NULL,
    ordinal_1 integer NOT NULL,
    value text NOT NULL,
    PRIMARY KEY (recording_id, owner_key, ordinal_1),
    FOREIGN KEY (recording_id, owner_key)
        REFERENCES browser_cookie_store_results (recording_id, event_key)
) PARTITION BY LIST (recording_id);

CREATE TABLE browser_cookie_access_cookies (
    recording_id uuid NOT NULL,
    owner_key bigint NOT NULL,
    ordinal_1 integer NOT NULL,
    name text NOT NULL,
    parsed boolean NOT NULL,
    domain text,
    path text,
    same_site_name_id integer REFERENCES names (name_id),
    secure boolean,
    http_only boolean,
    host_only boolean,
    partitioned boolean,
    persistent boolean,
    expired boolean,
    included boolean NOT NULL,
    exemption_reason_name_id integer REFERENCES names (name_id),
    PRIMARY KEY (recording_id, owner_key, ordinal_1),
    FOREIGN KEY (recording_id, owner_key)
        REFERENCES browser_cookie_accesses (recording_id, event_key)
) PARTITION BY LIST (recording_id);

CREATE TABLE browser_cookie_access_cookies_exclusion_reasons (
    recording_id uuid NOT NULL,
    owner_key bigint NOT NULL,
    ordinal_1 integer NOT NULL,
    ordinal_2 integer NOT NULL,
    value_name_id integer NOT NULL REFERENCES names (name_id),
    PRIMARY KEY (recording_id, owner_key, ordinal_1, ordinal_2),
    FOREIGN KEY (recording_id, owner_key, ordinal_1)
        REFERENCES browser_cookie_access_cookies (recording_id, owner_key, ordinal_1)
) PARTITION BY LIST (recording_id);

CREATE TABLE browser_cookie_access_cookies_warning_reasons (
    recording_id uuid NOT NULL,
    owner_key bigint NOT NULL,
    ordinal_1 integer NOT NULL,
    ordinal_2 integer NOT NULL,
    value_name_id integer NOT NULL REFERENCES names (name_id),
    PRIMARY KEY (recording_id, owner_key, ordinal_1, ordinal_2),
    FOREIGN KEY (recording_id, owner_key, ordinal_1)
        REFERENCES browser_cookie_access_cookies (recording_id, owner_key, ordinal_1)
) PARTITION BY LIST (recording_id);

CREATE TABLE browser_network_request_headers (
    recording_id uuid NOT NULL,
    owner_key bigint NOT NULL,
    ordinal_1 integer NOT NULL,
    name_name_id integer NOT NULL REFERENCES names (name_id),
    value text,
    value_redacted boolean NOT NULL,
    redaction_reason_name_id integer REFERENCES names (name_id),
    PRIMARY KEY (recording_id, owner_key, ordinal_1),
    FOREIGN KEY (recording_id, owner_key)
        REFERENCES browser_network_requests (recording_id, event_key)
) PARTITION BY LIST (recording_id);

CREATE TABLE browser_network_request_redirect_headers (
    recording_id uuid NOT NULL,
    owner_key bigint NOT NULL,
    ordinal_1 integer NOT NULL,
    name_name_id integer NOT NULL REFERENCES names (name_id),
    value text,
    value_redacted boolean NOT NULL,
    redaction_reason_name_id integer REFERENCES names (name_id),
    PRIMARY KEY (recording_id, owner_key, ordinal_1),
    FOREIGN KEY (recording_id, owner_key)
        REFERENCES browser_network_requests (recording_id, event_key)
) PARTITION BY LIST (recording_id);

CREATE TABLE browser_network_response_headers (
    recording_id uuid NOT NULL,
    owner_key bigint NOT NULL,
    ordinal_1 integer NOT NULL,
    name_name_id integer NOT NULL REFERENCES names (name_id),
    value text,
    value_redacted boolean NOT NULL,
    redaction_reason_name_id integer REFERENCES names (name_id),
    PRIMARY KEY (recording_id, owner_key, ordinal_1),
    FOREIGN KEY (recording_id, owner_key)
        REFERENCES browser_network_responses (recording_id, event_key)
) PARTITION BY LIST (recording_id);

CREATE TABLE browser_network_memory_cache_hit_request_headers (
    recording_id uuid NOT NULL,
    owner_key bigint NOT NULL,
    ordinal_1 integer NOT NULL,
    name_name_id integer NOT NULL REFERENCES names (name_id),
    value text,
    value_redacted boolean NOT NULL,
    redaction_reason_name_id integer REFERENCES names (name_id),
    PRIMARY KEY (recording_id, owner_key, ordinal_1),
    FOREIGN KEY (recording_id, owner_key)
        REFERENCES browser_network_memory_cache_hits (recording_id, event_key)
) PARTITION BY LIST (recording_id);

CREATE TABLE browser_network_memory_cache_hit_response_headers (
    recording_id uuid NOT NULL,
    owner_key bigint NOT NULL,
    ordinal_1 integer NOT NULL,
    name_name_id integer NOT NULL REFERENCES names (name_id),
    value text,
    value_redacted boolean NOT NULL,
    redaction_reason_name_id integer REFERENCES names (name_id),
    PRIMARY KEY (recording_id, owner_key, ordinal_1),
    FOREIGN KEY (recording_id, owner_key)
        REFERENCES browser_network_memory_cache_hits (recording_id, event_key)
) PARTITION BY LIST (recording_id);

CREATE TABLE browser_network_request_headers_sent_headers (
    recording_id uuid NOT NULL,
    owner_key bigint NOT NULL,
    ordinal_1 integer NOT NULL,
    name_name_id integer NOT NULL REFERENCES names (name_id),
    value text,
    value_redacted boolean NOT NULL,
    redaction_reason_name_id integer REFERENCES names (name_id),
    PRIMARY KEY (recording_id, owner_key, ordinal_1),
    FOREIGN KEY (recording_id, owner_key)
        REFERENCES browser_network_request_headers_sent (recording_id, event_key)
) PARTITION BY LIST (recording_id);

CREATE TABLE browser_network_sent_request_cookies (
    recording_id uuid NOT NULL,
    owner_key bigint NOT NULL,
    ordinal_1 integer NOT NULL,
    name text NOT NULL,
    parsed boolean NOT NULL,
    domain text,
    path text,
    same_site_name_id integer REFERENCES names (name_id),
    secure boolean,
    http_only boolean,
    host_only boolean,
    partitioned boolean,
    persistent boolean,
    expired boolean,
    included boolean NOT NULL,
    exemption_reason_name_id integer REFERENCES names (name_id),
    PRIMARY KEY (recording_id, owner_key, ordinal_1),
    FOREIGN KEY (recording_id, owner_key)
        REFERENCES browser_network_request_headers_sent (recording_id, event_key)
) PARTITION BY LIST (recording_id);

CREATE TABLE browser_network_sent_request_cookies_exclusion_reasons (
    recording_id uuid NOT NULL,
    owner_key bigint NOT NULL,
    ordinal_1 integer NOT NULL,
    ordinal_2 integer NOT NULL,
    value_name_id integer NOT NULL REFERENCES names (name_id),
    PRIMARY KEY (recording_id, owner_key, ordinal_1, ordinal_2),
    FOREIGN KEY (recording_id, owner_key, ordinal_1)
        REFERENCES browser_network_sent_request_cookies (recording_id, owner_key, ordinal_1)
) PARTITION BY LIST (recording_id);

CREATE TABLE browser_network_sent_request_cookies_warning_reasons (
    recording_id uuid NOT NULL,
    owner_key bigint NOT NULL,
    ordinal_1 integer NOT NULL,
    ordinal_2 integer NOT NULL,
    value_name_id integer NOT NULL REFERENCES names (name_id),
    PRIMARY KEY (recording_id, owner_key, ordinal_1, ordinal_2),
    FOREIGN KEY (recording_id, owner_key, ordinal_1)
        REFERENCES browser_network_sent_request_cookies (recording_id, owner_key, ordinal_1)
) PARTITION BY LIST (recording_id);

CREATE TABLE browser_network_response_headers_received_headers (
    recording_id uuid NOT NULL,
    owner_key bigint NOT NULL,
    ordinal_1 integer NOT NULL,
    name_name_id integer NOT NULL REFERENCES names (name_id),
    value text,
    value_redacted boolean NOT NULL,
    redaction_reason_name_id integer REFERENCES names (name_id),
    PRIMARY KEY (recording_id, owner_key, ordinal_1),
    FOREIGN KEY (recording_id, owner_key)
        REFERENCES browser_network_response_headers_received (recording_id, event_key)
) PARTITION BY LIST (recording_id);

CREATE TABLE browser_network_received_response_cookies (
    recording_id uuid NOT NULL,
    owner_key bigint NOT NULL,
    ordinal_1 integer NOT NULL,
    name text NOT NULL,
    parsed boolean NOT NULL,
    domain text,
    path text,
    same_site_name_id integer REFERENCES names (name_id),
    secure boolean,
    http_only boolean,
    host_only boolean,
    partitioned boolean,
    persistent boolean,
    expired boolean,
    included boolean NOT NULL,
    exemption_reason_name_id integer REFERENCES names (name_id),
    PRIMARY KEY (recording_id, owner_key, ordinal_1),
    FOREIGN KEY (recording_id, owner_key)
        REFERENCES browser_network_response_headers_received (recording_id, event_key)
) PARTITION BY LIST (recording_id);

CREATE TABLE browser_network_received_response_cookies_exclusion_reasons (
    recording_id uuid NOT NULL,
    owner_key bigint NOT NULL,
    ordinal_1 integer NOT NULL,
    ordinal_2 integer NOT NULL,
    value_name_id integer NOT NULL REFERENCES names (name_id),
    PRIMARY KEY (recording_id, owner_key, ordinal_1, ordinal_2),
    FOREIGN KEY (recording_id, owner_key, ordinal_1)
        REFERENCES browser_network_received_response_cookies (recording_id, owner_key, ordinal_1)
) PARTITION BY LIST (recording_id);

CREATE TABLE browser_network_received_response_cookies_warning_reasons (
    recording_id uuid NOT NULL,
    owner_key bigint NOT NULL,
    ordinal_1 integer NOT NULL,
    ordinal_2 integer NOT NULL,
    value_name_id integer NOT NULL REFERENCES names (name_id),
    PRIMARY KEY (recording_id, owner_key, ordinal_1, ordinal_2),
    FOREIGN KEY (recording_id, owner_key, ordinal_1)
        REFERENCES browser_network_received_response_cookies (recording_id, owner_key, ordinal_1)
) PARTITION BY LIST (recording_id);

CREATE TABLE browser_network_navigation_redirect_chains (
    recording_id uuid NOT NULL,
    owner_key bigint NOT NULL,
    ordinal_1 integer NOT NULL,
    value text NOT NULL,
    PRIMARY KEY (recording_id, owner_key, ordinal_1),
    FOREIGN KEY (recording_id, owner_key)
        REFERENCES browser_network_navigation_responses (recording_id, event_key)
) PARTITION BY LIST (recording_id);

CREATE TABLE browser_network_navigation_request_headers (
    recording_id uuid NOT NULL,
    owner_key bigint NOT NULL,
    ordinal_1 integer NOT NULL,
    name_name_id integer NOT NULL REFERENCES names (name_id),
    value text,
    value_redacted boolean NOT NULL,
    redaction_reason_name_id integer REFERENCES names (name_id),
    PRIMARY KEY (recording_id, owner_key, ordinal_1),
    FOREIGN KEY (recording_id, owner_key)
        REFERENCES browser_network_navigation_responses (recording_id, event_key)
) PARTITION BY LIST (recording_id);

CREATE TABLE browser_network_navigation_response_headers (
    recording_id uuid NOT NULL,
    owner_key bigint NOT NULL,
    ordinal_1 integer NOT NULL,
    name_name_id integer NOT NULL REFERENCES names (name_id),
    value text,
    value_redacted boolean NOT NULL,
    redaction_reason_name_id integer REFERENCES names (name_id),
    PRIMARY KEY (recording_id, owner_key, ordinal_1),
    FOREIGN KEY (recording_id, owner_key)
        REFERENCES browser_network_navigation_responses (recording_id, event_key)
) PARTITION BY LIST (recording_id);

CREATE TABLE browser_websocket_handshake_request_cookie_names (
    recording_id uuid NOT NULL,
    owner_key bigint NOT NULL,
    ordinal_1 integer NOT NULL,
    value text NOT NULL,
    PRIMARY KEY (recording_id, owner_key, ordinal_1),
    FOREIGN KEY (recording_id, owner_key)
        REFERENCES browser_websocket_handshake_requests (recording_id, event_key)
) PARTITION BY LIST (recording_id);

CREATE TABLE browser_websocket_handshake_request_headers (
    recording_id uuid NOT NULL,
    owner_key bigint NOT NULL,
    ordinal_1 integer NOT NULL,
    name_name_id integer NOT NULL REFERENCES names (name_id),
    value text,
    value_redacted boolean NOT NULL,
    redaction_reason_name_id integer REFERENCES names (name_id),
    PRIMARY KEY (recording_id, owner_key, ordinal_1),
    FOREIGN KEY (recording_id, owner_key)
        REFERENCES browser_websocket_handshake_requests (recording_id, event_key)
) PARTITION BY LIST (recording_id);

CREATE TABLE browser_websocket_handshake_response_set_cookie_names (
    recording_id uuid NOT NULL,
    owner_key bigint NOT NULL,
    ordinal_1 integer NOT NULL,
    value text NOT NULL,
    PRIMARY KEY (recording_id, owner_key, ordinal_1),
    FOREIGN KEY (recording_id, owner_key)
        REFERENCES browser_websocket_handshake_responses (recording_id, event_key)
) PARTITION BY LIST (recording_id);

CREATE TABLE browser_websocket_handshake_response_headers (
    recording_id uuid NOT NULL,
    owner_key bigint NOT NULL,
    ordinal_1 integer NOT NULL,
    name_name_id integer NOT NULL REFERENCES names (name_id),
    value text,
    value_redacted boolean NOT NULL,
    redaction_reason_name_id integer REFERENCES names (name_id),
    PRIMARY KEY (recording_id, owner_key, ordinal_1),
    FOREIGN KEY (recording_id, owner_key)
        REFERENCES browser_websocket_handshake_responses (recording_id, event_key)
) PARTITION BY LIST (recording_id);

CREATE TABLE browser_websocket_message_sent_withheld (
    recording_id uuid NOT NULL,
    owner_key bigint NOT NULL,
    ordinal_1 integer NOT NULL,
    text_offset integer NOT NULL,
    reason_name_id integer NOT NULL REFERENCES names (name_id),
    PRIMARY KEY (recording_id, owner_key, ordinal_1),
    FOREIGN KEY (recording_id, owner_key)
        REFERENCES browser_websocket_messages_sent (recording_id, event_key)
) PARTITION BY LIST (recording_id);

CREATE TABLE browser_websocket_message_received_withheld (
    recording_id uuid NOT NULL,
    owner_key bigint NOT NULL,
    ordinal_1 integer NOT NULL,
    text_offset integer NOT NULL,
    reason_name_id integer NOT NULL REFERENCES names (name_id),
    PRIMARY KEY (recording_id, owner_key, ordinal_1),
    FOREIGN KEY (recording_id, owner_key)
        REFERENCES browser_websocket_messages_received (recording_id, event_key)
) PARTITION BY LIST (recording_id);

CREATE TABLE browser_websocket_close_request_withheld (
    recording_id uuid NOT NULL,
    owner_key bigint NOT NULL,
    ordinal_1 integer NOT NULL,
    text_offset integer NOT NULL,
    reason_name_id integer NOT NULL REFERENCES names (name_id),
    PRIMARY KEY (recording_id, owner_key, ordinal_1),
    FOREIGN KEY (recording_id, owner_key)
        REFERENCES browser_websocket_close_requests (recording_id, event_key)
) PARTITION BY LIST (recording_id);

CREATE TABLE browser_websocket_closure_withheld (
    recording_id uuid NOT NULL,
    owner_key bigint NOT NULL,
    ordinal_1 integer NOT NULL,
    text_offset integer NOT NULL,
    reason_name_id integer NOT NULL REFERENCES names (name_id),
    PRIMARY KEY (recording_id, owner_key, ordinal_1),
    FOREIGN KEY (recording_id, owner_key)
        REFERENCES browser_websocket_closures (recording_id, event_key)
) PARTITION BY LIST (recording_id);

CREATE TABLE browser_event_source_last_event_id_withheld (
    recording_id uuid NOT NULL,
    owner_key bigint NOT NULL,
    ordinal_1 integer NOT NULL,
    text_offset integer NOT NULL,
    reason_name_id integer NOT NULL REFERENCES names (name_id),
    PRIMARY KEY (recording_id, owner_key, ordinal_1),
    FOREIGN KEY (recording_id, owner_key)
        REFERENCES browser_event_source_messages (recording_id, event_key)
) PARTITION BY LIST (recording_id);

CREATE TABLE browser_event_source_data_withheld (
    recording_id uuid NOT NULL,
    owner_key bigint NOT NULL,
    ordinal_1 integer NOT NULL,
    text_offset integer NOT NULL,
    reason_name_id integer NOT NULL REFERENCES names (name_id),
    PRIMARY KEY (recording_id, owner_key, ordinal_1),
    FOREIGN KEY (recording_id, owner_key)
        REFERENCES browser_event_source_messages (recording_id, event_key)
) PARTITION BY LIST (recording_id);

CREATE TABLE browser_web_transport_establishment_set_cookie_names (
    recording_id uuid NOT NULL,
    owner_key bigint NOT NULL,
    ordinal_1 integer NOT NULL,
    value text NOT NULL,
    PRIMARY KEY (recording_id, owner_key, ordinal_1),
    FOREIGN KEY (recording_id, owner_key)
        REFERENCES browser_web_transport_establishments (recording_id, event_key)
) PARTITION BY LIST (recording_id);

CREATE TABLE browser_web_transport_establishment_headers (
    recording_id uuid NOT NULL,
    owner_key bigint NOT NULL,
    ordinal_1 integer NOT NULL,
    name_name_id integer NOT NULL REFERENCES names (name_id),
    value text,
    value_redacted boolean NOT NULL,
    redaction_reason_name_id integer REFERENCES names (name_id),
    PRIMARY KEY (recording_id, owner_key, ordinal_1),
    FOREIGN KEY (recording_id, owner_key)
        REFERENCES browser_web_transport_establishments (recording_id, event_key)
) PARTITION BY LIST (recording_id);

CREATE TABLE browser_web_transport_close_request_withheld (
    recording_id uuid NOT NULL,
    owner_key bigint NOT NULL,
    ordinal_1 integer NOT NULL,
    text_offset integer NOT NULL,
    reason_name_id integer NOT NULL REFERENCES names (name_id),
    PRIMARY KEY (recording_id, owner_key, ordinal_1),
    FOREIGN KEY (recording_id, owner_key)
        REFERENCES browser_web_transport_close_requests (recording_id, event_key)
) PARTITION BY LIST (recording_id);

CREATE TABLE browser_web_transport_closure_withheld (
    recording_id uuid NOT NULL,
    owner_key bigint NOT NULL,
    ordinal_1 integer NOT NULL,
    text_offset integer NOT NULL,
    reason_name_id integer NOT NULL REFERENCES names (name_id),
    PRIMARY KEY (recording_id, owner_key, ordinal_1),
    FOREIGN KEY (recording_id, owner_key)
        REFERENCES browser_web_transport_closures (recording_id, event_key)
) PARTITION BY LIST (recording_id);

INSERT INTO recording_partitioned_tables (table_name, partition_prefix, partition_order) VALUES
    ('browser_network_scopes', 'pt72', 72),
    ('browser_document_cookie_reads', 'pt73', 73),
    ('browser_document_cookie_writes', 'pt74', 74),
    ('browser_cookie_store_requests', 'pt75', 75),
    ('browser_cookie_store_results', 'pt76', 76),
    ('browser_cookie_store_changes', 'pt77', 77),
    ('browser_cookie_accesses', 'pt78', 78),
    ('browser_network_requests', 'pt79', 79),
    ('browser_network_responses', 'pt80', 80),
    ('browser_network_request_finishes', 'pt81', 81),
    ('browser_network_request_failures', 'pt82', 82),
    ('browser_network_memory_cache_hits', 'pt83', 83),
    ('browser_network_request_headers_sent', 'pt84', 84),
    ('browser_network_response_headers_received', 'pt85', 85),
    ('browser_network_navigation_responses', 'pt86', 86),
    ('browser_websocket_creations', 'pt87', 87),
    ('browser_websocket_handshake_requests', 'pt88', 88),
    ('browser_websocket_handshake_responses', 'pt89', 89),
    ('browser_websocket_messages_sent', 'pt90', 90),
    ('browser_websocket_messages_received', 'pt91', 91),
    ('browser_websocket_close_requests', 'pt92', 92),
    ('browser_websocket_errors', 'pt93', 93),
    ('browser_websocket_closures', 'pt94', 94),
    ('browser_event_source_messages', 'pt95', 95),
    ('browser_web_transport_creations', 'pt96', 96),
    ('browser_web_transport_establishments', 'pt97', 97),
    ('browser_web_transport_close_requests', 'pt98', 98),
    ('browser_web_transport_closures', 'pt99', 99),
    ('browser_document_cookie_read_names', 'pt100', 100),
    ('browser_document_cookie_write_attribute_names', 'pt101', 101),
    ('browser_cookie_store_result_names', 'pt102', 102),
    ('browser_cookie_access_cookies', 'pt103', 103),
    ('browser_cookie_access_cookies_exclusion_reasons', 'pt104', 104),
    ('browser_cookie_access_cookies_warning_reasons', 'pt105', 105),
    ('browser_network_request_headers', 'pt106', 106),
    ('browser_network_request_redirect_headers', 'pt107', 107),
    ('browser_network_response_headers', 'pt108', 108),
    ('browser_network_memory_cache_hit_request_headers', 'pt109', 109),
    ('browser_network_memory_cache_hit_response_headers', 'pt110', 110),
    ('browser_network_request_headers_sent_headers', 'pt111', 111),
    ('browser_network_sent_request_cookies', 'pt112', 112),
    ('browser_network_sent_request_cookies_exclusion_reasons', 'pt113', 113),
    ('browser_network_sent_request_cookies_warning_reasons', 'pt114', 114),
    ('browser_network_response_headers_received_headers', 'pt115', 115),
    ('browser_network_received_response_cookies', 'pt116', 116),
    ('browser_network_received_response_cookies_exclusion_reasons', 'pt117', 117),
    ('browser_network_received_response_cookies_warning_reasons', 'pt118', 118),
    ('browser_network_navigation_redirect_chains', 'pt119', 119),
    ('browser_network_navigation_request_headers', 'pt120', 120),
    ('browser_network_navigation_response_headers', 'pt121', 121),
    ('browser_websocket_handshake_request_cookie_names', 'pt122', 122),
    ('browser_websocket_handshake_request_headers', 'pt123', 123),
    ('browser_websocket_handshake_response_set_cookie_names', 'pt124', 124),
    ('browser_websocket_handshake_response_headers', 'pt125', 125),
    ('browser_websocket_message_sent_withheld', 'pt126', 126),
    ('browser_websocket_message_received_withheld', 'pt127', 127),
    ('browser_websocket_close_request_withheld', 'pt128', 128),
    ('browser_websocket_closure_withheld', 'pt129', 129),
    ('browser_event_source_last_event_id_withheld', 'pt130', 130),
    ('browser_event_source_data_withheld', 'pt131', 131),
    ('browser_web_transport_establishment_set_cookie_names', 'pt132', 132),
    ('browser_web_transport_establishment_headers', 'pt133', 133),
    ('browser_web_transport_close_request_withheld', 'pt134', 134),
    ('browser_web_transport_closure_withheld', 'pt135', 135);
