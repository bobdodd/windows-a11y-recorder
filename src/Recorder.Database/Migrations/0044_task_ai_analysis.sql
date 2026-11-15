-- The switch that turns AI analysis off (owner, 2026-11-15). See
-- docs/architecture/analysis-runner.md, "Turning AI analysis off".
--
-- Whether a task may use a model, as the player queued it: the analysis
-- setting, or the report's own checkbox, which starts from the setting.
-- A task queued with it off makes no request to any model and reuses no
-- model's answer. Tasks queued before the switch existed could use one.
ALTER TABLE analysis_tasks
    ADD COLUMN ai_analysis boolean NOT NULL DEFAULT true;
