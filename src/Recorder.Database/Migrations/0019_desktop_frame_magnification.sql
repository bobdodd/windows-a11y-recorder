-- A desktop frame records the full screen magnification transform read as it
-- was captured, or the problem that stopped the reading. The evidence tables
-- are not written for a recording that has a recording file (see
-- 0013_recording_files.sql); they keep mapping every payload member so the
-- database writer still accepts every record. Frames stored before this
-- migration have no reading. See docs/architecture/magnified-view-playback.md.
ALTER TABLE desktop_frames
    ADD COLUMN has_fullscreen_magnification boolean NOT NULL DEFAULT false,
    ADD COLUMN fullscreen_magnification_level double precision,
    ADD COLUMN fullscreen_magnification_x integer,
    ADD COLUMN fullscreen_magnification_y integer,
    ADD COLUMN fullscreen_magnification_problem text;

ALTER TABLE desktop_frames
    ALTER COLUMN has_fullscreen_magnification DROP DEFAULT;
