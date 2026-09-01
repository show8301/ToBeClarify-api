-- Optional transparent staff signature artwork used in public roster cards.
-- The display name remains the accessible and text fallback value.

ALTER TABLE `STAFF_MEMBERS`
    ADD COLUMN IF NOT EXISTS `SIGNATURE_MEDIA_ID` VARCHAR(40) NULL AFTER `AVATAR_MEDIA_ID`,
    ADD KEY IF NOT EXISTS `IX_STAFF_MEMBERS_SIGNATURE_MEDIA_ID` (`SIGNATURE_MEDIA_ID`);
