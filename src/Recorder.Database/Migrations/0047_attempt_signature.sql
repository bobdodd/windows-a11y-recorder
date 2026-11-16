-- What a check's answer is kept against beside its request, where the
-- request is not the same each time for the same thing: for the check of
-- images with no text alternative, the image's own pixels and the text
-- beside it, so that an identical image is given the first answer again
-- across visits and recordings in the project (owner, 2026-11-16, rule 4;
-- docs/architecture/android-checks.md, step 20). Six identical icons were
-- asked about on 2026-11-16 and given two answers; the pictures sent
-- differed by the screen round them, so their requests differed.
--
-- The bytes are the check's own (AndroidImageAlternative.Signature): a
-- version, the image's width and height in pixels, the SHA-256 of the
-- text beside it, and a 16 by 16 thumbnail of its pixels. Two are the
-- same image when the check says so, within a tolerance, so they are not
-- compared here. Null for every other step.
ALTER TABLE analysis_attempts
    ADD COLUMN input_signature bytea;
CREATE INDEX analysis_attempts_signature ON analysis_attempts (prompt_template, prompt_template_version)
    WHERE input_signature IS NOT NULL;
