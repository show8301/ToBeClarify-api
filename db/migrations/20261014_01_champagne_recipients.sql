-- Apply before releasing the champagne recipient API. Existing orders retain legacy accounting.
-- MariaDB additive migration; no recipient or category is inferred for historical orders.
ALTER TABLE `ORDER_ITEMS`
  ADD COLUMN IF NOT EXISTS `IS_CHAMPAGNE_TOWER` BOOLEAN NOT NULL DEFAULT FALSE,
  ADD COLUMN IF NOT EXISTS `RECIPIENT_STAFF_ID` VARCHAR(40) NULL,
  ADD COLUMN IF NOT EXISTS `RECIPIENT_STAFF_NAME_SNAPSHOT` VARCHAR(200) NULL;
