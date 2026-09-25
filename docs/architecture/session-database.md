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
  recording's folder, which the writer drains in order and deletes once the
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

## Open items

- The size PostgreSQL 18 adds to the app installation.
- Whether identical checkpoint node states are stored once, decided by
  measurement.
- The storage size per hour of the normalized store, measured by the system
  test.
