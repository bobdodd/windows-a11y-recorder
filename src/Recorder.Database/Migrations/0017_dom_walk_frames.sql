-- Protocol 0.55 names, in a DOM walk's started record, the DevTools frame
-- token of the walked document's frame and whether that frame is a main
-- frame, both null for a document with no frame. The evidence tables are not
-- written for a recording that has a recording file (see
-- 0013_recording_files.sql); they keep mapping every payload member so the
-- database writer still accepts every record. See
-- docs/architecture/page-recreation.md, "Slice 5".
ALTER TABLE browser_dom_checkpoint_starts
    ADD COLUMN frame_token text,
    ADD COLUMN main_frame boolean;
