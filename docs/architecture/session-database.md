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

- One recording produced an event log of 487.8 MB with 160,640 events.
- Another produced a 920 MB log, of which layout checkpoint node records were
  694 MB (216,255 records, about 3.2 KB each).
- On a 1.35 GB recording, the archive check at stop took about 11.7 s, about
  26 microseconds per record.

Auditors make recordings of an hour or more, and the final tool will hold many
projects, each with many recordings. A single linear file that must be parsed
before the player can respond does not scale to that use.

## Decision

1. **PostgreSQL is part of the application.** The app ships its own PostgreSQL
   server and data directory, starts the server when the app starts, and stops
   it when the app exits. The user never installs, configures, or connects to
   it directly. SQLite is not used.
2. **Events go straight into the database.** There is no event log file. A
   recording's events are rows, written while the recording runs.
3. **Writes are buffered.** If the database does not accept writes for a time,
   events wait in a buffer and are written when it does. Because the app is
   the only client of its own server, this is expected to be rare.
4. **Old recordings are not migrated.** File-based sessions made before this
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
- **Structure.** Projects, recordings, collectors, and later findings are
  related records; event payloads vary by channel. PostgreSQL holds both, with
  the payload in a `jsonb` column
  ([PostgreSQL JSON types](https://www.postgresql.org/docs/current/datatype-json.html)).
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
- Only the app can connect. The server accepts no connections from other
  hosts. The connection path is either a loopback TCP port chosen at start
  with password authentication using a secret generated at `initdb` and
  protected for the current user, or a Unix-domain socket, which PostgreSQL
  has supported on Windows since version 13
  ([PostgreSQL 13 release notes](https://www.postgresql.org/docs/13/release-13.html)).
  Which one is used is decided by a Windows test, including whether Npgsql
  connects over a Windows Unix-domain socket.
- A server that is already running from an earlier app instance that exited
  abnormally is detected and reused or restarted. PostgreSQL's own crash
  recovery handles an unclean shutdown.

### Schema outline

- `projects`, `recordings`, and `collectors`, with a recording's status
  (`recording`, `completed`, `failed`, `interrupted`), configuration, clock
  origin, and collector capability and health, which the manifest holds today.
- `events`: recording, channel, per-channel sequence, monotonic time in
  nanoseconds, event type, event identifier, quality flags, and the payload as
  `jsonb`. It is partitioned by recording
  ([Table Partitioning](https://www.postgresql.org/docs/current/ddl-partitioning.html)),
  so deleting a recording drops a partition, and indexed by channel and time.
  A unique key on recording, channel, and sequence rejects duplicates.
- `media`: frames and audio remain files in a per-recording folder next to the
  data directory. Each has a row with its path, size, time, and the SHA-256
  already computed while it was written. Large binary data stays out of the
  event tables so that event queries stay fast.

Whether layout checkpoint payloads, which dominated the measured logs, stay
in `jsonb` or move to a separate table is decided by measurement of storage
size and query time on a long recording.

### Writing during capture

- Collectors keep writing to the bounded in-memory queue they use today
  (`IRecorderEventSink`).
- One database writer takes events from the queue and writes them in batches
  with binary `COPY`, by size or by time, whichever comes first.
- The per-record checks the archive validator makes today (required envelope
  fields, known channel, monotonic time within a channel) run in the writer
  before a record is sent, or as table constraints. A record that fails is
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

- The PostgreSQL major version to ship and the size it adds to the app.
- Loopback TCP or Unix-domain socket, decided by the Windows test above.
- How layout checkpoint payloads are stored, decided by measurement.
