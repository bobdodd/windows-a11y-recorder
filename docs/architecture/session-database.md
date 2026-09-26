# Session Database

Status: decided, not implemented. This record states the decision and the
design it implies. It replaces the file-based event log and the checks built
around it once implemented.

## Context

The recorder writes every event to one append-only file, `events.ndjson`, per
recording. Stopping a recording validates the archive by parsing every record,
and opening a recording parses every record again to build the player's
timeline. Both costs grow with the length of the recording.

Measured on the maintainer's PC (September 25, 2026):

- One recording of 44.6 s, with instrumented Chromium, produced an event log
  of 487.8 MB with 160,640 events, and 547 MB of artifacts in total. At that
  rate an hour produces about 39 GB of event log and about 13 million events.
  This is an extrapolation from one short recording, not a measurement of an
  hour.
- Another produced a 920 MB log, of which layout checkpoint node records were
  694 MB (216,255 records, about 3.2 KB each).
- On a 1.35 GB recording, the archive check at stop took about 11.7 s, about
  26 microseconds per record.

The app is used by one person, on several projects, each typically holding 30
or more recordings, many of them an hour or longer. At the rate above, one
such project is on the order of a terabyte of event text. A single linear file that must be parsed
before the player can respond does not scale to that use.

## Decision

1. **PostgreSQL is part of the application.** The app installs and owns its
   own PostgreSQL server and data directory, starts the server when the app
   starts, and stops it when the app exits. The user never installs,
   configures, or connects to it directly. SQLite is not used.
2. **The app connects over a loopback TCP port.** This keeps the connection
   the same as it would be to an external server, so an external database can
   be supported later without changing the data access code.
3. **The schema is normalized.** Evidence is stored in typed columns, with
   entities that recur (such as channels, event types, collectors, windows,
   UI Automation elements, documents, frames, nodes, and URLs) held once in
   their own tables and referenced by key. `jsonb` is not used for fields an
   evidence model defines.
4. **PostgreSQL 18 is the shipped version.** See [Version](#version).
5. **Events go straight into the database.** There is no event log file. A
   recording's events are rows, written while the recording runs.
6. **Writes are buffered.** If the database does not accept writes for a time,
   events wait in a buffer and are written when it does. Because the app is
   the only client of its own server, this is expected to be rare.
7. **Old recordings are not migrated.** File-based sessions made before this
   change are not imported and the new player does not open them.

The integrity of a recording rests on the database: constraints, transactions,
and write-ahead logging, rather than on per-file hashes and a manifest. The
recorder is a testing tool; its records are not prepared as evidence for legal
proceedings.

## Rationale

- **Responsiveness.** The player asks the database for what it needs (events
  near the playhead, a filtered list, one record for the inspector, counts per
  lane for the timeline overview) through indexed queries. Opening and seeking
  do not depend on the recording's length.
- **Stop is short.** Stopping flushes the last batch and marks the recording
  complete. Nothing rereads or reparses the recording.
- **Structure and size.** Projects, recordings, collectors, and later findings
  are related records, and the evidence models in this folder already define
  each channel's fields. Storing those fields in typed columns, and recurring
  entities once, keeps repeated names, identifiers, and URLs out of every
  event, which matters at the volumes above. How much smaller the normalized
  store is than the event text is not yet measured.
- **Bulk writing.** PostgreSQL's `COPY` "incurs significantly less overhead
  for large data loads" than a series of `INSERT` commands
  ([Populating a Database](https://www.postgresql.org/docs/current/populate.html)),
  and Npgsql supports it from .NET
  ([Npgsql COPY](https://www.npgsql.org/doc/copy.html)).
- **Redistribution.** PostgreSQL is released under the PostgreSQL License, a
  liberal open source license similar to BSD or MIT
  ([PostgreSQL License](https://www.postgresql.org/about/licence/?lang=en)). The
  project provides a zip archive of Windows binaries "intended for users who
  wish to include Postgres as part of another application installer"
  ([PostgreSQL Windows downloads](https://www.postgresql.org/download/windows/)).

## Design

### Embedded server

- The data directory is per Windows user, under the user's local application
  data folder. The first start runs `initdb`.
- The server runs as the signed-in user, from the ordinary, non-administrator
  process that runs the app.
- The server listens only on the loopback interface, on a port chosen when the
  app starts it, and accepts no connections from other hosts. Other local
  processes can reach a loopback port, so the app authenticates with a
  password generated at `initdb` and protected for the current Windows user.
- The connection string is the only thing that differs from an external
  server. Supporting an external server later is a configuration change plus
  the operational work that server needs; it is not part of this decision.
- A server that is already running from an earlier app instance that exited
  abnormally is detected and reused or restarted. PostgreSQL's own crash
  recovery handles an unclean shutdown.

### Schema outline

The schema is derived from the evidence models in this folder, one group of
tables per model, and is versioned with migrations the app applies at start.

- **Organisation.** `projects`, `recordings` (status `recording`,
  `completed`, `failed`, or `interrupted`, with configuration and clock
  origin), and `recording_collectors` (collector, capability, limitations,
  lifecycle, and health), which the manifest holds today.
- **Reference tables.** Channels, event types, quality flags, collectors, and
  other closed vocabularies, referenced by small integer keys.
- **Identity tables.** Entities that recur within a recording, such as
  windows, processes, UI Automation elements, browser documents, frames,
  nodes, and URLs, each stored once per recording and referenced by key.
- **Events.** One row per event with recording, channel, per-channel sequence,
  monotonic time in nanoseconds, event type, and event identifier. A unique
  key on recording, channel, and sequence rejects duplicates, and an index on
  recording, channel, and time serves the player. Quality flags are a
  separate event-to-flag table.
- **Evidence tables.** Each event type's fields in typed columns, keyed by
  event, referencing identity tables rather than repeating their values.
  Collections within an event, such as the nodes of a layout checkpoint or
  the monitors of a desktop frame, are child rows.
- **Media.** Frames and audio remain files in a per-recording folder next to
  the data directory. Each has a row with its path, size, time, and the
  SHA-256 already computed while it was written. Large binary data stays out
  of the event tables so that event queries stay fast.
- **Partitioning.** The event and evidence tables are partitioned by
  recording
  ([Table Partitioning](https://www.postgresql.org/docs/current/ddl-partitioning.html)),
  so deleting a recording drops its partitions and a query for one recording
  touches only that recording's data.

Layout checkpoint node records were about three quarters of a measured log.
Whether identical node states in successive checkpoints are stored once and
referenced is decided by measuring storage size and query time on an hour-long
recording.

### Writing during capture

- Collectors keep writing to the bounded in-memory queue they use today
  (`IRecorderEventSink`).
- One database writer takes events from the queue and writes them in batches
  with binary `COPY`, by size or by time, whichever comes first.
- The writer maps each event to its rows, resolving identities to keys it
  caches for the recording. The per-record checks the archive validator makes
  today (required envelope fields, known channel, monotonic time within a
  channel) run in the writer before a record is sent, or as table
  constraints. A record that fails is
  counted and reported on the recording, as the validator reports it today.
- **Buffering.** While the database is not accepting writes, batches are held
  in memory up to a fixed limit, then in a temporary spill file in the
  database's data directory, which the writer drains in order and deletes once the
  database accepts writes again. Buffering is bounded, as the architecture
  requires of every queue. If the bound is reached, events are refused and the
  refusal is recorded as a `collector-omission`, the same way queue loss is
  stated today.
- A recording whose app exits during capture keeps every committed batch and is
  marked `interrupted` at the next start.

### Playback

- Opening a recording reads its metadata and the timeline overview, which is
  counts per lane per time bucket at the width being drawn, not every event.
- Seeking and the event list query a time window. The inspector reads one
  event's payload by identifier.
- The player never holds the whole recording in memory.

### Version

The app ships PostgreSQL 18, at its current minor release (18.6 on
September 25, 2026). PostgreSQL 18 was first released on September 25, 2025,
and is supported until November 14, 2030
([PostgreSQL versioning policy](https://www.postgresql.org/support/versioning/)).
PostgreSQL 19 is at its fourth beta, and beta versions are not advised for
production use ([PostgreSQL](https://www.postgresql.org/)).

PostgreSQL 18 features that bear on this workload
([PostgreSQL 18 release notes](https://www.postgresql.org/docs/current/release-18.html)):

- An asynchronous I/O subsystem that can improve sequential scans and bitmap
  heap scans, which the timeline overview and later analysis over whole
  recordings use.
- Skip scan lookups, which let multicolumn B-tree indexes be used in more
  cases, such as the recording, channel, and time index queried without a
  channel.
- A `uuidv7()` function that generates timestamp-ordered UUIDs, suited to
  project and recording identifiers created in time order.

Because the app owns the server, it owns upgrades. A minor release replaces
the binaries only; a major release needs `pg_upgrade` or a dump and reload
([PostgreSQL versioning policy](https://www.postgresql.org/support/versioning/)),
so an app release that moves to a new major version ships the upgrade step and
runs it on the user's data directory. The move to PostgreSQL 19 is considered
after it is generally available and has had minor releases.

## What this retires

When implemented, the following are removed rather than kept alongside:
`events.ndjson` and `NdjsonEventWriter`, the manifest's artifact inventory and
`ArtifactHashRegistry` for the event log, the finalization archive check,
`SessionArchiveReader`, `NdjsonLineReader`, and `SessionPlaybackArchiveBuilder`.
The capture host uses the same database writer. The privacy and data handling
policy and the threat model describe session files and are revised in the same
change.

## Required tests

- Unit tests for batching, buffering, spill and drain, the bound on buffering,
  and the per-record checks.
- Integration tests against a real PostgreSQL server started by the test,
  covering schema creation, `COPY` writes, recovery after the server stops
  and restarts during a recording, interrupted recordings, and each playback
  query.
- A system test on Windows: a recording of at least one hour with every
  collector enabled, measuring stop time, open time, seek time, memory use,
  and database size, in an ordinary, non-administrator session.

## Implementation status

The implementation is on the `postgres-session-store` branch and is merged
only once the database version is tested in full. It adds the
`Recorder.Database` project and connects the coordinator and app to it.

- **Server.** `EmbeddedPostgresServer` runs `initdb` on first start with
  SCRAM-SHA-256 authentication, UTF-8 encoding, no locale, a generated
  password stored protected for the current Windows user, and
  `listen_addresses` set to `127.0.0.1` with no Unix-domain socket. It
  attaches to a server an earlier instance left running, and otherwise starts
  one with `pg_ctl`.
- **Migrations.** `DatabaseMigrator` applies the embedded SQL migrations in
  order, each in its own transaction, under an advisory lock.
- **Partitions.** `RecordingStore` creates one partition of each event table
  when it creates a recording, named by a short table prefix and the
  recording key, such as `ev_<key>`, because a name built from the full
  table name exceeds PostgreSQL's 63-byte identifier limit and is truncated.
  Deleting a recording detaches and drops its partitions, referring tables
  first, and then deletes its row.
- **Writing.** `PostgresEventWriter` makes the archive validator's per-record
  checks, rejecting a record with the validator's code into
  `event_rejections`. It holds up to 256 MB in memory, then writes to a spill
  file of up to 8 GB, and records events refused beyond that as omission runs
  in `writer_omissions`. A batch the database refuses for a reason other than
  an outage is written event by event, so one bad record refuses only itself.
  If the store cannot be reached within the completion timeout when
  recording stops, the spill file is kept and its path is reported.
- **Evidence tables.** An event's payload is stored in typed tables chosen by
  its channel and event type, generated from the evidence catalog in
  `src/Recorder.Database/Evidence/EvidenceCatalog.cs`. The catalog assigns
  its tables to migrations: `0003_evidence_tables.sql` and
  `0004_browser_script_evidence.sql`, and `0005_browser_document_evidence.sql`
  are generated from it, and a test
  requires each file to match; setting `RECORDER_REGENERATE_EVIDENCE_MIGRATION`
  to `1` while running that test rewrites them. A table is created by the
  first migration whose event types reach it, and partition orders continue
  from one migration to the next, so a later migration adds tables without
  changing one a database has already applied. A recording created before a
  migration has no partitions of the tables that migration adds, so resuming
  it afterwards refuses events of those types. Each payload member is a
  column, except that:
  - strings from small or recurring vocabularies, such as reasons, states,
    process names, and control types, are stored once in `names` and
    referenced by `name_id`;
  - nested objects that recur within a recording are identities, stored once
    per recording and referenced by key: windows, monitors, UI Automation
    elements, browser contexts, browser event targets, script locations,
    execution worlds and scopes, and repeated texts such as an audio buffer's
    file path or a navigation URL. A writer stores each distinct value once; a resumed recording
    has a new writer, which stores its identities again under new keys;
  - other nested objects are flattened into the owning row, with a
    `has_<member>` column when the object may be null;
  - arrays and maps are child tables keyed by their owner and position or
    entry name.
  The first slice covers collector lifecycle, session markers, raw keyboard
  and mouse input, the foreground window, UI Automation events, desktop
  frames, microphone and system audio, browser lifecycle, and the collector
  omissions of those channels. The second slice, migration 0004, covers
  browser listeners, event dispatch with its composed path and path scopes,
  timers, scheduler wake-up deferrals, and navigations, and the collector
  omissions of every browser channel. The third slice, migration 0005, covers
  the browser accessibility, DOM, and interaction channels: accessibility,
  DOM, and interaction checkpoints with their nodes, attributes, shadow
  roots, slot assignments, and text controls, DOM attribute and character
  data changes, and focus, selection, text control value, and active
  descendant changes. A slot's assigned node list keeps its null entries.
- **Stored forms.** The rebuilt payload matches the written payload in
  content, with these normal forms: a member the validator allows to be
  absent, when written as null, reads back absent; a UTC time is stored to
  the tenth of a microsecond and reads back in the form System.Text.Json
  writes, without trailing fractional zeros; a number stored as a double
  reads back in PostgreSQL's shortest form, so a browser's `544.0` reads back
  as `544`; and property order is not kept.
  A payload member the catalog does not hold, a member of the wrong type, and
  text containing a NUL character, which PostgreSQL text cannot store, are
  refused with a reason such as `payload-member-unmapped:payload/colour` in
  `event_rejections`, rather than stored in part.
- **Transitional payload table.** Payloads of event types the catalog does
  not cover yet are stored in `event_payloads_unmapped` as `jsonb`, which the
  player also reads for recordings written before the evidence tables. This
  contradicts the decision against `jsonb` for evidence-model fields, and the
  table is emptied and dropped before the branch is merged.
- **Binaries.** `scripts/Get-PostgresBinaries.ps1` downloads the EDB Windows
  x64 binaries archive for 18.6, checks its SHA-256 against the value computed
  from the archive downloaded on September 25, 2026, and extracts the server
  programs, libraries, shared files, and licences into `.postgres\pgsql`,
  which is not committed. The integration tests use those binaries, or the
  directory named by `RECORDER_POSTGRES_BIN`.

- **Recording.** `SessionDatabase` starts the server, marks recordings an
  earlier run left in the recording state as interrupted, and holds one
  project, named "Default project", until projects are exposed in the app.
  When the coordinator is given one, each recording is created in the
  database with its capture settings when it starts, its collectors are
  registered with their capability, and each event and marker goes to the
  session files and then to the database writer. At stop the collectors'
  final lifecycle and health states are recorded, the writer drains while the
  session files are inventoried and validated, and the recording's status,
  end time, duration, and event counts are stored. A recording whose files
  are complete but whose events were not all written is stored as failed,
  with the unwritten count as its failure reason. The coordinator's status
  reports the writer's accepted, written, rejected, dropped, and unwritten
  counts and any database problem, and the app shows them.
- **Spill location.** A recording's spill file is
  `spill\<session key>.ndjson` in the database's data directory, not the
  session folder, so the session files and their manifest do not change
  while the writer drains it.
- **Transition.** The session files, including `events.ndjson`, are still
  written in full. The player reads a recording from the database when the
  database holds it complete, and from the session files otherwise. The
  files are removed only once that removal is agreed.
- **Playback.** `DatabasePlaybackReader` finds a recording by its session
  key, which is the session folder's name. Only a recording stored as
  completed is opened from the database; a recording that is not found, or
  is stored as recording, failed, or interrupted, is opened from its session
  files, and the app shows the reason with the recording. A database that
  cannot be read is treated the same way. Opening a recording reads only
  the events that frames, audio tracks, and browser navigation are built
  from, with the payload properties the player uses projected in the
  database, and one grouped pass over the recording's events that gives the
  number of events on each channel and which of 262,144 equal time buckets
  hold events of each channel. The player draws the timeline from those
  buckets, so an event can be drawn up to one bucket earlier than its time;
  at the greatest zoom, 32 times, a bucket is no wider than one pixel column
  on a timeline up to 4,096 pixels wide. The timeline's events are not held
  in memory: the event shown at the playhead, the event selected by a click,
  and stepping with the arrow keys, Home, and End are each a query that reads,
  for each shown channel, one entry of the index on recording, channel,
  time, and event key (migration 0002), and returns the best of those.
  Events with the same time are ordered by event key, which follows the
  order they were stored in; a recording read from its session files orders
  them by line, which gives the same order. Queries are asynchronous, so the
  window stays responsive while one runs, and a result that arrives after
  the user has moved on is discarded.
  An event's complete record is read from the database only when it is
  selected in the inspector, and is rebuilt from the envelope columns and
  child tables, and its payload from the evidence tables. The rebuilt record
  matches the event log's record in content but not byte for byte, in the
  stored forms described above: a payload's property order is not kept,
  quality flags are returned in the order their names were first stored, and
  numbers are returned as PostgreSQL normalizes them. `manifest.json` is
  still read from the session folder, and frames and audio are still read
  from their files. A recording opened from the database is not revalidated
  from its session files, because the database stores it as completed only
  when finalization validation passed. On the Linux development sandbox, a
  recording of 100,000 events with payloads of about 2 KB opened from the
  database in about 145 ms, and 200 timeline lookups took about 224 ms in
  total, in one run of a debug build. Before paging, the same recording
  loaded in about 1 s. With a quarter of those events, the UI Automation
  events, read from evidence tables, one run opened the recording in about
  154 ms and took about 304 ms for the 200 lookups. These figures are not measurements on the target
  machine, and a recording of an hour or more has not been measured.
- **App.** The app starts the database when its window loads, with the data
  directory `%LOCALAPPDATA%\Windows A11y Recorder\Database`, and stops it
  when the window closes. Recording is not available until the start
  attempt ends. If the server cannot start, the app says so, and recordings
  are written to session files only. The build copies `.postgres\pgsql`, when
  present, to `pgsql` in the app's output folder.

Still to come on the branch: evidence tables for the browser layout, presentation, cookie, and network
channels, removing what this
retires, including `events.ndjson` once that is agreed, the revised privacy policy and threat model, and
the hour-long Windows system test.

A throughput probe on the Linux development sandbox, with two processor cores
and a debug build, wrote 200,000 events with payloads of about 2.8 KB at about
13,000 events per second with no spilling, of which about 12 s was spent in
the database writes. When the producer outran the database so the memory
bound was reached, the spill file roughly halved the rate. The measured
recording averaged about 3,600 events per second. These figures are from one
run on a machine unlike the target and are not a substitute for the system
test.

## Open items

- The size PostgreSQL 18 adds to the app installation. The extracted server
  subset is 1,576 files and about 128 MB before any further pruning.
- Whether identical checkpoint node states are stored once, decided by
  measurement.
- The storage size per hour of the normalized store, measured by the system
  test.
