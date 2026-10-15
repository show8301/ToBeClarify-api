namespace ToBeClarify.Api.Repositories.Shared;

/// <summary>Duty plan alias P and Taiwan-local parameter DutyAt are required.</summary>
public static class StaffDutySql
{
    // Approval and the plan interval are the only authority for duty status.
    // End times at or before the start belong to the following calendar day.
    public const string IsOnDuty = """
        (COALESCE(P.`IS_WORKING`, FALSE)
         AND P.`APPROVAL_STATUS` = 'approved'
         AND P.`START_TIME` IS NOT NULL AND P.`END_TIME` IS NOT NULL
         AND @DutyAt >= TIMESTAMP(P.`BUSINESS_DATE`, P.`START_TIME`)
         AND @DutyAt < TIMESTAMP(P.`BUSINESS_DATE`, P.`END_TIME`)
             + INTERVAL (CASE WHEN P.`END_TIME` <= P.`START_TIME` THEN 1 ELSE 0 END) DAY)
        """;

    // Prefer the plan covering DutyAt, even if an open business period points to
    // another date. Explicit date catalogs remain scoped to their requested day.
    public const string PlanJoin = $"""
        LEFT JOIN `STAFF_DUTY_PLANS` P ON P.`ID` = (
            SELECT P.`ID` FROM `STAFF_DUTY_PLANS` P
            WHERE P.`STAFF_MEMBER_ID` = M.`ID` AND P.`APPROVAL_STATUS` = 'approved'
              AND (P.`BUSINESS_DATE` = @BusinessDate OR (@UseCurrentPlan = TRUE AND {IsOnDuty}))
            ORDER BY {IsOnDuty} DESC, P.`BUSINESS_DATE` DESC LIMIT 1)
        """;
}
