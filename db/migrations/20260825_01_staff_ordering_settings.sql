-- Staff ordering settings for LUCID-DREAM / ToBeClarify API.
-- Target: MariaDB database `tobeclarify`.
-- Applied to the shared tobeclarify database on 2026-08-25.
-- Every additional environment must apply this migration before deploying the matching API build.
--
-- Compatibility:
-- - Existing staff remain closed for nomination (`IS_NOMINATABLE = 0`).
-- - Existing services keep their legacy `PRICE_TEXT`; structured values remain NULL.
-- - Existing services keep the previous UI default of being nominatable.

ALTER TABLE `STAFF_MEMBERS`
    ADD COLUMN IF NOT EXISTS `BUFFER_MINUTES` SMALLINT UNSIGNED NULL
        COMMENT 'Optional buffer between two nominations, in minutes' AFTER `PROFILE_BIO`,
    ADD COLUMN IF NOT EXISTS `IS_NOMINATABLE` TINYINT(1) NOT NULL DEFAULT 0
        COMMENT 'Whether the staff member currently accepts nominations' AFTER `BUFFER_MINUTES`;

ALTER TABLE `STAFF_SERVICES`
    ADD COLUMN IF NOT EXISTS `PRICE` INT UNSIGNED NULL
        COMMENT 'Structured service price in Gil' AFTER `PRICE_TEXT`,
    ADD COLUMN IF NOT EXISTS `DURATION_MINUTES` SMALLINT UNSIGNED NULL
        COMMENT 'Structured service duration in minutes' AFTER `PRICE`,
    ADD COLUMN IF NOT EXISTS `IS_NOMINATABLE` TINYINT(1) NOT NULL DEFAULT 1
        COMMENT 'Whether this service can be nominated' AFTER `DURATION_MINUTES`,
    ADD COLUMN IF NOT EXISTS `ADDITIONAL_PERSON_PRICE` INT UNSIGNED NULL
        COMMENT 'Additional price per extra person in Gil' AFTER `IS_NOMINATABLE`;

-- Verification query:
-- SELECT TABLE_NAME, COLUMN_NAME, COLUMN_TYPE, IS_NULLABLE, COLUMN_DEFAULT
-- FROM information_schema.COLUMNS
-- WHERE TABLE_SCHEMA = DATABASE()
--   AND TABLE_NAME IN ('STAFF_MEMBERS', 'STAFF_SERVICES')
--   AND COLUMN_NAME IN (
--       'BUFFER_MINUTES', 'IS_NOMINATABLE', 'PRICE', 'DURATION_MINUTES',
--       'ADDITIONAL_PERSON_PRICE'
--   )
-- ORDER BY TABLE_NAME, ORDINAL_POSITION;

-- Rollback requires a separately approved DBA migration because it drops stored data.
