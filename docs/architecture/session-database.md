# Session Database

Status: decided, implemented on the `postgres-session-store` branch and not
yet merged. This record states the decision and the design it implies. It
replaced the file-based event log and the checks built around it.

## Context

Before this decision, the recorder wrote every event to one append-only file,
`events.ndjson`, per recording. Stopping a recording validated the archive by
parsing every record, and opening a recording parsed every record again to
build the player's timeline. Both costs grew with the length of the recording.

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

The integrity of a recording's events rests on the database: constraints,
transactions, and write-ahead logging, rather than on a hashed event log. The
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
- **Per-recording tables.** Every recording's events and evidence share
  one set of ordinary tables, whose keys and indexes lead with the
  recording, so a query for one recording reads only that recording's
  entries. The tables were first partitioned by recording
  ([Table Partitioning](https://www.postgresql.org/docs/current/ddl-partitioning.html)),
  one partition of each of 135 tables per recording. Every foreign key
  check on a written row then grew slower with each recording in the
  database, and on a database of 39 recordings the writer fell behind a
  recording and could not finish it; see Implementation status. Migration
  0009 replaced the partitions with ordinary tables.

Layout checkpoint node records were about three quarters of a measured log.
Each node's computed style, a map of about 340 properties in a browser
recording, was stored as one row per property for every node. Migration 0010
stores each distinct computed style of a recording once, with its entries, and
has each node refer to it; see Implementation status. Whether other identical
node states in successive checkpoints are stored once and referenced is
decided by measuring storage size and query time on an hour-long recording.

### Writing during capture

- Collectors keep writing to the bounded in-memory queue they use today
  (`IRecorderEventSink`).
- The database writer takes events from the queue in batches, by size or by
  time, whichever comes first, and writes up to a configured number of
  batches at once, each on its own connection with binary `COPY` in one
  transaction. A batch is stored completely or not at all.
- The writer maps each event to its rows, resolving identities to keys it
  caches for the recording. The per-record checks the finalization archive
  check used to make (envelope fields, event identifier, evidence class,
  sequence and time order within a channel, and the payload's shape for its
  channel and event type) run in the writer before a record is sent, or as
  table constraints. A record that fails is not stored; it is counted, its
  reason is stored with the recording, and the recording is stored as
  failed.
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

The following were removed rather than kept alongside: `events.ndjson` and
`NdjsonEventWriter`, the event log's entry in the manifest's artifact inventory
and in `ArtifactHashRegistry`, the finalization archive check
(`SessionArchiveValidator`) and its report, `SessionArchiveReader`,
`NdjsonLineReader`, and the coordinator's copying of each event to both the
file and the database. The capture host uses the same database and writer. The
privacy and data handling policy and the threat model were revised in the same
change.

Three parts remain, in a changed role:

- `manifest.json` stays in the session folder. It states the recording's
  settings, collectors, outcome, and counts, and its artifact inventory lists
  and hashes the media files, which are not yet database rows.
- `SessionPlaybackArchiveBuilder` builds the player's frames, audio tracks, and
  browser navigations from the events the database reader supplies.
- The per-record checks moved into the writer: `EventRecordValidator` checks
  the envelope and `EventPayloadValidator` the payload. They check each record
  and its order within its stream. They do not check that an event a record
  cites exists in the recording, which the archive check did across the whole
  file, because a record may cite an event that has not yet arrived.
- The archive check also compared each media file with its size and hash in
  the manifest, and the manifest's counts and status with the event log. No
  check of the session folder runs at stop now; the manifest's hashes are
  computed and written, and nothing compares the files with them later.
  Duplicate event identifiers, which the archive check reported, are refused
  by the database's unique key.

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
- **Per-recording tables.** `recording_tables` lists the tables that hold
  rows of each recording, in an order that puts a referenced table before
  the tables that refer to it. Creating a recording adds only its row.
  Deleting a recording removes its rows from each listed table, referring
  tables first, and then its recording row, in one transaction. The foreign
  key checks are skipped while the listed tables' rows are removed, because
  every row of the recording goes and a checked removal looks for referring
  rows through columns no index leads with; the recording row is removed
  with the checks in force. In one Linux test run on the development
  sandbox, not on the target Windows machine, writing a recording of 44,100
  events took 18.5 to 24.7 seconds with 40 earlier recordings in the
  partitioned tables, and 4.5 seconds with 40 earlier recordings in the
  ordinary tables. Deleting it took about 0.14 seconds with the checks
  skipped; with them in force it took about 30 seconds, and a second
  deletion exceeded the 30-second command limit.
- **Migration 0009.** Creates each per-recording table again as an
  ordinary table with the same name, columns, checks, keys, indexes, and
  foreign keys, under the same constraint and index names, renames the
  partitioned table `legacy_<order>`, and lists the tables in
  `recording_tables`. Removing thousands of partitions in one transaction
  locks each of them and every object that depends on them, which on a test
  database of 20 recordings was about 92,000 locks, more than PostgreSQL's
  lock table holds with its default `max_locks_per_transaction` of 64 for
  each server process
  ([Lock Management](https://www.postgresql.org/docs/current/runtime-config-locks.html)).
  `LegacyPartitions`, which `DatabaseMigrator` runs after the migrations,
  therefore finishes the move in small transactions: it drops the legacy
  tables' foreign keys one at a time, then copies each recording's rows into
  the ordinary tables and drops its partitions, one recording per
  transaction, and then drops the empty legacy tables. On that test
  database the migration took about 2,400 locks and each recording's move
  about 2,500. Every step can be repeated, so an interrupted move continues
  the next time the database opens. Migrations run without a command time
  limit, because the move takes as long as the data it copies. In the same
  sandbox, migrating a database of 60 recordings of 1,470 events each took
  about 127 seconds, during which the database is not yet open.
- **Migration 0010.** Stores each distinct computed style of a recording once.
  `browser_computed_styles` holds one row for each distinct style, and
  `browser_computed_style_entries` its properties and values;
  `browser_layout_checkpoint_nodes.computed_style_key` refers to it, and is
  null for a node whose computed style is null. The migration moves existing
  styles into the new tables, identifying equal styles by a SHA-256 digest of
  their entries ordered by name identifier, and then drops the table of per-node
  entries. It also makes every foreign key of the per-recording tables
  deferrable, still checked at each statement unless a transaction defers
  it. A test writes the evidence samples at version 10, returns the schema to
  its version 9 form, applies 0010 again, and requires every payload to read
  back unchanged and each distinct style to be stored once. On a 30-second
  browser recording on the target Windows machine before this migration,
  114,740 events were recorded and 10,875 were not written within the
  completion timeout, because each node's style became about 340 rows.
- **Migration 0011.** Checks the references between a recording's rows
  once, when the recording is completed, instead of at each row. It lists in
  `recording_references` every foreign key from a per-recording table to
  another per-recording table or to `names`, with its columns and the
  referenced table and columns, and drops those foreign keys; foreign keys to
  lookup tables and to `recordings` remain. When a recording is completed,
  `RecordingStore.CheckReferencesAsync` runs one query per listed reference,
  several at once, counting the recording's rows whose referenced row is
  missing, and each reference with such rows makes the recording failed,
  with the table, columns, and count in the failure reason. A reference with
  a null column is not checked, as a foreign key would not check it. The
  migration also stores each dispatch path scope's visible path indexes as
  an `integer[]` column, `visible_path_indexes`, on
  `browser_dispatch_path_scopes`, in their original order, and drops the
  table that held one row per index. A test writes the evidence samples at
  version 11, returns the scopes to their version 10 form, applies 0011
  again, and requires every payload to read back unchanged; another inserts
  a row whose owner is missing and requires the check to report it.
  PostgreSQL's guidance on bulk loading states that checking foreign keys
  for many rows at once is more efficient than checking them row by row
  ([Populating a Database](https://www.postgresql.org/docs/current/populate.html)).
  In the Windows recording that led to this migration, 442,588 events were
  accepted and 83,993 were left in the spill file when the completion
  timeout ended writing. Summed over the four writing connections, commits
  took 178 s and waits for another batch's commit 252 s, against about 45 s
  for all `COPY` steps. The statistics of the recorder's server showed about
  20.5 million index scans of `browser_dispatch_path_scopes` and 19.9 million
  of `names`, and the table of visible indexes held about 6.7 million of the
  8.9 million rows stored. These figures are from one run and are not a
  substitute for the system test.
- **Server garbage collection.** The app uses .NET server garbage
  collection, which collects with one thread and heap per logical processor
  ([Workstation and server garbage collection](https://learn.microsoft.com/en-us/dotnet/standard/garbage-collection/workstation-server-gc)),
  set by the `ServerGarbageCollection` property
  ([Garbage collector config settings](https://learn.microsoft.com/en-us/dotnet/core/runtime-config/garbage-collector)).
  With the default workstation collection, the same recording paused the
  app for 12.5 s in total over 131 s. The first Windows recording with
  server collection paused it for 7.1 s over 132 s; that recording had
  979,783 events against 442,588, so the two runs are not a like-for-like
  comparison.
- **Measured after migration 0011.** On that recording, the writer stored
  every accepted event: no event was spilled, at most about 2,000 events
  waited in memory, and the last event was written about one second after
  the last was accepted. Summed over the four writing connections, commits
  took 64.7 s and waits for another batch's commit 6.6 s. The reference
  check found no missing rows and took 11.9 s when recording stopped, most
  of the time between stopping and showing the recording. Run again later
  on the same recording, one query at a time, the 455 reference queries
  took 2.7 s in total, 350 of them under 2 ms each; the faster repeat is
  not explained, and may come from the data then being in memory. The check
  now skips the tables that have no rows in the recording, runs up to half
  the processors' worth of queries at once, at most eight, and times each
  query under `check-references:<table>` so the next recording shows where
  the time goes. The recording was stored as failed because 24
  `browser.dispatch` records were refused for a timestamp earlier than the
  record before them; see the evidence queue section of
  `chromium/recorder_bridge/README.md`. These figures are from one run and
  are not a substitute for the system test.
- **Reference check planning.** The first recording with the timed check
  (53c43ba, 523,534 events) completed with no rejections, and the check
  took 8.6 s. The slowest queries checked
  `browser_layout_checkpoint_style_properties` (4.7 s),
  `browser_dispatch_path_targets` (3.7 s), `browser_dispatch_path_scopes`
  (3.65 s), and `browser_dom_checkpoint_nodes` (1.46 s). About a minute
  later, after autovacuum had analyzed the tables, the same targets query
  took 122 ms with a hash anti join and the style properties query 21 ms.
  A measurement on the development sandbox, not the target machine,
  isolated the cause. A base recording of the evidence samples was written
  through the writer and copied into three earlier recordings, which were
  vacuumed and analyzed, with autovacuum off; a fresh copy was then checked
  with `EXPLAIN (ANALYZE, BUFFERS)` for each reference of an occupied table:
  - with the statistics from before the copy, the planner estimated about
    one row for the new `recording_id` and chose nested loop anti joins
    that compared each referring row with every referenced row of the
    recording; one query reported 3,441,650 rows removed by its join filter;
  - checking the same copy a second time took as long, so the first read of
    new rows was not the cause;
  - analyzing the occupied tables first, or disabling nested loop joins,
    led the planner to hash or merge anti joins.

  | Base events | Stale statistics | Analyze, then check | Nested loops off |
  |---|---|---|---|
  | 7,300 | 5,504 ms | 241 ms + 717 ms | 253 ms |
  | 21,900 | 47,640 ms | 303 ms + 524 ms | 514 ms |

  With stale statistics the time grew about ninefold for three times the
  rows, as a comparison of every row with every row does; both remedies grew
  about in proportion to the rows. Disabling nested loops was the faster of
  the two at both sizes and needs no statistics, so each check query now
  runs in its own transaction after `SET LOCAL enable_nestloop = off`,
  which ends with the transaction and leaves pooled connections unchanged.
  The store's own check of a fresh copy at the larger size took 475 ms. A
  test requires every pooled connection to plan with nested loops enabled
  after a check. The measurement used the evidence samples, not a real
  recording, on a two-processor Linux machine.
- **Reference check on Windows with nested loops off.** In the first
  Windows recording with this change (2bba098, 93.6 s), the check took
  5.2 s against 8.6 s in the recording before it, which was not the same
  capture. The 334 queries summed to 33.1 s over up to eight connections;
  the slowest checked `browser_dispatch_path_scopes` (3.7 s),
  `browser_dispatch_path_targets` (2.6 s), and
  `browser_dom_checkpoint_attributes` (2.3 s). About half an hour later,
  with the same setting, the scopes query took 84 ms with a merge anti join
  and the attributes query 144 ms with a hash anti join, reading between
  3,000 and 7,600 buffers each. The sandbox result therefore did not carry
  over: the check at stop is still 25 to 45 times slower than the same
  queries run later, and the measurements do not show why. To find out,
  the check now notes in `database-writer-timings.json`, under `notes`:
  - `check-references-activity:start`, the server's other active client
    connections, autovacuum workers, and parallel workers, each with its
    state, wait event, and the start of its query;
  - `check-references-statistics:start`, for each table the queries read,
    its live rows, the rows changed since its statistics were last
    gathered, and how long ago that was;
  - `check-references-plan:<table>(<columns>)-><referenced table>`, for
    each query that took at least 250 ms, its plan with each step's actual
    rows and buffers, and the time the query spent reading and writing
    buffers. The plan comes from `auto_explain`, a module PostgreSQL ships
    ([auto_explain](https://www.postgresql.org/docs/current/auto-explain.html)),
    which the check loads into its own connections and sets, for its own
    transactions only, to send each plan to the connection as a notice
    rather than to the server log. If the module cannot be loaded, the check
    runs without noting plans. Steps are not timed one by one: the module's
    documentation states that per-step timing applies to every statement,
    noted or not, and can have an extremely negative effect on performance.
    Counting rows and buffers still adds some cost, not measured here.
  Tests require a check with a zero threshold to note the plan of a known
  query, with actual rows and buffers, no per-step times, and no nested
  loop, and a check with
  the default threshold to note no plan for the quick queries of a small
  recording.
- **Parallel writing.** `PostgresEventWriter` writes up to
  `WriterConnections` batches at once, four by default; while the database
  is unavailable it retries one batch at a time. Each batch is written by
  `PostgresEventBatchTarget` in one transaction that defers its remaining
  foreign key checks to commit, so a batch is stored completely or not at all and a
  failure leaves no partial rows. Batches are prepared one at a time, in
  order, and each identity a batch needs, such as a computed style, is
  claimed by the first batch that needs it:
  - a batch writes the identities it claimed with its own rows;
  - a batch that needs an identity an earlier batch claimed waits for that
    batch to finish before committing, so the row it refers to exists;
  - if the earlier batch failed, the waiting batch writes the identity
    itself, and so does a batch that needs an identity a later batch
    claimed. Those writes go through a temporary table and insert only the
    rows not yet present, so an identity is stored once whichever batch
    commits first.
  Events are ordered by their event key, not by commit order. The events of
  batches being written count against the memory bound until they are
  committed, so spilled events are read back only as far as they fit in the
  bound, and not at all while a write is being retried. At stop, the events
  of batches not yet committed are spilled with the rest. Tests
  require a writer never to exceed its configured number of batches at
  once, and require parallel writing with small batches to store every
  payload and the same number of identity rows as one writer. In throughput
  checks on the development sandbox, a two-processor Linux machine, not on
  the target Windows machine, 20,000 layout nodes sharing 200 distinct
  340-property styles were written in 8.1 seconds with one connection and
  4.7 seconds with four; 1,000 nodes with 1,000 distinct styles took 11 to
  13.5 seconds either way, since they formed one batch. These times vary
  between runs on that machine and do not predict times on the target.
- **Schema checks.** A test holds the SHA-256 of each applied migration file,
  so a migration a database may already have applied is not changed, and
  compares the live schema of a migrated database with the evidence
  catalog: each table's columns, primary key, references listed in
  `recording_references`, and position in `recording_tables` after the
  tables it refers to; that no foreign key refers to another per-recording
  table or to `names`; and that every remaining foreign key is deferrable.
- **Writing.** `PostgresEventWriter` checks each record with
  `EventRecordValidator`, which checks the envelope and passes the payload to
  `EventPayloadValidator`, and rejects a record that fails into
  `event_rejections` with the first issue's code, such as
  `event-sequence-not-increasing` or `payload-property-unexpected`. The
  writer's result names the first rejection, and a recording with any
  rejected or unwritten events is stored as failed, with a failure reason
  that gives the count and the first rejection. It holds up to 256 MB in memory, then writes to a spill
  file of up to 8 GB, and records events refused beyond that as omission runs
  in `writer_omissions`. A batch the database refuses for a reason other than
  an outage is written event by event, so one bad record refuses only itself.
  If the store cannot be reached within the completion timeout when
  recording stops, the spill file is kept and its path is reported.
- **Evidence tables.** An event's payload is stored in typed tables chosen by
  its channel and event type, generated from the evidence catalog in
  `src/Recorder.Database/Evidence/EvidenceCatalog.cs`. The catalog assigns
  its tables to migrations: `0003_evidence_tables.sql`,
  `0004_browser_script_evidence.sql`, `0005_browser_document_evidence.sql`,
  `0006_browser_rendering_evidence.sql`, and
  `0007_browser_network_evidence.sql` are generated from it, and a test
  requires each file to match; setting `RECORDER_REGENERATE_EVIDENCE_MIGRATION`
  to `1` while running that test rewrites them. A table is created by the
  first migration whose event types reach it, and table orders continue
  from one migration to the next, so a later migration adds tables without
  changing one a database has already applied. A migration after 0008
  creates ordinary tables and lists them in `recording_tables`. Each payload
  member is a column, except that:
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
    entry name, except a dispatch path scope's visible path indexes, which
    are an `integer[]` column of the scope since migration 0011.
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
  The fourth slice, migration 0006, covers the browser layout and
  presentation channels: layout checkpoints with their nodes, computed
  styles, and pseudo-elements, and presentation requests, not-swapped
  outcomes, swaps, and feedback. A computed style value that is null is
  stored as a NULL value, and a null computed style is kept apart from an
  empty one. Presentation tick and microsecond values stay decimal text, as
  the payload carries them.
  The fifth slice, migration 0007, covers the browser cookie and network
  channels: document and Cookie Store cookie reads, writes, results, and
  changes, cookie accesses with their cookies, network requests,
  responses, completions, failures, memory cache hits, wire headers, and
  navigation responses, and WebSocket, EventSource, and WebTransport
  records. Header lists, cookie lists, and the withheld parts of a
  recorded text are child tables, and a network scope is stored once per
  recording. The tables have no column for a cookie value, as the cookie
  payloads have no member for one.
- **Stored forms.** The rebuilt payload matches the written payload in
  content, with these normal forms: a member the validator allows to be
  absent, when written as null, reads back absent; a UTC time is stored to
  the tenth of a microsecond and reads back in the form System.Text.Json
  writes, without trailing fractional zeros; a number stored as a double
  reads back in PostgreSQL's shortest form, so a browser's `544.0` reads back
  as `544`; and property order is not kept.
  A payload member the validator does not allow is refused as
  `payload-property-unexpected`. A payload member the catalog does not hold,
  a member of the wrong type, and text containing a NUL character, which
  PostgreSQL text cannot store, are refused by the evidence tables with a
  reason such as `payload-member-unmapped:payload/colour` or
  `payload-text-nul:payload/note` in `event_rejections`, rather than stored
  in part.
- **Other channels.** Every event type of the recorder's built-in channels
  has evidence tables. The writer refuses an event of a built-in channel whose
  type the validator does not define, with the reason
  `event-type-unsupported`, and one whose type has no evidence tables, with
  the reason `event-type-unmapped`. The payload of
  an event on a channel the recorder does not define, which has no evidence
  model, is stored in `event_payloads_other_channels` as `jsonb`, and the
  player reads it from there. Migration 0008 renamed the transitional
  `event_payloads_unmapped` table to this name, and removed each recording
  that still held a built-in payload in it, as the recording store removes a
  recording. The session folders of removed recordings are left on disk.
- **Binaries.** `scripts/Get-PostgresBinaries.ps1` downloads the EDB Windows
  x64 binaries archive for 18.6, checks its SHA-256 against the value computed
  from the archive downloaded on September 25, 2026, and extracts the server
  programs, libraries, shared files, and licences into `.postgres\pgsql`,
  which is not committed. The integration tests use those binaries, or the
  directory named by `RECORDER_POSTGRES_BIN`.

- **Recording.** `SessionDatabase` takes a lock file next to the data
  directory, starts the server, marks recordings an earlier run left in the
  recording state as interrupted, and holds one project, named "Default
  project", until projects are exposed in the app. Only one recorder process
  uses the database at a time: while the app has it open, the capture host
  refuses to start, and the reverse, with the message "Another Windows A11y
  Recorder process is using the database." The coordinator requires a
  database. Each recording is created in the database with its capture
  settings when it starts, its collectors are registered with their
  capability, and each event and marker goes to the database writer. At stop
  the collectors stop, their final lifecycle and health states are recorded,
  the writer drains, the manifest is written with the outcome, the counts,
  and the media inventory, and the recording's status, end time, duration,
  and event counts are stored. A collector that fails to stop, or any event
  that was rejected or not written, makes the recording failed, in both the
  manifest and the database. The coordinator's status reports the writer's
  accepted, written, rejected, dropped, and unwritten counts and any database
  problem, and the app and capture host show them.
- **Spill location.** A recording's spill file is
  `spill\<session key>.ndjson` in the database's data directory, not the
  session folder. It is a buffer, not a log: the writer deletes it once it
  has drained it, and keeps it only when the store could not be reached
  before the completion timeout.
- **Session folder.** A recording's folder holds `manifest.json`, the frames
  and audio files, and, while the recording runs, a `.recording` marker. It
  holds no event log.
- **Playback.** `DatabasePlaybackReader` finds a recording by its session
  key, which is the session folder's name. A recording stored as completed,
  failed, or interrupted is opened from the database, and the app shows a
  failed or interrupted status with the recording, since such a recording
  holds every event that was stored before it ended. A recording still being
  recorded, and one the database does not hold, are not opened, and the app
  shows the reason. There is no other source of events. Opening a recording reads only
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
  order they were stored in. Queries are asynchronous, so the
  window stays responsive while one runs, and a result that arrives after
  the user has moved on is discarded.
  An event's complete record is read from the database only when it is
  selected in the inspector, and is rebuilt from the envelope columns and
  child tables, and its payload from the evidence tables. The rebuilt record
  matches the written record in content but not byte for byte, in the
  stored forms described above: a payload's property order is not kept,
  quality flags are returned in the order their names were first stored, and
  numbers are returned as PostgreSQL normalizes them. `manifest.json` is
  read from the session folder, and frames and audio are read from their
  files. Records are checked when they are written, not again when a
  recording is opened. On the Linux development sandbox, a
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
  can be neither made nor opened. The capture host starts the same database
  in the same data directory. The builds of both copy `.postgres\pgsql`, when
  present, to `pgsql` in their output folders.

Still to come on the branch: media rows for frames and audio, which would let
the manifest's artifact inventory go, and the hour-long Windows system test.
The Windows validation scripts `Run-AppSessionValidation.ps1`,
`Verify-AppSessionEvidence.ps1`, `Run-BlinkValidation.ps1`, and
`Verify-BlinkEvidence.ps1` read `events.ndjson` and the archive check's
report, and do not run against a recording made on this branch until they
read the database instead.

A throughput probe on the Linux development sandbox, with two processor cores
and a debug build, wrote 200,000 events with payloads of about 2.8 KB at about
13,000 events per second with no spilling, of which about 12 s was spent in
the database writes. When the producer outran the database so the memory
bound was reached, the spill file roughly halved the rate. The measured
recording averaged about 3,600 events per second. These figures are from one
run on a machine unlike the target and are not a substitute for the system
test.

A 94 s Windows recording on the same branch produced about 4,700 events per
second, and the writer stored about 2,700 per second. When recording stopped,
the writer used its 60 s completion timeout, 33,151 events stayed in the spill
file, and the recording was stored as failed. To find where the time goes,
each recording's folder gets `database-writer-timings.json`, written by
`WriterTimings` when writing finishes: the total and longest time of each
writing step, with the events or rows it handled, and a once-a-second sample
of the writer's backlog and of the processor time of the app and of the
`postgres` processes. The `postgres` total counts every process with that
name, which on the recorder's machine are the app's own server.

## Open items

- The size PostgreSQL 18 adds to the app installation. The extracted server
  subset is 1,576 files and about 128 MB before any further pruning.
- Whether identical checkpoint node states are stored once, decided by
  measurement.
- The storage size per hour of the normalized store, measured by the system
  test.
