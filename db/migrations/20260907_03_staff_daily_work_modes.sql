-- Daily operational identities are separate from the public nomination flag.
-- This allows one staff member to be scheduled as service, designated and/or
-- backstage staff, then activate the roles actually being handled today.
-- Apply before deploying the API build that reads STAFF_DAILY_WORK_MODES.

CREATE TABLE IF NOT EXISTS `STAFF_DAILY_WORK_MODES` (
    `ID` VARCHAR(36) NOT NULL,
    `STAFF_MEMBER_ID` VARCHAR(36) NOT NULL,
    `BUSINESS_DATE` DATE NOT NULL,
    `IS_WORKING` TINYINT(1) NOT NULL DEFAULT 1,
    `SCHEDULED_ROLES_JSON` LONGTEXT NOT NULL,
    `ACTIVE_ROLES_JSON` LONGTEXT NOT NULL,
    `STARTED_AT` DATETIME NULL,
    `CREATED_AT` DATETIME NOT NULL,
    `CREATED_BY` VARCHAR(36) NULL,
    `UPDATED_AT` DATETIME NOT NULL,
    `UPDATED_BY` VARCHAR(36) NULL,
    PRIMARY KEY (`ID`),
    UNIQUE KEY `UX_STAFF_DAILY_WORK_MODES_STAFF_DATE` (`STAFF_MEMBER_ID`, `BUSINESS_DATE`),
    KEY `IX_STAFF_DAILY_WORK_MODES_DATE_STATUS` (`BUSINESS_DATE`, `IS_WORKING`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Seed today's row from the existing schedule and public nomination setting.
-- `IS_NOMINATABLE` is only a compatibility default; it is not the source of
-- truth after a daily work mode has been configured.
INSERT INTO `STAFF_DAILY_WORK_MODES`
    (`ID`, `STAFF_MEMBER_ID`, `BUSINESS_DATE`, `IS_WORKING`, `SCHEDULED_ROLES_JSON`, `ACTIVE_ROLES_JSON`, `CREATED_AT`, `UPDATED_AT`)
SELECT UUID(), M.`ID`, CURRENT_DATE(), COALESCE(S.`IS_WORKING`, TRUE),
       CASE WHEN M.`IS_NOMINATABLE` = TRUE THEN '["service","designated"]' ELSE '["service"]' END,
       CASE WHEN COALESCE(S.`IS_WORKING`, TRUE) = TRUE
            THEN CASE WHEN M.`IS_NOMINATABLE` = TRUE THEN '["service","designated"]' ELSE '["service"]' END
            ELSE '[]' END,
       NOW(), NOW()
FROM `STAFF_MEMBERS` M
LEFT JOIN `STAFF_SCHEDULES` S
    ON S.`STAFF_ID` = M.`ID` AND S.`WORK_DATE` = CURRENT_DATE()
WHERE NOT EXISTS (
    SELECT 1 FROM `STAFF_DAILY_WORK_MODES` D
    WHERE D.`STAFF_MEMBER_ID` = M.`ID` AND D.`BUSINESS_DATE` = CURRENT_DATE()
);

