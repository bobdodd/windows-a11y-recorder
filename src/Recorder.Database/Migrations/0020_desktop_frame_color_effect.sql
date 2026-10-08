-- A desktop frame records the full screen color effect read as it was
-- captured, the 25 values of its matrix row by row, or the problem that
-- stopped the reading. The evidence tables are not written for a recording
-- that has a recording file (see 0013_recording_files.sql); they keep
-- mapping every payload member so the database writer still accepts every
-- record. Frames stored before this migration have no reading. See
-- docs/architecture/magnified-view-playback.md, "Color effect".
ALTER TABLE desktop_frames
    ADD COLUMN has_fullscreen_color_effect boolean NOT NULL DEFAULT false,
    ADD COLUMN fullscreen_color_effect_matrix double precision[],
    ADD COLUMN fullscreen_color_effect_problem text;

ALTER TABLE desktop_frames
    ALTER COLUMN has_fullscreen_color_effect DROP DEFAULT;
