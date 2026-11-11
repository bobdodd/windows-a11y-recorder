-- Models a service names, such as Claude through Anthropic's API (owner,
-- 2026-11-11). See docs/architecture/cloud-models.md.
--
-- Such a model has no file, so an attempt with it has no model SHA-256;
-- its basis is "service": the model is the one the profile names, as the
-- service serves it. The model is named in the request, whose SHA-256 is
-- kept with the attempt.
ALTER TABLE analysis_attempts DROP CONSTRAINT analysis_attempts_model_sha256_basis_check;
ALTER TABLE analysis_attempts
    ADD CONSTRAINT analysis_attempts_model_sha256_basis_check
    CHECK (model_sha256_basis IN ('hashed', 'stated', 'unknown', 'service'));

-- What the service said of each answer: the ID it gave the response
-- (Anthropic's request-id header) and the model it says wrote it
-- (Anthropic's model field). Null for a local server, which gives neither.
ALTER TABLE analysis_attempts
    ADD COLUMN service_request_id text,
    ADD COLUMN service_model text;
