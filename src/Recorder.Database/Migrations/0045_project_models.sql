-- A project's models (owner, 2026-11-15). See docs/architecture/projects.md,
-- "The project's models".
--
-- The model profile, by name, chosen for each use: report analysis (the
-- summaries), text-based checks and vision-based checks. Null is no
-- model, when no model is asked, cloud or local. Every project, new or
-- already made, starts with no model for each, so nothing is sent to a
-- model until one is chosen. The names are of profiles in the analysis
-- settings file on this PC; a name no profile has any longer is kept, and
-- reported, rather than dropped.
ALTER TABLE projects
    ADD COLUMN report_model text CHECK (length(report_model) > 0),
    ADD COLUMN text_checks_model text CHECK (length(text_checks_model) > 0),
    ADD COLUMN vision_checks_model text CHECK (length(vision_checks_model) > 0);
