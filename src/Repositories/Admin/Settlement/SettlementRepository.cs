using System.Data;
using Dapper;
using ToBeClarify.Api.Infrastructure;
using ToBeClarify.Api.Models.Entities;
using ToBeClarify.Api.Repositories.Shared;

namespace ToBeClarify.Api.Repositories.Admin.Settlement;

public sealed class SettlementRepository : DapperRepositoryBase, ISettlementRepository
{
    public SettlementRepository(AppDbContext dbContext) : base(dbContext) { }

    public async Task<bool> StaffMembersExistAsync(IReadOnlyList<string> staffIds, CancellationToken cancellationToken)
    {
        if (staffIds.Count == 0) return true;
        await using var connection = await DbContext.CreateOpenConnectionAsync(cancellationToken);
        var count = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM `STAFF_MEMBERS` WHERE `ID` IN @StaffIds;",
            new { StaffIds = staffIds }, cancellationToken: cancellationToken));
        return count == staffIds.Count;
    }

    public Task<IReadOnlyList<SettlementRuleRow>> GetRulesAsync(string? dayType, CancellationToken cancellationToken)
        => QueryAsync<SettlementRuleRow>("""
            SELECT `ID` AS Id, `DAY_TYPE` AS DayType, `EFFECTIVE_FROM` AS EffectiveFrom,
                   `DESIGNATED_HOURLY_RATE` AS DesignatedHourlyRate,
                   `SERVICE_MANAGER_HOURLY_RATE` AS ServiceManagerHourlyRate,
                   `BACKSTAGE_HOURLY_RATE` AS BackstageHourlyRate,
                   `DESIGNATED_SHARE_PERCENTAGE` AS DesignatedSharePercentage,
                   `PUBLIC_ROOM_STAFF_PERCENTAGE` AS PublicRoomStaffPercentage,
                   `DEDICATED_ROOM_OWNER_PERCENTAGE` AS DedicatedRoomOwnerPercentage,
                   `SERVICE_MANAGER_POOL_PERCENTAGE` AS ServiceManagerPoolPercentage,
                   `BACKSTAGE_POOL_PERCENTAGE` AS BackstagePoolPercentage,
                   `COMPANY_PERCENTAGE` AS CompanyPercentage,
                   `TIME_ROUND_MINUTES` AS TimeRoundMinutes, `MONEY_ROUND_UNIT` AS MoneyRoundUnit,
                   `PUBLIC_TIP_MODE` AS PublicTipMode
            FROM `SETTLEMENT_RULE_VERSIONS`
            WHERE (@DayType IS NULL OR `DAY_TYPE` = @DayType)
            ORDER BY `DAY_TYPE`, `EFFECTIVE_FROM` DESC;
            """, new { DayType = dayType }, cancellationToken);

    public Task<SettlementRuleRow?> GetEffectiveRuleAsync(string dayType, DateOnly businessDate,
        CancellationToken cancellationToken)
        => QuerySingleOrDefaultAsync<SettlementRuleRow>("""
            SELECT `ID` AS Id, `DAY_TYPE` AS DayType, `EFFECTIVE_FROM` AS EffectiveFrom,
                   `DESIGNATED_HOURLY_RATE` AS DesignatedHourlyRate,
                   `SERVICE_MANAGER_HOURLY_RATE` AS ServiceManagerHourlyRate,
                   `BACKSTAGE_HOURLY_RATE` AS BackstageHourlyRate,
                   `DESIGNATED_SHARE_PERCENTAGE` AS DesignatedSharePercentage,
                   `PUBLIC_ROOM_STAFF_PERCENTAGE` AS PublicRoomStaffPercentage,
                   `DEDICATED_ROOM_OWNER_PERCENTAGE` AS DedicatedRoomOwnerPercentage,
                   `SERVICE_MANAGER_POOL_PERCENTAGE` AS ServiceManagerPoolPercentage,
                   `BACKSTAGE_POOL_PERCENTAGE` AS BackstagePoolPercentage,
                   `COMPANY_PERCENTAGE` AS CompanyPercentage,
                   `TIME_ROUND_MINUTES` AS TimeRoundMinutes, `MONEY_ROUND_UNIT` AS MoneyRoundUnit,
                   `PUBLIC_TIP_MODE` AS PublicTipMode
            FROM `SETTLEMENT_RULE_VERSIONS`
            WHERE `DAY_TYPE` = @DayType AND `EFFECTIVE_FROM` <= @BusinessDate
            ORDER BY `EFFECTIVE_FROM` DESC LIMIT 1;
            """, new { DayType = dayType, BusinessDate = businessDate.ToDateTime(TimeOnly.MinValue) }, cancellationToken);

    public async Task InsertRuleAsync(SettlementRuleRow rule, string actorId, DateTime now,
        CancellationToken cancellationToken)
    {
        await using var connection = await DbContext.CreateOpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO `SETTLEMENT_RULE_VERSIONS`
                (`ID`, `DAY_TYPE`, `EFFECTIVE_FROM`, `DESIGNATED_HOURLY_RATE`, `SERVICE_MANAGER_HOURLY_RATE`,
                 `BACKSTAGE_HOURLY_RATE`, `DESIGNATED_SHARE_PERCENTAGE`, `PUBLIC_ROOM_STAFF_PERCENTAGE`,
                 `DEDICATED_ROOM_OWNER_PERCENTAGE`, `SERVICE_MANAGER_POOL_PERCENTAGE`, `BACKSTAGE_POOL_PERCENTAGE`,
                 `COMPANY_PERCENTAGE`, `TIME_ROUND_MINUTES`, `MONEY_ROUND_UNIT`, `PUBLIC_TIP_MODE`,
                 `CREATED_AT`, `CREATED_BY`)
            VALUES (@Id, @DayType, @EffectiveFrom, @DesignatedHourlyRate, @ServiceManagerHourlyRate,
                    @BackstageHourlyRate, @DesignatedSharePercentage, @PublicRoomStaffPercentage,
                    @DedicatedRoomOwnerPercentage, @ServiceManagerPoolPercentage, @BackstagePoolPercentage,
                    @CompanyPercentage, @TimeRoundMinutes, @MoneyRoundUnit, @PublicTipMode, @Now, @ActorId);
            """, new { rule.Id, rule.DayType, EffectiveFrom = rule.EffectiveFrom.Date,
                rule.DesignatedHourlyRate, rule.ServiceManagerHourlyRate, rule.BackstageHourlyRate,
                rule.DesignatedSharePercentage, rule.PublicRoomStaffPercentage, rule.DedicatedRoomOwnerPercentage,
                rule.ServiceManagerPoolPercentage, rule.BackstagePoolPercentage, rule.CompanyPercentage,
                rule.TimeRoundMinutes, rule.MoneyRoundUnit, rule.PublicTipMode, Now = now, ActorId = actorId },
            cancellationToken: cancellationToken));
    }

    public Task<SettlementRunRow?> GetRunAsync(DateOnly businessDate, int sessionNo,
        CancellationToken cancellationToken)
        => QuerySingleOrDefaultAsync<SettlementRunRow>(RunSelectSql + " WHERE `BUSINESS_DATE` = @BusinessDate AND `SESSION_NO` = @SessionNo LIMIT 1;",
            new { BusinessDate = businessDate.ToDateTime(TimeOnly.MinValue), SessionNo = sessionNo }, cancellationToken);

    public async Task<SettlementRunRow> GetOrCreateRunAsync(DateOnly businessDate, int sessionNo, string dayType,
        string? ruleVersionId, string actorId, DateTime now, CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid().ToString("D");
        await using var connection = await DbContext.CreateOpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO `SETTLEMENT_RUNS`
                (`ID`, `BUSINESS_DATE`, `SESSION_NO`, `DAY_TYPE`, `STATUS`, `RULE_VERSION_ID`,
                 `CREATED_AT`, `CREATED_BY`, `UPDATED_AT`, `UPDATED_BY`)
            VALUES (@Id, @BusinessDate, @SessionNo, @DayType, 'draft', @RuleVersionId,
                    @Now, @ActorId, @Now, @ActorId)
            ON DUPLICATE KEY UPDATE `ID` = `ID`;
            """, new { Id = id, BusinessDate = businessDate.ToDateTime(TimeOnly.MinValue), SessionNo = sessionNo,
                DayType = dayType, RuleVersionId = ruleVersionId, Now = now, ActorId = actorId },
            cancellationToken: cancellationToken));
        return await GetRunAsync(businessDate, sessionNo, cancellationToken)
            ?? throw new InvalidOperationException("Settlement run could not be created.");
    }

    public async Task<SettlementSourceData> GetSourceDataAsync(string settlementId, DateOnly businessDate,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT O.`ID` AS OrderId, O.`TOTAL_AMOUNT` AS TotalAmount, O.`SUBTOTAL` AS Subtotal,
                   COALESCE(SUM(T.`TIP_AMOUNT`), 0) AS TipAmount, A.`ADJUSTED_AMOUNT` AS AdjustedAmount
            FROM `ORDERS` O
            INNER JOIN `CUSTOMER_ORDER_SESSIONS` S ON S.`ID` = O.`SESSION_ID`
            LEFT JOIN `ORDER_TIPS` T ON T.`ORDER_ID` = O.`ID`
            LEFT JOIN `SETTLEMENT_ORDER_ADJUSTMENTS` A ON A.`SETTLEMENT_ID` = @SettlementId
                AND A.`SOURCE_TYPE` = 'order' AND A.`SOURCE_ID` = O.`ID`
            WHERE S.`BUSINESS_DATE` = @BusinessDate
              AND O.`ORDER_STATUS` NOT IN ('cancelled', 'expired', 'rejected')
            GROUP BY O.`ID`, O.`TOTAL_AMOUNT`, O.`SUBTOTAL`, A.`ADJUSTED_AMOUNT`;

            SELECT O.`ID` AS OrderId, I.`ID` AS ItemId, I.`ITEM_TYPE` AS ItemType,
                   I.`REFERENCE_ID` AS ReferenceId, I.`PARENT_ITEM_ID` AS ParentItemId,
                   I.`LINE_TOTAL` AS LineTotal
            FROM `ORDERS` O
            INNER JOIN `CUSTOMER_ORDER_SESSIONS` S ON S.`ID` = O.`SESSION_ID`
            INNER JOIN `ORDER_ITEMS` I ON I.`ORDER_ID` = O.`ID`
            WHERE S.`BUSINESS_DATE` = @BusinessDate
              AND O.`ORDER_STATUS` NOT IN ('cancelled', 'expired', 'rejected');

            SELECT N.`ORDER_ID` AS OrderId, N.`STAFF_ID` AS StaffId,
                   N.`STAFF_NAME_SNAPSHOT` AS StaffName
            FROM `ORDER_NOMINEES` N
            INNER JOIN `ORDERS` O ON O.`ID` = N.`ORDER_ID`
            INNER JOIN `CUSTOMER_ORDER_SESSIONS` S ON S.`ID` = O.`SESSION_ID`
            WHERE S.`BUSINESS_DATE` = @BusinessDate
              AND O.`ORDER_STATUS` NOT IN ('cancelled', 'expired', 'rejected');

            SELECT T.`ORDER_ID` AS OrderId, T.`STAFF_ID` AS StaffId,
                   T.`STAFF_AMOUNT` AS StaffAmount, T.`TIP_AMOUNT` AS TipAmount
            FROM `ORDER_TIPS` T
            INNER JOIN `ORDERS` O ON O.`ID` = T.`ORDER_ID`
            INNER JOIN `CUSTOMER_ORDER_SESSIONS` S ON S.`ID` = O.`SESSION_ID`
            WHERE S.`BUSINESS_DATE` = @BusinessDate
              AND O.`ORDER_STATUS` NOT IN ('cancelled', 'expired', 'rejected');

            SELECT R.`ID` AS Id, R.`ORDER_ID` AS OrderId, R.`ORDER_ITEM_ID` AS OrderItemId,
                   R.`ROOM_ID` AS RoomId, R.`ROOM_NAME_SNAPSHOT` AS RoomName,
                   RM.`OWNER_STAFF_ID` AS OwnerStaffId, M.`DISPLAY_NAME` AS OwnerStaffName,
                   R.`TOTAL_AMOUNT` AS TotalAmount
            FROM `ROOM_SERVICE_ORDERS` R
            LEFT JOIN `ROOMS` RM ON RM.`ID` = R.`ROOM_ID`
            LEFT JOIN `STAFF_MEMBERS` M ON M.`ID` = RM.`OWNER_STAFF_ID`
            LEFT JOIN `ORDERS` O ON O.`ID` = R.`ORDER_ID`
            WHERE R.`BUSINESS_DATE` = @BusinessDate
              AND R.`ORDER_STATUS` <> 'cancelled'
              AND (R.`ORDER_ID` IS NULL OR O.`ORDER_STATUS` NOT IN ('cancelled', 'expired', 'rejected'));

            SELECT A.`ORDER_ID` AS OrderId, A.`STAFF_ID` AS StaffId,
                   COALESCE(SUM(I.`LINE_TOTAL`), 0) AS LineTotal
            FROM `ORDER_SERVICE_ADDONS` A
            INNER JOIN `ORDERS` O ON O.`ID` = A.`ORDER_ID`
            INNER JOIN `CUSTOMER_ORDER_SESSIONS` S ON S.`ID` = O.`SESSION_ID`
            LEFT JOIN `ORDER_ITEMS` I ON I.`ORDER_ID` = A.`ORDER_ID` AND I.`ITEM_TYPE` = 'staff_service_addon'
            WHERE S.`BUSINESS_DATE` = @BusinessDate
              AND O.`ORDER_STATUS` NOT IN ('cancelled', 'expired', 'rejected')
            GROUP BY A.`ORDER_ID`, A.`STAFF_ID`;

            SELECT COUNT(*) AS SessionCount,
                   (SELECT `PRICE_TEXT` FROM `PRICING_RULES`
                    WHERE `IS_ENABLED` = TRUE AND (`TITLE` LIKE '%入場%' OR `DESCRIPTION` LIKE '%入場%')
                    ORDER BY `SORT_ORDER`, `CREATED_AT` LIMIT 1) AS AdmissionFeeText
            FROM `CUSTOMER_ORDER_SESSIONS`
            WHERE `BUSINESS_DATE` = @BusinessDate;

            SELECT I.`ID` AS Id, I.`SETTLEMENT_ID` AS SettlementId, I.`STAFF_ID` AS StaffId,
                   I.`ROLE` AS Role, M.`DISPLAY_NAME` AS DisplayName, M.`ROLE_TITLE` AS RoleTitle,
                   M.`IS_ACTIVE` AS IsActive, COALESCE(S.`IS_WORKING`, FALSE) AS IsWorking,
                   I.`ACTUAL_MINUTES` AS ActualMinutes, I.`ACTIVITY_HOURS` AS ActivityHours,
                   I.`ATTENDANCE_SOURCE` AS AttendanceSource,
                   I.`ATTENDANCE_REQUEST_ID` AS AttendanceRequestId,
                   I.`ATTENDANCE_APPROVED_BY` AS AttendanceApprovedBy,
                   I.`ATTENDANCE_APPROVED_AT` AS AttendanceApprovedAt,
                   I.`PUBLIC_TIP_ELIGIBLE` AS PublicTipEligible,
                   I.`IS_BACKSTAGE_PARTICIPANT` AS IsBackstageParticipant, I.`NOTE` AS Note
            FROM `SETTLEMENT_STAFF_INPUTS` I
            INNER JOIN `STAFF_MEMBERS` M ON M.`ID` = I.`STAFF_ID`
            LEFT JOIN `STAFF_SCHEDULES` S ON S.`STAFF_ID` = I.`STAFF_ID` AND S.`WORK_DATE` = @BusinessDate
            WHERE I.`SETTLEMENT_ID` = @SettlementId;

            SELECT R.`ID` AS Id, R.`SETTLEMENT_ID` AS SettlementId, R.`STAFF_ID` AS StaffId,
                   R.`ROLE` AS Role, M.`DISPLAY_NAME` AS DisplayName, R.`PAYABLE_HOURS` AS PayableHours,
                   R.`HOURLY_RATE` AS HourlyRate, R.`BASE_PAY` AS BasePay, R.`REVENUE_BASE` AS RevenueBase,
                   R.`REVENUE_PERCENTAGE` AS RevenuePercentage, R.`REVENUE_SHARE` AS RevenueShare,
                   R.`DESIGNATED_TIP` AS DesignatedTip, R.`PUBLIC_TIP` AS PublicTip,
                   R.`PREFERRED_PAY` AS PreferredPay, R.`BEFORE_ROUNDING` AS BeforeRounding,
                   R.`AFTER_ROUNDING` AS AfterRounding, R.`COMPANY_SUBSIDY` AS CompanySubsidy,
                   R.`MANUAL_ADJUSTMENT` AS ManualAdjustment, R.`ANOMALY_STATUS` AS AnomalyStatus,
                   R.`ANOMALY_NOTE` AS AnomalyNote
            FROM `SETTLEMENT_RESULT_LINES` R
            LEFT JOIN `STAFF_MEMBERS` M ON M.`ID` = R.`STAFF_ID`
            WHERE R.`SETTLEMENT_ID` = @SettlementId
            ORDER BY R.`ROLE`, M.`DISPLAY_NAME`;

            SELECT B.`ID` AS Id, B.`SETTLEMENT_ID` AS SettlementId, B.`STAFF_ID` AS StaffId,
                   M.`DISPLAY_NAME` AS StaffName, B.`ROLE` AS Role,
                   B.`REQUESTED_MINUTES` AS RequestedMinutes, B.`REASON` AS Reason,
                   B.`STATUS` AS Status, B.`REQUESTED_BY` AS RequestedBy,
                   B.`REQUESTED_AT` AS RequestedAt, B.`REVIEWED_BY` AS ReviewedBy,
                   B.`REVIEWED_AT` AS ReviewedAt, B.`REVIEW_NOTE` AS ReviewNote
            FROM `SETTLEMENT_ATTENDANCE_BACKFILL_REQUESTS` B
            LEFT JOIN `STAFF_MEMBERS` M ON M.`ID` = B.`STAFF_ID`
            WHERE B.`SETTLEMENT_ID` = @SettlementId
            ORDER BY B.`REQUESTED_AT` DESC;
            """;

        await using var connection = await DbContext.CreateOpenConnectionAsync(cancellationToken);
        using var multi = await connection.QueryMultipleAsync(new CommandDefinition(sql,
            new { SettlementId = settlementId, BusinessDate = businessDate.ToDateTime(TimeOnly.MinValue) },
            cancellationToken: cancellationToken));
        var orders = (await multi.ReadAsync<SettlementOrderRevenueRow>()).AsList();
        var items = (await multi.ReadAsync<SettlementOrderItemRevenueRow>()).AsList();
        var nominees = (await multi.ReadAsync<SettlementNomineeRevenueRow>()).AsList();
        var tips = (await multi.ReadAsync<SettlementTipRevenueRow>()).AsList();
        var rooms = (await multi.ReadAsync<SettlementRoomRevenueRow>()).AsList();
        var addons = (await multi.ReadAsync<SettlementAddonRevenueRow>()).AsList();
        var admissions = (await multi.ReadAsync<SettlementAdmissionRow>()).AsList();
        var staffInputs = (await multi.ReadAsync<SettlementStaffInputRow>()).AsList();
        var results = (await multi.ReadAsync<SettlementResultLineRow>()).AsList();
        var attendanceBackfillRequests = (await multi.ReadAsync<SettlementAttendanceBackfillRow>()).AsList();
        return new SettlementSourceData { Orders = orders, Items = items, Nominees = nominees, Tips = tips,
            Rooms = rooms, Addons = addons, Admissions = admissions, StaffInputs = staffInputs, Results = results,
            AttendanceBackfillRequests = attendanceBackfillRequests };
    }

    public async Task SaveInputsAsync(string settlementId, SaveInputsData inputs, string actorId, DateTime now,
        CancellationToken cancellationToken)
    {
        await using var connection = await DbContext.CreateOpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE `SETTLEMENT_RUNS`
            SET `DAY_TYPE` = COALESCE(@DayType, `DAY_TYPE`), `RULE_VERSION_ID` = COALESCE(@RuleVersionId, `RULE_VERSION_ID`),
                `PUBLIC_TIP_AMOUNT` = @PublicTipAmount, `ADMISSION_FEE_OVERRIDE` = @AdmissionFeeOverride,
                `ACTIVITY_EXPENSE` = @ActivityExpense, `COMPANY_SHARE_HOURS` = @CompanyShareHours,
                `ACTIVITY_HOURS_CONFIRMED` = @ActivityHoursConfirmed, `UPDATED_AT` = @Now, `UPDATED_BY` = @ActorId
            WHERE `ID` = @SettlementId;
            """, new { SettlementId = settlementId, inputs.DayType, inputs.RuleVersionId, inputs.PublicTipAmount, inputs.AdmissionFeeOverride,
                inputs.ActivityExpense, inputs.CompanyShareHours, inputs.ActivityHoursConfirmed,
                Now = now, ActorId = actorId }, transaction, cancellationToken: cancellationToken));

        foreach (var input in inputs.StaffInputs)
        {
            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO `SETTLEMENT_STAFF_INPUTS`
                    (`ID`, `SETTLEMENT_ID`, `STAFF_ID`, `ROLE`, `ACTUAL_MINUTES`, `ACTIVITY_HOURS`,
                     `ATTENDANCE_SOURCE`, `PUBLIC_TIP_ELIGIBLE`, `IS_BACKSTAGE_PARTICIPANT`, `NOTE`, `CREATED_AT`, `CREATED_BY`,
                     `UPDATED_AT`, `UPDATED_BY`)
                VALUES (@Id, @SettlementId, @StaffId, @Role, @ActualMinutes, @ActivityHours,
                        @AttendanceSource, @PublicTipEligible, @IsBackstageParticipant, @Note, @Now, @ActorId, @Now, @ActorId)
                ON DUPLICATE KEY UPDATE `ACTUAL_MINUTES` = VALUES(`ACTUAL_MINUTES`),
                    `ACTIVITY_HOURS` = VALUES(`ACTIVITY_HOURS`),
                    `ATTENDANCE_SOURCE` = VALUES(`ATTENDANCE_SOURCE`),
                    `PUBLIC_TIP_ELIGIBLE` = VALUES(`PUBLIC_TIP_ELIGIBLE`),
                    `IS_BACKSTAGE_PARTICIPANT` = VALUES(`IS_BACKSTAGE_PARTICIPANT`),
                    `NOTE` = VALUES(`NOTE`), `UPDATED_AT` = @Now, `UPDATED_BY` = @ActorId;
                """, new { Id = Guid.NewGuid().ToString("D"), SettlementId = settlementId, input.StaffId,
                    input.Role, input.ActualMinutes, input.ActivityHours, input.PublicTipEligible,
                    input.IsBackstageParticipant, input.AttendanceSource, input.Note, Now = now, ActorId = actorId },
                transaction, cancellationToken: cancellationToken));
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task SaveCalculationAsync(string settlementId, SaveCalculationData calculation, string actorId,
        DateTime now, CancellationToken cancellationToken)
    {
        await using var connection = await DbContext.CreateOpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE `SETTLEMENT_RESULT_LINES`
            SET `ANOMALY_STATUS` = 'stale', `ANOMALY_NOTE` = 'superseded by a newer calculation'
            WHERE `SETTLEMENT_ID` = @SettlementId;
            """, new { SettlementId = settlementId }, transaction, cancellationToken: cancellationToken));
        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE `SETTLEMENT_RUNS`
            SET `RULE_VERSION_ID` = @RuleVersionId, `RULE_SNAPSHOT_JSON` = @RuleSnapshotJson,
                `ADMISSION_FEE_SNAPSHOT` = @AdmissionFeeSnapshot, `GROSS_REVENUE` = @GrossRevenue,
                `DESIGNATED_REVENUE_BASE` = @DesignatedRevenueBase, `DEDICATED_ROOM_GROSS` = @DedicatedRoomGross,
                `DEDICATED_ROOM_OWNER_SHARE` = @DedicatedRoomOwnerShare,
                `DEDICATED_ROOM_COMPANY_REMAINDER` = @DedicatedRoomCompanyRemainder,
                `PUBLIC_ROOM_UNASSIGNED_REVENUE` = @PublicRoomUnassignedRevenue,
                `ADMISSION_REVENUE` = @AdmissionRevenue, `MEAL_REVENUE` = @MealRevenue,
                `COMPANY_REVENUE` = @CompanyRevenue, `SERVICE_MANAGER_POOL` = @ServiceManagerPool,
                `BACKSTAGE_POOL` = @BackstagePool, `COMPANY_INCOME` = @CompanyIncome,
                `ACTIVITY_NET_REVENUE` = @ActivityNetRevenue, `STATUS` = 'calculated',
                `UPDATED_AT` = @Now, `UPDATED_BY` = @ActorId
            WHERE `ID` = @SettlementId;
            """, new { SettlementId = settlementId, calculation.Run.RuleVersionId,
                calculation.Run.RuleSnapshotJson, calculation.Run.AdmissionFeeSnapshot,
                calculation.Run.GrossRevenue, calculation.Run.DesignatedRevenueBase,
                calculation.Run.DedicatedRoomGross, calculation.Run.DedicatedRoomOwnerShare,
                calculation.Run.DedicatedRoomCompanyRemainder, calculation.Run.PublicRoomUnassignedRevenue,
                calculation.Run.AdmissionRevenue, calculation.Run.MealRevenue, calculation.Run.CompanyRevenue,
                calculation.Run.ServiceManagerPool, calculation.Run.BackstagePool, calculation.Run.CompanyIncome,
                calculation.Run.ActivityNetRevenue, Now = now, ActorId = actorId }, transaction,
            cancellationToken: cancellationToken));

        foreach (var result in calculation.Results)
        {
            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO `SETTLEMENT_RESULT_LINES`
                    (`ID`, `SETTLEMENT_ID`, `STAFF_ID`, `ROLE`, `PAYABLE_HOURS`, `HOURLY_RATE`, `BASE_PAY`,
                     `REVENUE_BASE`, `REVENUE_PERCENTAGE`, `REVENUE_SHARE`, `DESIGNATED_TIP`, `PUBLIC_TIP`,
                     `PREFERRED_PAY`, `BEFORE_ROUNDING`, `AFTER_ROUNDING`, `COMPANY_SUBSIDY`, `MANUAL_ADJUSTMENT`,
                     `ANOMALY_STATUS`, `ANOMALY_NOTE`, `CREATED_AT`)
                VALUES (@Id, @SettlementId, @StaffId, @Role, @PayableHours, @HourlyRate, @BasePay,
                        @RevenueBase, @RevenuePercentage, @RevenueShare, @DesignatedTip, @PublicTip,
                        @PreferredPay, @BeforeRounding, @AfterRounding, @CompanySubsidy, @ManualAdjustment,
                        @AnomalyStatus, @AnomalyNote, @Now)
                ON DUPLICATE KEY UPDATE `PAYABLE_HOURS` = VALUES(`PAYABLE_HOURS`), `HOURLY_RATE` = VALUES(`HOURLY_RATE`),
                    `BASE_PAY` = VALUES(`BASE_PAY`), `REVENUE_BASE` = VALUES(`REVENUE_BASE`),
                    `REVENUE_PERCENTAGE` = VALUES(`REVENUE_PERCENTAGE`), `REVENUE_SHARE` = VALUES(`REVENUE_SHARE`),
                    `DESIGNATED_TIP` = VALUES(`DESIGNATED_TIP`), `PUBLIC_TIP` = VALUES(`PUBLIC_TIP`),
                    `PREFERRED_PAY` = VALUES(`PREFERRED_PAY`), `BEFORE_ROUNDING` = VALUES(`BEFORE_ROUNDING`),
                    `AFTER_ROUNDING` = VALUES(`AFTER_ROUNDING`), `COMPANY_SUBSIDY` = VALUES(`COMPANY_SUBSIDY`),
                    `MANUAL_ADJUSTMENT` = VALUES(`MANUAL_ADJUSTMENT`), `ANOMALY_STATUS` = VALUES(`ANOMALY_STATUS`),
                    `ANOMALY_NOTE` = VALUES(`ANOMALY_NOTE`);
                """, new { Id = string.IsNullOrWhiteSpace(result.Id) ? Guid.NewGuid().ToString("D") : result.Id,
                    SettlementId = settlementId, result.StaffId, result.Role, result.PayableHours,
                    result.HourlyRate, result.BasePay, result.RevenueBase, result.RevenuePercentage,
                    result.RevenueShare, result.DesignatedTip, result.PublicTip, result.PreferredPay,
                    result.BeforeRounding, result.AfterRounding, result.CompanySubsidy, result.ManualAdjustment,
                    result.AnomalyStatus, result.AnomalyNote, Now = now }, transaction,
                cancellationToken: cancellationToken));
        }
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task FinalizeAsync(string settlementId, string actorId, DateTime now,
        CancellationToken cancellationToken)
    {
        await using var connection = await DbContext.CreateOpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE `SETTLEMENT_RUNS`
            SET `STATUS` = 'finalized', `FINALIZED_AT` = @Now, `FINALIZED_BY` = @ActorId,
                `UPDATED_AT` = @Now, `UPDATED_BY` = @ActorId
            WHERE `ID` = @SettlementId AND `STATUS` = 'calculated';
            """, new { SettlementId = settlementId, Now = now, ActorId = actorId },
            cancellationToken: cancellationToken));
    }

    public async Task<SettlementRunRow> ReopenAsync(DateOnly businessDate, int sessionNo, SettlementRuleRow rule,
        string reason, string actorId, DateTime now, CancellationToken cancellationToken)
    {
        await using var connection = await DbContext.CreateOpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var previous = await connection.QuerySingleOrDefaultAsync<SettlementRunRow>(new CommandDefinition(
            RunSelectSql + " WHERE `BUSINESS_DATE` = @BusinessDate AND `SESSION_NO` = @SessionNo LIMIT 1;",
            new { BusinessDate = businessDate.ToDateTime(TimeOnly.MinValue), SessionNo = sessionNo }, transaction,
            cancellationToken: cancellationToken));
        if (previous is null || previous.Status != "finalized")
            throw new InvalidOperationException("Only a finalized settlement can be reopened.");
        var nextNo = await connection.QuerySingleAsync<int>(new CommandDefinition(
            "SELECT COALESCE(MAX(`SESSION_NO`), 0) + 1 FROM `SETTLEMENT_RUNS` WHERE `BUSINESS_DATE` = @BusinessDate;",
            new { BusinessDate = businessDate.ToDateTime(TimeOnly.MinValue) }, transaction, cancellationToken: cancellationToken));
        var nextId = Guid.NewGuid().ToString("D");
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO `SETTLEMENT_RUNS`
                (`ID`, `BUSINESS_DATE`, `SESSION_NO`, `DAY_TYPE`, `STATUS`, `RULE_VERSION_ID`, `RULE_SNAPSHOT_JSON`, `CREATED_AT`,
                 `CREATED_BY`, `UPDATED_AT`, `UPDATED_BY`)
            VALUES (@Id, @BusinessDate, @SessionNo, @DayType, 'draft', @RuleVersionId, @RuleSnapshotJson,
                    @Now, @ActorId, @Now, @ActorId);
            INSERT INTO `SETTLEMENT_AUDIT_LOG`
                (`ID`, `SETTLEMENT_ID`, `ACTION_TYPE`, `BEFORE_JSON`, `AFTER_JSON`, `REASON`, `ACTOR_ID`, `CREATED_AT`)
            VALUES (@AuditId, @PreviousId, 'reopen', @BeforeJson, @AfterJson, @Reason, @ActorId, @Now);
            """, new { Id = nextId, BusinessDate = businessDate.ToDateTime(TimeOnly.MinValue), SessionNo = nextNo,
                previous.DayType, RuleVersionId = rule.Id, RuleSnapshotJson = System.Text.Json.JsonSerializer.Serialize(rule),
                Now = now, ActorId = actorId, AuditId = Guid.NewGuid().ToString("D"),
                PreviousId = previous.Id, BeforeJson = $"{{\"status\":\"{previous.Status}\",\"sessionNo\":{previous.SessionNo}}}",
                AfterJson = $"{{\"status\":\"draft\",\"sessionNo\":{nextNo}}}", Reason = reason }, transaction,
            cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken);
        return await GetRunAsync(businessDate, nextNo, cancellationToken)
            ?? throw new InvalidOperationException("Reopened settlement could not be read.");
    }

    public async Task<SettlementAttendanceBackfillRow> SubmitAttendanceBackfillAsync(string settlementId,
        string staffId, string role, int requestedMinutes, string reason, string actorId, DateTime now,
        CancellationToken cancellationToken)
    {
        await using var connection = await DbContext.CreateOpenConnectionAsync(cancellationToken);
        var id = Guid.NewGuid().ToString("D");
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO `SETTLEMENT_ATTENDANCE_BACKFILL_REQUESTS`
                (`ID`, `SETTLEMENT_ID`, `STAFF_ID`, `ROLE`, `REQUESTED_MINUTES`, `REASON`, `STATUS`,
                 `REQUESTED_BY`, `REQUESTED_AT`)
            VALUES (@Id, @SettlementId, @StaffId, @Role, @RequestedMinutes, @Reason, 'pending',
                    @ActorId, @Now);
            INSERT INTO `SETTLEMENT_AUDIT_LOG`
                (`ID`, `SETTLEMENT_ID`, `ACTION_TYPE`, `AFTER_JSON`, `REASON`, `ACTOR_ID`, `CREATED_AT`)
            VALUES (@AuditId, @SettlementId, 'attendance_backfill_requested', @AfterJson, @Reason, @ActorId, @Now);
            """, new { Id = id, SettlementId = settlementId, StaffId = staffId, Role = role,
                RequestedMinutes = requestedMinutes, Reason = reason, ActorId = actorId, Now = now,
                AuditId = Guid.NewGuid().ToString("D"), AfterJson = $"{{\"staffId\":\"{staffId}\",\"role\":\"{role}\",\"minutes\":{requestedMinutes}}}" },
            cancellationToken: cancellationToken));
        return await GetAttendanceBackfillAsync(id, cancellationToken)
            ?? throw new InvalidOperationException("Attendance backfill request could not be read.");
    }

    public async Task<SettlementAttendanceBackfillRow> ReviewAttendanceBackfillAsync(string requestId,
        bool approved, string? note, string actorId, DateTime now, CancellationToken cancellationToken)
    {
        await using var connection = await DbContext.CreateOpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var request = await connection.QuerySingleOrDefaultAsync<SettlementAttendanceBackfillRow>(new CommandDefinition("""
            SELECT B.`ID` AS Id, B.`SETTLEMENT_ID` AS SettlementId, B.`STAFF_ID` AS StaffId,
                   M.`DISPLAY_NAME` AS StaffName, B.`ROLE` AS Role, B.`REQUESTED_MINUTES` AS RequestedMinutes,
                   B.`REASON` AS Reason, B.`STATUS` AS Status, B.`REQUESTED_BY` AS RequestedBy,
                   B.`REQUESTED_AT` AS RequestedAt, B.`REVIEWED_BY` AS ReviewedBy,
                   B.`REVIEWED_AT` AS ReviewedAt, B.`REVIEW_NOTE` AS ReviewNote
            FROM `SETTLEMENT_ATTENDANCE_BACKFILL_REQUESTS` B
            LEFT JOIN `STAFF_MEMBERS` M ON M.`ID` = B.`STAFF_ID`
            WHERE B.`ID` = @RequestId LIMIT 1;
            """, new { RequestId = requestId }, transaction, cancellationToken: cancellationToken));
        if (request is null) throw new InvalidOperationException("Attendance backfill request not found.");
        if (request.Status != "pending") throw new InvalidOperationException("Attendance backfill request is already reviewed.");
        var status = approved ? "approved" : "rejected";
        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE `SETTLEMENT_ATTENDANCE_BACKFILL_REQUESTS`
            SET `STATUS` = @Status, `REVIEWED_BY` = @ActorId, `REVIEWED_AT` = @Now, `REVIEW_NOTE` = @Note
            WHERE `ID` = @RequestId AND `STATUS` = 'pending';
            """, new { RequestId = requestId, Status = status, ActorId = actorId, Now = now, Note = note },
            transaction, cancellationToken: cancellationToken));
        if (approved)
        {
            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO `SETTLEMENT_STAFF_INPUTS`
                    (`ID`, `SETTLEMENT_ID`, `STAFF_ID`, `ROLE`, `ACTUAL_MINUTES`, `ATTENDANCE_SOURCE`,
                     `ATTENDANCE_REQUEST_ID`, `ATTENDANCE_APPROVED_BY`, `ATTENDANCE_APPROVED_AT`,
                     `PUBLIC_TIP_ELIGIBLE`, `CREATED_AT`, `CREATED_BY`, `UPDATED_AT`, `UPDATED_BY`)
                VALUES (@InputId, @SettlementId, @StaffId, @Role, @RequestedMinutes, 'backfill_approved',
                        @RequestId, @ActorId, @Now, TRUE, @Now, @ActorId, @Now, @ActorId)
                ON DUPLICATE KEY UPDATE `ACTUAL_MINUTES` = VALUES(`ACTUAL_MINUTES`),
                    `ATTENDANCE_SOURCE` = 'backfill_approved', `ATTENDANCE_REQUEST_ID` = @RequestId,
                    `ATTENDANCE_APPROVED_BY` = @ActorId, `ATTENDANCE_APPROVED_AT` = @Now,
                    `UPDATED_AT` = @Now, `UPDATED_BY` = @ActorId;
                """, new { InputId = Guid.NewGuid().ToString("D"), request.SettlementId, request.StaffId,
                    request.Role, request.RequestedMinutes, RequestId = request.Id, ActorId = actorId, Now = now },
                transaction, cancellationToken: cancellationToken));
        }
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO `SETTLEMENT_AUDIT_LOG`
                (`ID`, `SETTLEMENT_ID`, `ACTION_TYPE`, `BEFORE_JSON`, `AFTER_JSON`, `REASON`, `ACTOR_ID`, `CREATED_AT`)
            VALUES (@AuditId, @SettlementId, 'attendance_backfill_reviewed', @BeforeJson, @AfterJson,
                    @Reason, @ActorId, @Now);
            """, new { AuditId = Guid.NewGuid().ToString("D"), request.SettlementId,
                BeforeJson = $"{{\"status\":\"pending\",\"requestId\":\"{request.Id}\"}}",
                AfterJson = $"{{\"status\":\"{status}\",\"approvedMinutes\":{(approved ? request.RequestedMinutes : 0)}}}",
                Reason = note, ActorId = actorId, Now = now }, transaction, cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken);
        return await GetAttendanceBackfillAsync(requestId, cancellationToken)
            ?? throw new InvalidOperationException("Reviewed attendance backfill request could not be read.");
    }

    private async Task<SettlementAttendanceBackfillRow?> GetAttendanceBackfillAsync(string requestId,
        CancellationToken cancellationToken)
        => await QuerySingleOrDefaultAsync<SettlementAttendanceBackfillRow>("""
            SELECT B.`ID` AS Id, B.`SETTLEMENT_ID` AS SettlementId, B.`STAFF_ID` AS StaffId,
                   M.`DISPLAY_NAME` AS StaffName, B.`ROLE` AS Role, B.`REQUESTED_MINUTES` AS RequestedMinutes,
                   B.`REASON` AS Reason, B.`STATUS` AS Status, B.`REQUESTED_BY` AS RequestedBy,
                   B.`REQUESTED_AT` AS RequestedAt, B.`REVIEWED_BY` AS ReviewedBy,
                   B.`REVIEWED_AT` AS ReviewedAt, B.`REVIEW_NOTE` AS ReviewNote
            FROM `SETTLEMENT_ATTENDANCE_BACKFILL_REQUESTS` B
            LEFT JOIN `STAFF_MEMBERS` M ON M.`ID` = B.`STAFF_ID`
            WHERE B.`ID` = @RequestId LIMIT 1;
            """, new { RequestId = requestId }, cancellationToken);

    public async Task SaveOrderAdjustmentAsync(string settlementId, string orderId, int adjustedAmount,
        string reason, string? note, string actorId, DateTime now, CancellationToken cancellationToken)
    {
        await using var connection = await DbContext.CreateOpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var previousAmount = await connection.QuerySingleOrDefaultAsync<int?>(new CommandDefinition("""
            SELECT COALESCE(A.`ADJUSTED_AMOUNT`, O.`TOTAL_AMOUNT`)
            FROM `ORDERS` O
            LEFT JOIN `SETTLEMENT_ORDER_ADJUSTMENTS` A ON A.`SETTLEMENT_ID` = @SettlementId
                AND A.`SOURCE_TYPE` = 'order' AND A.`SOURCE_ID` = O.`ID`
            WHERE O.`ID` = @OrderId LIMIT 1;
            """, new { SettlementId = settlementId, OrderId = orderId }, transaction,
            cancellationToken: cancellationToken));
        if (previousAmount is null) throw new InvalidOperationException("Order not found.");
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO `SETTLEMENT_ORDER_ADJUSTMENTS`
                (`ID`, `SETTLEMENT_ID`, `SOURCE_TYPE`, `SOURCE_ID`, `ORIGINAL_AMOUNT`, `ADJUSTED_AMOUNT`,
                 `REASON`, `NOTE`, `CREATED_AT`, `CREATED_BY`)
            SELECT @Id, @SettlementId, 'order', @OrderId, `TOTAL_AMOUNT`, @AdjustedAmount,
                   @Reason, @Note, @Now, @ActorId
            FROM `ORDERS` WHERE `ID` = @OrderId
            ON DUPLICATE KEY UPDATE `ADJUSTED_AMOUNT` = VALUES(`ADJUSTED_AMOUNT`), `REASON` = VALUES(`REASON`),
                `NOTE` = VALUES(`NOTE`), `CREATED_AT` = @Now, `CREATED_BY` = @ActorId;
            """, new { Id = Guid.NewGuid().ToString("D"), SettlementId = settlementId, OrderId = orderId,
                AdjustedAmount = adjustedAmount, Reason = reason, Note = note, Now = now, ActorId = actorId,
                OriginalAmount = previousAmount.Value }, transaction, cancellationToken: cancellationToken));
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO `SETTLEMENT_AUDIT_LOG`
                (`ID`, `SETTLEMENT_ID`, `ACTION_TYPE`, `BEFORE_JSON`, `AFTER_JSON`, `REASON`, `ACTOR_ID`, `CREATED_AT`)
            VALUES (@AuditId, @SettlementId, 'order_amount_adjustment', @BeforeJson, @AfterJson, @Reason, @ActorId, @Now);
            """, new { AuditId = Guid.NewGuid().ToString("D"), SettlementId = settlementId,
                BeforeJson = $"{{\"orderId\":\"{orderId}\",\"amount\":{previousAmount.Value}}}",
                AfterJson = $"{{\"orderId\":\"{orderId}\",\"amount\":{adjustedAmount}}}", Reason = reason,
                ActorId = actorId, Now = now }, transaction, cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<int?> GetOrderTotalAsync(string orderId, CancellationToken cancellationToken)
        => await QuerySingleOrDefaultAsync<int?>("SELECT `TOTAL_AMOUNT` FROM `ORDERS` WHERE `ID` = @OrderId LIMIT 1;",
            new { OrderId = orderId }, cancellationToken);

    private const string RunSelectSql = """
        SELECT `ID` AS Id, `BUSINESS_DATE` AS BusinessDate, `SESSION_NO` AS SessionNo,
               `DAY_TYPE` AS DayType, `STATUS` AS Status, `RULE_VERSION_ID` AS RuleVersionId,
               `RULE_SNAPSHOT_JSON` AS RuleSnapshotJson, `PUBLIC_TIP_AMOUNT` AS PublicTipAmount,
               `ADMISSION_FEE_OVERRIDE` AS AdmissionFeeOverride, `ADMISSION_FEE_SNAPSHOT` AS AdmissionFeeSnapshot,
               `ACTIVITY_EXPENSE` AS ActivityExpense, `COMPANY_SHARE_HOURS` AS CompanyShareHours,
               `ACTIVITY_HOURS_CONFIRMED` AS ActivityHoursConfirmed, `GROSS_REVENUE` AS GrossRevenue,
               `DESIGNATED_REVENUE_BASE` AS DesignatedRevenueBase, `DEDICATED_ROOM_GROSS` AS DedicatedRoomGross,
               `DEDICATED_ROOM_OWNER_SHARE` AS DedicatedRoomOwnerShare,
               `DEDICATED_ROOM_COMPANY_REMAINDER` AS DedicatedRoomCompanyRemainder,
               `PUBLIC_ROOM_UNASSIGNED_REVENUE` AS PublicRoomUnassignedRevenue,
               `ADMISSION_REVENUE` AS AdmissionRevenue, `MEAL_REVENUE` AS MealRevenue,
               `COMPANY_REVENUE` AS CompanyRevenue, `SERVICE_MANAGER_POOL` AS ServiceManagerPool,
               `BACKSTAGE_POOL` AS BackstagePool, `COMPANY_INCOME` AS CompanyIncome,
               `ACTIVITY_NET_REVENUE` AS ActivityNetRevenue, `FINALIZED_AT` AS FinalizedAt,
               `FINALIZED_BY` AS FinalizedBy
        FROM `SETTLEMENT_RUNS`
        """;
}
