-- Remove the deprecated ADMIN_USERS.DISPLAY_NAME column.
-- Target: MariaDB database `tobeclarify`.
--
-- Apply only after every API instance has been replaced by a build that reads
-- display names from STAFF_MEMBERS and no longer writes this column.

-- Pre-deployment verification: this query must return zero rows.
SELECT U.`ID`, U.`LOGIN_NAME`, U.`STAFF_MEMBER_ID`
FROM `ADMIN_USERS` U
LEFT JOIN `STAFF_MEMBERS` S ON S.`ID` = U.`STAFF_MEMBER_ID`
WHERE U.`STAFF_MEMBER_ID` IS NULL OR S.`ID` IS NULL;

ALTER TABLE `ADMIN_USERS`
    DROP COLUMN IF EXISTS `DISPLAY_NAME`;

-- Post-deployment verification:
-- SELECT U.ID, U.LOGIN_NAME, S.DISPLAY_NAME
-- FROM ADMIN_USERS U
-- INNER JOIN STAFF_MEMBERS S ON S.ID = U.STAFF_MEMBER_ID
-- ORDER BY S.DISPLAY_NAME, U.LOGIN_NAME;

-- Rollback requires a separately approved DBA migration because restoring the
-- removed column would recreate duplicated data.
