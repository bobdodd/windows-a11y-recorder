-- Projects with more than one recording (owner, 2026-11-14). See
-- docs/architecture/projects.md.
--
-- A task is of one recording, as before, or of a project: its recordings
-- taken together, such as the Android report of every recording in the
-- project. Removing a project removes its tasks, as removing a recording
-- removes its own.
ALTER TABLE analysis_tasks ALTER COLUMN recording_id DROP NOT NULL;
ALTER TABLE analysis_tasks
    ADD COLUMN project_id uuid REFERENCES projects (project_id) ON DELETE CASCADE;
ALTER TABLE analysis_tasks
    ADD CONSTRAINT analysis_tasks_one_subject
    CHECK (num_nonnulls(recording_id, project_id) = 1);
CREATE INDEX analysis_tasks_project ON analysis_tasks (project_id, queued_utc)
    WHERE project_id IS NOT NULL;
