-- Enforce the one-to-one relationship between admin accounts and staff members.
-- Target: MariaDB database `tobeclarify`.
--
-- Preconditions:
-- - Every ADMIN_USERS row has a STAFF_MEMBER_ID.
-- - Every STAFF_MEMBER_ID references an existing STAFF_MEMBERS row.
-- - No two admin accounts reference the same staff member.

-- All three values must be zero before applying the ALTER TABLE below.
SELECT
    SUM(U.`STAFF_MEMBER_ID` IS NULL) AS `NULL_LINKS`,
    SUM(U.`STAFF_MEMBER_ID` IS NOT NULL AND S.`ID` IS NULL) AS `ORPHAN_LINKS`,
    (
        SELECT COUNT(*)
        FROM (
            SELECT `STAFF_MEMBER_ID`
            FROM `ADMIN_USERS`
            WHERE `STAFF_MEMBER_ID` IS NOT NULL
            GROUP BY `STAFF_MEMBER_ID`
            HAVING COUNT(*) > 1
        ) D
    ) AS `DUPLICATE_LINKS`
FROM `ADMIN_USERS` U
LEFT JOIN `STAFF_MEMBERS` S ON S.`ID` = U.`STAFF_MEMBER_ID`;

ALTER TABLE `ADMIN_USERS`
    DROP INDEX `IX_ADMIN_USERS_STAFF`,
    MODIFY COLUMN `STAFF_MEMBER_ID` VARCHAR(40) NOT NULL,
    ADD CONSTRAINT `UQ_ADMIN_USERS_STAFF_MEMBER`
        UNIQUE (`STAFF_MEMBER_ID`),
    ADD CONSTRAINT `FK_ADMIN_USERS_STAFF_MEMBER`
        FOREIGN KEY (`STAFF_MEMBER_ID`)
        REFERENCES `STAFF_MEMBERS` (`ID`)
        ON UPDATE RESTRICT
        ON DELETE RESTRICT;

-- Verification query:
-- SELECT K.CONSTRAINT_NAME, K.COLUMN_NAME, K.REFERENCED_TABLE_NAME,
--        K.REFERENCED_COLUMN_NAME, R.UPDATE_RULE, R.DELETE_RULE
-- FROM information_schema.KEY_COLUMN_USAGE K
-- INNER JOIN information_schema.REFERENTIAL_CONSTRAINTS R
--   ON R.CONSTRAINT_SCHEMA = K.CONSTRAINT_SCHEMA
--  AND R.CONSTRAINT_NAME = K.CONSTRAINT_NAME
-- WHERE K.TABLE_SCHEMA = DATABASE()
--   AND K.TABLE_NAME = 'ADMIN_USERS'
--   AND K.COLUMN_NAME = 'STAFF_MEMBER_ID';

-- Rollback requires a separately approved migration. Removing these constraints
-- would allow orphaned, duplicate, or unlinked admin accounts again.
