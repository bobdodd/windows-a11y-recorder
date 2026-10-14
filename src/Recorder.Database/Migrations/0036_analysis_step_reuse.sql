-- Reusing accepted steps when a task is run again. See
-- docs/architecture/analysis-runner.md, "Reusing steps when run again".

-- The task this one runs again, when Run again was pressed rather than Run
-- again from scratch. Its accepted steps whose requests are unchanged are
-- reused, not sent to the model again.
ALTER TABLE analysis_tasks
    ADD COLUMN rerun_of_task_id uuid REFERENCES analysis_tasks (task_id) ON DELETE SET NULL;

-- For a reused attempt, the attempt whose answer it copies: the one the
-- model wrote, so that a chain of reruns leads back to it.
ALTER TABLE analysis_attempts
    ADD COLUMN reused_from_attempt_id uuid REFERENCES analysis_attempts (attempt_id) ON DELETE SET NULL;
