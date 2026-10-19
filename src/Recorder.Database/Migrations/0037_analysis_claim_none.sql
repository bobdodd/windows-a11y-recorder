-- A statement of kind none: that no evidence of something was found
-- (owner, 2026-10-19: "refer to evidence or state no evidence, both are
-- acceptable"). See docs/architecture/analysis-runner.md, decision 7.
ALTER TABLE analysis_claims DROP CONSTRAINT analysis_claims_claim_kind_check;
ALTER TABLE analysis_claims
    ADD CONSTRAINT analysis_claims_claim_kind_check CHECK (claim_kind IN ('claim', 'unknown', 'none'));
