-- Prepare ADMIN_USERS for an API build that no longer writes DISPLAY_NAME.
-- Target: MariaDB database `tobeclarify`.
--
-- This compatibility migration is safe to apply before deploying the matching
-- API build. Existing API instances may continue writing DISPLAY_NAME, while
-- the matching build may omit it during registration.

ALTER TABLE `ADMIN_USERS`
    MODIFY COLUMN `DISPLAY_NAME` VARCHAR(60) NULL DEFAULT NULL
        COMMENT 'Deprecated; display names are sourced from STAFF_MEMBERS';

-- Verification query: IS_NULLABLE must be YES.
-- SELECT COLUMN_NAME, COLUMN_TYPE, IS_NULLABLE, COLUMN_DEFAULT
-- FROM information_schema.COLUMNS
-- WHERE TABLE_SCHEMA = DATABASE()
--   AND TABLE_NAME = 'ADMIN_USERS'
--   AND COLUMN_NAME = 'DISPLAY_NAME';

