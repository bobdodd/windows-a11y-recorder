-- Core schema: projects, recordings, collectors, closed vocabularies, and the
-- event envelope. Evidence payload tables are added per evidence model by
-- later migrations.

CREATE TABLE projects (
    project_id uuid PRIMARY KEY DEFAULT uuidv7(),
    name text NOT NULL UNIQUE CHECK (length(name) > 0),
    created_utc timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE recording_statuses (
    recording_status_id smallint PRIMARY KEY,
    name text NOT NULL UNIQUE
);

INSERT INTO recording_statuses (recording_status_id, name) VALUES
    (1, 'recording'),
    (2, 'completed'),
    (3, 'failed'),
    (4, 'interrupted');

CREATE TABLE recordings (
    recording_id uuid PRIMARY KEY DEFAULT uuidv7(),
    project_id uuid NOT NULL REFERENCES projects (project_id),
    session_key text NOT NULL UNIQUE,
    recording_status_id smallint NOT NULL
        REFERENCES recording_statuses (recording_status_id),
    started_utc timestamptz NOT NULL,
    ended_utc timestamptz,
    duration_nanoseconds bigint CHECK (duration_nanoseconds >= 0),
    clock_frequency bigint NOT NULL CHECK (clock_frequency > 0),
    clock_origin_timestamp bigint NOT NULL,
    os_description text NOT NULL,
    framework_description text NOT NULL,
    process_architecture text NOT NULL,
    capture_keyboard_and_mouse boolean NOT NULL,
    capture_ui_automation boolean NOT NULL,
    capture_foreground_window boolean NOT NULL,
    capture_desktop_frames boolean NOT NULL,
    frames_per_second integer NOT NULL,
    capture_microphone boolean NOT NULL,
    capture_system_audio boolean NOT NULL,
    accepted_event_count bigint NOT NULL DEFAULT 0,
    dropped_event_count bigint NOT NULL DEFAULT 0,
    rejected_event_count bigint NOT NULL DEFAULT 0,
    failure text
);

CREATE INDEX recordings_project ON recordings (project_id, started_utc);

-- Closed vocabularies. Each distinct value is stored once and referenced by
-- key. Values are added by the writer as they are first seen.
CREATE TABLE channels (
    channel_id smallint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    name text NOT NULL UNIQUE
);

CREATE TABLE event_types (
    event_type_id integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    name text NOT NULL UNIQUE
);

CREATE TABLE quality_flags (
    quality_flag_id integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    name text NOT NULL UNIQUE
);

CREATE TABLE event_schema_versions (
    event_schema_version_id smallint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    name text NOT NULL UNIQUE
);

CREATE TABLE evidence_classes (
    evidence_class_id smallint PRIMARY KEY,
    name text NOT NULL UNIQUE
);

INSERT INTO evidence_classes (evidence_class_id, name) VALUES
    (1, 'observed'),
    (2, 'derived'),
    (3, 'inferred'),
    (4, 'unknown');

CREATE TABLE timestamp_domains (
    timestamp_domain_id smallint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    name text NOT NULL UNIQUE
);

CREATE TABLE timestamp_units (
    timestamp_unit_id smallint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    name text NOT NULL UNIQUE
);

-- A collector kind is what an event's envelope states about its producer.
CREATE TABLE collector_kinds (
    collector_kind_id integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    collector_type text NOT NULL,
    producer_version text NOT NULL,
    capture_method text NOT NULL,
    UNIQUE (collector_type, producer_version, capture_method)
);

-- One row per collector instance that wrote to a recording. The coordinator
-- adds what it knows about the implementation, capability, and final state.
CREATE TABLE recording_collectors (
    recording_id uuid NOT NULL REFERENCES recordings (recording_id)
        ON DELETE CASCADE,
    recording_collector_id integer GENERATED ALWAYS AS IDENTITY,
    instance_id text NOT NULL,
    collector_kind_id integer NOT NULL
        REFERENCES collector_kinds (collector_kind_id),
    implementation text,
    contract_version text,
    capability_status text,
    lifecycle_state text,
    health_state text,
    PRIMARY KEY (recording_collector_id),
    UNIQUE (recording_id, instance_id)
);

CREATE TABLE recording_collector_channels (
    recording_collector_id integer NOT NULL
        REFERENCES recording_collectors (recording_collector_id)
        ON DELETE CASCADE,
    channel_id smallint NOT NULL REFERENCES channels (channel_id),
    PRIMARY KEY (recording_collector_id, channel_id)
);

CREATE TABLE recording_collector_limitations (
    recording_collector_id integer NOT NULL
        REFERENCES recording_collectors (recording_collector_id)
        ON DELETE CASCADE,
    ordinal smallint NOT NULL,
    limitation text NOT NULL,
    PRIMARY KEY (recording_collector_id, ordinal)
);

CREATE TABLE clock_mappings (
    clock_mapping_id integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    recording_id uuid NOT NULL REFERENCES recordings (recording_id)
        ON DELETE CASCADE,
    name text NOT NULL,
    UNIQUE (recording_id, name)
);

-- Events the writer refused, with the reason, so a recording states what it
-- is missing. Refused events are not stored.
CREATE TABLE event_rejections (
    recording_id uuid NOT NULL REFERENCES recordings (recording_id)
        ON DELETE CASCADE,
    rejection_ordinal integer NOT NULL,
    reason text NOT NULL,
    channel text,
    event_type text,
    sequence numeric(20, 0),
    PRIMARY KEY (recording_id, rejection_ordinal)
);

-- Runs of events the writer could not buffer while the database was not
-- accepting writes. The events are lost; the run states when and how many.
CREATE TABLE writer_omissions (
    recording_id uuid NOT NULL REFERENCES recordings (recording_id)
        ON DELETE CASCADE,
    omission_ordinal integer NOT NULL,
    first_monotonic_nanoseconds bigint NOT NULL,
    last_monotonic_nanoseconds bigint NOT NULL,
    event_count bigint NOT NULL CHECK (event_count > 0),
    PRIMARY KEY (recording_id, omission_ordinal)
);

-- The event envelope. The event identifier is not stored: it is derived from
-- the recording's session key, the collector instance, the channel, and the
-- sequence, and the writer rejects an event whose identifier differs.
-- observed_utc holds microseconds; observed_utc_tick_remainder holds the
-- remaining 100 ns ticks, so the recorded time is kept exactly.
-- Partitioned by recording, one partition per recording.
CREATE TABLE events (
    recording_id uuid NOT NULL,
    event_key bigint NOT NULL,
    recording_collector_id integer NOT NULL,
    channel_id smallint NOT NULL,
    sequence bigint NOT NULL CHECK (sequence >= 0),
    monotonic_nanoseconds bigint NOT NULL CHECK (monotonic_nanoseconds >= 0),
    event_type_id integer NOT NULL,
    evidence_class_id smallint NOT NULL,
    event_schema_version_id smallint NOT NULL,
    clock_mapping_id integer NOT NULL,
    observed_utc timestamptz NOT NULL,
    observed_utc_tick_remainder smallint NOT NULL
        CHECK (observed_utc_tick_remainder BETWEEN 0 AND 9),
    native_timestamp_domain_id smallint,
    native_timestamp_value bigint,
    native_timestamp_unit_id smallint,
    timestamp_uncertainty_nanoseconds bigint
        CHECK (timestamp_uncertainty_nanoseconds >= 0),
    PRIMARY KEY (recording_id, event_key),
    UNIQUE (recording_id, recording_collector_id, channel_id, sequence),
    FOREIGN KEY (recording_collector_id)
        REFERENCES recording_collectors (recording_collector_id),
    FOREIGN KEY (channel_id) REFERENCES channels (channel_id),
    FOREIGN KEY (event_type_id) REFERENCES event_types (event_type_id),
    FOREIGN KEY (evidence_class_id)
        REFERENCES evidence_classes (evidence_class_id),
    FOREIGN KEY (event_schema_version_id)
        REFERENCES event_schema_versions (event_schema_version_id),
    FOREIGN KEY (clock_mapping_id)
        REFERENCES clock_mappings (clock_mapping_id),
    FOREIGN KEY (native_timestamp_domain_id)
        REFERENCES timestamp_domains (timestamp_domain_id),
    FOREIGN KEY (native_timestamp_unit_id)
        REFERENCES timestamp_units (timestamp_unit_id),
    CHECK ((native_timestamp_domain_id IS NULL) =
           (native_timestamp_value IS NULL) AND
           (native_timestamp_value IS NULL) =
           (native_timestamp_unit_id IS NULL))
) PARTITION BY LIST (recording_id);

CREATE INDEX events_channel_time
    ON events (recording_id, channel_id, monotonic_nanoseconds);
CREATE INDEX events_time ON events (recording_id, monotonic_nanoseconds);

CREATE TABLE event_quality_flags (
    recording_id uuid NOT NULL,
    event_key bigint NOT NULL,
    quality_flag_id integer NOT NULL REFERENCES quality_flags (quality_flag_id),
    PRIMARY KEY (recording_id, event_key, quality_flag_id),
    FOREIGN KEY (recording_id, event_key)
        REFERENCES events (recording_id, event_key)
) PARTITION BY LIST (recording_id);

-- Links from derived or inferred evidence to the evidence it was derived
-- from, by event identifier, because a related event may belong to another
-- recording or may not have been written yet.
CREATE TABLE event_related_evidence (
    recording_id uuid NOT NULL,
    event_key bigint NOT NULL,
    ordinal smallint NOT NULL,
    related_event_id text NOT NULL,
    PRIMARY KEY (recording_id, event_key, ordinal),
    FOREIGN KEY (recording_id, event_key)
        REFERENCES events (recording_id, event_key)
) PARTITION BY LIST (recording_id);

CREATE TABLE event_analysis (
    recording_id uuid NOT NULL,
    event_key bigint NOT NULL,
    analyzer text NOT NULL,
    analyzer_version text NOT NULL,
    method text NOT NULL,
    confidence_category text,
    insufficient_evidence_reason text,
    PRIMARY KEY (recording_id, event_key),
    FOREIGN KEY (recording_id, event_key)
        REFERENCES events (recording_id, event_key)
) PARTITION BY LIST (recording_id);

CREATE TABLE event_analysis_competing_interpretations (
    recording_id uuid NOT NULL,
    event_key bigint NOT NULL,
    ordinal smallint NOT NULL,
    interpretation text NOT NULL,
    PRIMARY KEY (recording_id, event_key, ordinal),
    FOREIGN KEY (recording_id, event_key)
        REFERENCES event_analysis (recording_id, event_key)
) PARTITION BY LIST (recording_id);

-- Transitional. Holds the payload of every event type that does not yet have
-- evidence tables. Each evidence model's migration moves its event types out
-- of this table. The table must be empty of recorder event types, and is
-- dropped, before this branch is merged.
CREATE TABLE event_payloads_unmapped (
    recording_id uuid NOT NULL,
    event_key bigint NOT NULL,
    payload jsonb NOT NULL,
    PRIMARY KEY (recording_id, event_key),
    FOREIGN KEY (recording_id, event_key)
        REFERENCES events (recording_id, event_key)
) PARTITION BY LIST (recording_id);

-- The tables that have one partition per recording. The recording store
-- creates a partition of each when it creates a recording, named by the
-- table's short prefix and the recording key, which keeps the name within
-- PostgreSQL's 63-byte identifier limit. partition_order puts a referenced
-- table before the tables that refer to it.
CREATE TABLE recording_partitioned_tables (
    table_name text PRIMARY KEY,
    partition_prefix text NOT NULL UNIQUE,
    partition_order smallint NOT NULL UNIQUE
);

INSERT INTO recording_partitioned_tables (table_name, partition_prefix, partition_order) VALUES
    ('events', 'ev', 1),
    ('event_quality_flags', 'evqf', 2),
    ('event_related_evidence', 'evre', 3),
    ('event_analysis', 'evan', 4),
    ('event_analysis_competing_interpretations', 'evci', 5),
    ('event_payloads_unmapped', 'evpu', 6);
