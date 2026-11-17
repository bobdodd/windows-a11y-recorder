-- The models for complex questions (owner, 2026-11-17). See
-- docs/architecture/projects.md, "Simple and complex questions".
--
-- Each kind of check, text-based and vision-based, has a model for its
-- simple questions, the columns of 0045, and a choice for its complex
-- questions and for the simple ones the first model could not settle.
-- Null is the same model as the simple questions, so every project, new
-- or already made, asks one model for a kind of check until another is
-- chosen. An empty name is no model for complex questions. Otherwise it
-- is the name of a profile, kept and reported, rather than dropped, where
-- no profile has it any longer.
ALTER TABLE projects
    ADD COLUMN complex_text_checks_model text,
    ADD COLUMN complex_vision_checks_model text;
