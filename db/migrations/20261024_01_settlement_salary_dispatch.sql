-- Manual salary-dispatch markers only. No payment or correction ledger changes.
-- Additive and rerunnable; existing settlements start with an unmarked status.
CREATE TABLE IF NOT EXISTS `SETTLEMENT_SALARY_DISPATCH` (
    `SETTLEMENT_ID` VARCHAR(36) NOT NULL,
    `STAFF_ID` VARCHAR(36) NOT NULL,
    `IS_PAID` TINYINT(1) NOT NULL DEFAULT 0,
    `VERSION` INT NOT NULL DEFAULT 0,
    `UPDATED_AT` DATETIME(6) NOT NULL,
    `UPDATED_BY` VARCHAR(36) NOT NULL,
    PRIMARY KEY (`SETTLEMENT_ID`, `STAFF_ID`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
