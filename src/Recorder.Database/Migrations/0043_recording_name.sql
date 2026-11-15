-- Readable names for recordings (owner, 2026-11-14). See
-- docs/architecture/recording-names.md.
--
-- A recording may have a name, given when it starts or later; without one
-- it is known by its session key, as before. The session key stays the
-- recording's identity and its folder's name. A name is saved with spaces
-- at either end removed, and no two recordings in one project have the
-- same name ("Unique within project"), compared as typed, as project
-- names are.
ALTER TABLE recordings
    ADD COLUMN name text
    CHECK (name IS NULL OR (length(name) > 0 AND name = btrim(name)));
CREATE UNIQUE INDEX recordings_project_name ON recordings (project_id, name)
    WHERE name IS NOT NULL;
