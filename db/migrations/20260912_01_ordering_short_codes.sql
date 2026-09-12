-- Add a short bearer code for customer ordering URLs.
-- Apply with a migration-capable account before deploying the matching API build.
ALTER TABLE `CUSTOMER_ORDER_SESSIONS`
    ADD COLUMN IF NOT EXISTS `SHORT_CODE_HASH` CHAR(64) NULL AFTER `ACCESS_TOKEN_HASH`;

ALTER TABLE `CUSTOMER_ORDER_SESSIONS`
    ADD UNIQUE KEY `UX_CUSTOMER_ORDER_SESSIONS_SHORT_CODE` (`SHORT_CODE_HASH`);
