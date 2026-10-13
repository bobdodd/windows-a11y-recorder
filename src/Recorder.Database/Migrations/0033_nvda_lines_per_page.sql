-- NVDA's lines per page ([virtualBuffers] linesPerPage), added to its
-- settings record (assistive-technology-settings, on
-- system.assistive-technology), as nvda.ini writes it, or null where NVDA's
-- default of 25 applies: how far Page Down and Page Up move NVDA's caret in a
-- browse mode document. See docs/architecture/screen-reader-activity.md,
-- "What NVDA had to announce".
ALTER TABLE assistive_technology_settings
    ADD COLUMN lines_per_page text;
