-- Additive MariaDB migration. Apply before the matching API release.
-- Workbench preference belongs to the account, never to a business date.
ALTER TABLE `ADMIN_USERS`
  ADD COLUMN IF NOT EXISTS `PREFERRED_WORKBENCH` VARCHAR(16) NULL;

-- Payroll assignment is independent of planned/current operational support roles.
-- Settlement consumption is a later phase; legacy role arrays remain unchanged.
ALTER TABLE `STAFF_DUTY_PLANS`
  ADD COLUMN IF NOT EXISTS `PAYROLL_ROLE` VARCHAR(16) NULL;

-- Infer only an unambiguous legacy assignment. Backstage may be a separate share.
-- Rerunning never replaces a manager-confirmed assignment.
UPDATE `STAFF_DUTY_PLANS`
SET `PAYROLL_ROLE` = CASE
    WHEN JSON_VALID(`SCHEDULED_ROLES_JSON`) = FALSE THEN NULL
    WHEN JSON_TYPE(`SCHEDULED_ROLES_JSON`) <> 'ARRAY' THEN NULL
    WHEN JSON_LENGTH(`SCHEDULED_ROLES_JSON`) = 1
         AND JSON_UNQUOTE(JSON_EXTRACT(`SCHEDULED_ROLES_JSON`, '$[0]')) IN ('service', 'designated', 'backstage')
      THEN JSON_UNQUOTE(JSON_EXTRACT(`SCHEDULED_ROLES_JSON`, '$[0]'))
    WHEN JSON_LENGTH(`SCHEDULED_ROLES_JSON`) = 2
         AND JSON_CONTAINS(`SCHEDULED_ROLES_JSON`, '"backstage"')
         AND JSON_CONTAINS(`SCHEDULED_ROLES_JSON`, '"service"') THEN 'service'
    WHEN JSON_LENGTH(`SCHEDULED_ROLES_JSON`) = 2
         AND JSON_CONTAINS(`SCHEDULED_ROLES_JSON`, '"backstage"')
         AND JSON_CONTAINS(`SCHEDULED_ROLES_JSON`, '"designated"') THEN 'designated'
    ELSE NULL END
WHERE `PAYROLL_ROLE` IS NULL AND `IS_WORKING` = TRUE;
