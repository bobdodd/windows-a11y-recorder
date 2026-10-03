-- Protocol 0.43 presents a page popup's rendering updates, such as those of
-- an open select list, from the popup's own widget. A presentation record
-- names its widget's kind, "frame" or "page-popup", and a page popup's widget
-- is not told its frame sink, so its frame sink is null. The evidence tables
-- are not written for a recording that has a recording file (see
-- 0013_recording_files.sql); they keep mapping every payload member so the
-- database writer still accepts every record. See
-- docs/architecture/page-recreation.md, "Slice 4d".

-- As since 0011_bulk_reference_checks.sql, a reference to names is listed
-- in recording_references and checked when the recording is completed, not
-- by a foreign key.
ALTER TABLE browser_presentation_requests ADD COLUMN widget_kind_name_id integer;

ALTER TABLE browser_presentations_not_swapped
    ALTER COLUMN frame_sink_id DROP NOT NULL,
    ADD COLUMN widget_kind_name_id integer;

ALTER TABLE browser_presentation_swaps
    ALTER COLUMN frame_sink_id DROP NOT NULL,
    ADD COLUMN widget_kind_name_id integer;

ALTER TABLE browser_presentation_feedback
    ALTER COLUMN frame_sink_id DROP NOT NULL,
    ADD COLUMN widget_kind_name_id integer;

INSERT INTO recording_references (table_name, columns, referenced_table, referenced_columns)
VALUES
    ('browser_presentation_requests', '{widget_kind_name_id}', 'names', '{name_id}'),
    ('browser_presentations_not_swapped', '{widget_kind_name_id}', 'names', '{name_id}'),
    ('browser_presentation_swaps', '{widget_kind_name_id}', 'names', '{name_id}'),
    ('browser_presentation_feedback', '{widget_kind_name_id}', 'names', '{name_id}');
