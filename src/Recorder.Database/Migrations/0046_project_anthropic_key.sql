-- A project's Anthropic API key (owner, 2026-11-15: "I choose Haiku and
-- add my API key and it should work. This is a project based interface").
-- See docs/architecture/projects.md, "The project's models".
--
-- The key is protected for the Windows user with the Windows Data
-- Protection API before it is written, and is never shown again. Null is
-- no key, so a Claude model chosen for the project cannot be asked until
-- one is saved. The Claude models a project chooses are the recorder's
-- own, each with its defaults; there are no model profiles to set up.
ALTER TABLE projects
    ADD COLUMN anthropic_api_key text CHECK (length(anthropic_api_key) > 0);
