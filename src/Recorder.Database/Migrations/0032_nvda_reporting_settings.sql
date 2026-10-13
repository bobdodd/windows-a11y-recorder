-- NVDA's reporting settings, added to its settings record
-- (assistive-technology-settings, on system.assistive-technology): the
-- settings that decide what NVDA reports in a browse mode document, each as
-- nvda.ini writes it, or null where NVDA's default applies. The evidence
-- tables are not written for a recording that has a recording file (see
-- 0013_recording_files.sql); they keep mapping every payload member so the
-- database writer still accepts every record. See
-- docs/architecture/screen-reader-activity.md, "What NVDA had to announce".
ALTER TABLE assistive_technology_settings
    ADD COLUMN report_links text,
    ADD COLUMN report_link_type text,
    ADD COLUMN report_graphics text,
    ADD COLUMN report_headings text,
    ADD COLUMN report_lists text,
    ADD COLUMN report_block_quotes text,
    ADD COLUMN report_groupings text,
    ADD COLUMN report_landmarks text,
    ADD COLUMN report_articles text,
    ADD COLUMN report_frames text,
    ADD COLUMN report_figures text,
    ADD COLUMN report_clickable text,
    ADD COLUMN report_tables text,
    ADD COLUMN include_layout_tables text,
    ADD COLUMN detect_format_after_cursor text,
    ADD COLUMN report_font_name text,
    ADD COLUMN report_font_size text,
    ADD COLUMN font_attribute_reporting text,
    ADD COLUMN report_emphasis text,
    ADD COLUMN report_highlight text,
    ADD COLUMN report_superscripts_and_subscripts text,
    ADD COLUMN report_color text,
    ADD COLUMN report_alignment text,
    ADD COLUMN report_line_spacing text,
    ADD COLUMN report_style text,
    ADD COLUMN report_line_number text,
    ADD COLUMN report_line_indentation text,
    ADD COLUMN report_paragraph_indentation text,
    ADD COLUMN report_keyboard_shortcuts text,
    ADD COLUMN report_object_descriptions text,
    ADD COLUMN max_line_length text,
    ADD COLUMN use_screen_layout text,
    ADD COLUMN auto_say_all_on_page_load text,
    ADD COLUMN report_details text,
    ADD COLUMN report_aria_description text;
