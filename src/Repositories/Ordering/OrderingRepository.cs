using System.Text.Json;
using Dapper;
using MySqlConnector;
using ToBeClarify.Api.Exceptions;
using ToBeClarify.Api.Infrastructure;
using ToBeClarify.Api.Models.Entities;
using ToBeClarify.Api.Repositories.Shared;

namespace ToBeClarify.Api.Repositories.Ordering;

// Direct SQL maintenance account cannot delete rows or schema objects. API callers
// may use DeleteOrderItemAsync when the deployed API connection identity has the
// required privilege and the service/controller business checks have passed.
public sealed class OrderingRepository : DapperRepositoryBase, IOrderingRepository
{
    public OrderingRepository(AppDbContext dbContext) : base(dbContext) { }

    public async Task<OrderingSettingsRow> GetSettingsAsync(CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT `MINIMUM_MEAL_CREDIT` AS MinimumMealCredit, `BASE_NOMINATION_FEE` AS BaseNominationFee,
                   `TIP_PRESET_AMOUNT_1` AS TipPresetAmount1, `TIP_PRESET_AMOUNT_2` AS TipPresetAmount2,
                   `TIP_PRESET_AMOUNT_3` AS TipPresetAmount3, `TIP_PRESET_AMOUNT_4` AS TipPresetAmount4,
                   `SEGMENT_MINUTES` AS SegmentMinutes, `REMINDER_AFTER_MINUTES` AS ReminderAfterMinutes,
                   `ESCALATE_AFTER_MINUTES` AS EscalateAfterMinutes, `EXPIRE_AFTER_MINUTES` AS ExpireAfterMinutes,
                   `BUSINESS_DAY_START_MINUTE` AS BusinessDayStartMinute,
                   `BUSINESS_DAY_END_MINUTE` AS BusinessDayEndMinute,
                   `BUSINESS_DAY_ENDS_NEXT_DAY` AS BusinessDayEndsNextDay,
                   `NOMINATION_PAUSED_UNTIL` AS NominationPausedUntil
            FROM `ORDERING_SETTINGS` WHERE `ID` = 'default' LIMIT 1;
            """;
        return await QuerySingleOrDefaultAsync<OrderingSettingsRow>(sql, null, cancellationToken)
            ?? new OrderingSettingsRow
            {
                TipPresetAmount1 = 50, TipPresetAmount2 = 100, TipPresetAmount3 = 200, TipPresetAmount4 = 500,
                SegmentMinutes = 20, ReminderAfterMinutes = 5, EscalateAfterMinutes = 10, ExpireAfterMinutes = 20
            };
    }

    public async Task SaveSettingsAsync(OrderingSettingsRow settings, string actorId, DateTime now, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO `ORDERING_SETTINGS`
                (`ID`, `MINIMUM_MEAL_CREDIT`, `BASE_NOMINATION_FEE`, `TIP_PRESET_AMOUNT_1`, `TIP_PRESET_AMOUNT_2`,
                 `TIP_PRESET_AMOUNT_3`, `TIP_PRESET_AMOUNT_4`, `SEGMENT_MINUTES`,
                 `REMINDER_AFTER_MINUTES`, `ESCALATE_AFTER_MINUTES`, `EXPIRE_AFTER_MINUTES`,
                 `BUSINESS_DAY_START_MINUTE`, `BUSINESS_DAY_END_MINUTE`, `BUSINESS_DAY_ENDS_NEXT_DAY`,
                 `UPDATED_AT`, `UPDATED_BY`)
            VALUES ('default', @MinimumMealCredit, @BaseNominationFee, @TipPresetAmount1, @TipPresetAmount2,
                    @TipPresetAmount3, @TipPresetAmount4, @SegmentMinutes,
                    @ReminderAfterMinutes, @EscalateAfterMinutes, @ExpireAfterMinutes,
                    @BusinessDayStartMinute, @BusinessDayEndMinute, @BusinessDayEndsNextDay, @Now, @ActorId)
            ON DUPLICATE KEY UPDATE
                `MINIMUM_MEAL_CREDIT` = VALUES(`MINIMUM_MEAL_CREDIT`),
                `BASE_NOMINATION_FEE` = VALUES(`BASE_NOMINATION_FEE`),
                `TIP_PRESET_AMOUNT_1` = VALUES(`TIP_PRESET_AMOUNT_1`),
                `TIP_PRESET_AMOUNT_2` = VALUES(`TIP_PRESET_AMOUNT_2`),
                `TIP_PRESET_AMOUNT_3` = VALUES(`TIP_PRESET_AMOUNT_3`),
                `TIP_PRESET_AMOUNT_4` = VALUES(`TIP_PRESET_AMOUNT_4`),
                `SEGMENT_MINUTES` = VALUES(`SEGMENT_MINUTES`),
                `REMINDER_AFTER_MINUTES` = VALUES(`REMINDER_AFTER_MINUTES`),
                `ESCALATE_AFTER_MINUTES` = VALUES(`ESCALATE_AFTER_MINUTES`),
                `EXPIRE_AFTER_MINUTES` = VALUES(`EXPIRE_AFTER_MINUTES`),
                `BUSINESS_DAY_START_MINUTE` = VALUES(`BUSINESS_DAY_START_MINUTE`),
                `BUSINESS_DAY_END_MINUTE` = VALUES(`BUSINESS_DAY_END_MINUTE`),
                `BUSINESS_DAY_ENDS_NEXT_DAY` = VALUES(`BUSINESS_DAY_ENDS_NEXT_DAY`),
                `UPDATED_AT` = VALUES(`UPDATED_AT`), `UPDATED_BY` = VALUES(`UPDATED_BY`);
            """;
        await using var connection = await DbContext.CreateOpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(sql, new
        {
            settings.MinimumMealCredit, settings.BaseNominationFee,
            settings.TipPresetAmount1, settings.TipPresetAmount2, settings.TipPresetAmount3, settings.TipPresetAmount4,
            settings.SegmentMinutes,
            settings.ReminderAfterMinutes, settings.EscalateAfterMinutes, settings.ExpireAfterMinutes,
            settings.BusinessDayStartMinute, settings.BusinessDayEndMinute, settings.BusinessDayEndsNextDay,
            Now = now, ActorId = actorId
        }, cancellationToken: cancellationToken));
    }

    public async Task SetNominationPauseAsync(DateTime? pausedUntil, string actorId, DateTime now, CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE `ORDERING_SETTINGS` SET `NOMINATION_PAUSED_UNTIL` = @PausedUntil,
                   `UPDATED_AT` = @Now, `UPDATED_BY` = @ActorId WHERE `ID` = 'default';
            """;
        await using var connection = await DbContext.CreateOpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(sql, new { PausedUntil = pausedUntil, Now = now, ActorId = actorId },
            cancellationToken: cancellationToken));
    }

    public Task<OrderSessionRow?> GetSessionByIdAsync(string id, CancellationToken cancellationToken)
        => GetSessionAsync("S.`ID` = @Value", id, cancellationToken);

    public Task<OrderSessionRow?> GetSessionByTokenHashAsync(string tokenHash, CancellationToken cancellationToken)
        => GetSessionAsync("S.`ACCESS_TOKEN_HASH` = @Value", tokenHash, cancellationToken);

    public async Task<OrderSessionRow?> GetSessionByGameIdAsync(string gameId, DateOnly businessDate, CancellationToken cancellationToken)
    {
        const string sql = $"""
            SELECT {SessionColumns}
            FROM `CUSTOMER_ORDER_SESSIONS` S
            WHERE S.`GAME_ID` = @GameId AND S.`BUSINESS_DATE` = @BusinessDate LIMIT 1;
            """;
        return await QuerySingleOrDefaultAsync<OrderSessionRow>(sql,
            new { GameId = gameId, BusinessDate = businessDate.ToDateTime(TimeOnly.MinValue) }, cancellationToken);
    }

    private Task<OrderSessionRow?> GetSessionAsync(string where, string value, CancellationToken cancellationToken)
    {
        var sql = $"SELECT {SessionColumns} FROM `CUSTOMER_ORDER_SESSIONS` S WHERE {where} LIMIT 1;";
        return QuerySingleOrDefaultAsync<OrderSessionRow>(sql, new { Value = value }, cancellationToken);
    }

    public async Task CreateSessionAsync(OrderSessionRow session, string actorId, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO `CUSTOMER_ORDER_SESSIONS`
                (`ID`, `GAME_ID`, `CUSTOMER_NAME`, `BUSINESS_DATE`, `ACCESS_TOKEN_HASH`, `RECOVERY_CODE_HASH`,
                 `MAX_NOMINATED_STAFF`, `PREPAID_MEAL_CREDIT`, `REMAINING_MEAL_CREDIT`, `SESSION_STATUS`,
                 `CREATED_AT`, `CREATED_BY`, `UPDATED_AT`, `UPDATED_BY`)
            VALUES (@Id, @GameId, @CustomerName, @BusinessDate, @AccessTokenHash, @RecoveryCodeHash,
                    @MaxNominatedStaff, @PrepaidMealCredit, @RemainingMealCredit, 'active',
                    @CreatedAt, @ActorId, @CreatedAt, @ActorId);
            INSERT INTO `ORDER_AUDIT_LOG`
                (`ID`, `SESSION_ID`, `ACTION_TYPE`, `AFTER_JSON`, `ACTOR_ID`, `ACTOR_ROLE`, `CREATED_AT`)
            VALUES (@AuditId, @Id, 'session.created', @AfterJson, @ActorId, 'admin', @CreatedAt);
            """;
        await using var connection = await DbContext.CreateOpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(sql, new
        {
            session.Id, session.GameId, session.CustomerName,
            BusinessDate = session.BusinessDate.Date, session.AccessTokenHash, session.RecoveryCodeHash,
            session.MaxNominatedStaff, session.PrepaidMealCredit, session.RemainingMealCredit,
            session.CreatedAt, ActorId = actorId, AuditId = NewId(), AfterJson = JsonSerializer.Serialize(session)
        }, cancellationToken: cancellationToken));
    }

    public async Task RotateSessionCredentialsAsync(string sessionId, string tokenHash, string? recoveryCodeHash, DateTime now, CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE `CUSTOMER_ORDER_SESSIONS`
            SET `ACCESS_TOKEN_HASH` = @TokenHash,
                `RECOVERY_CODE_HASH` = COALESCE(@RecoveryCodeHash, `RECOVERY_CODE_HASH`),
                `LAST_ACCESSED_AT` = @Now, `UPDATED_AT` = @Now
            WHERE `ID` = @SessionId;
            """;
        await using var connection = await DbContext.CreateOpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(sql, new { SessionId = sessionId, TokenHash = tokenHash, RecoveryCodeHash = recoveryCodeHash, Now = now },
            cancellationToken: cancellationToken));
    }

    public async Task UpdateSessionAsync(string sessionId, string? customerName, int? maxNominatedStaff,
        int? remainingMealCredit, string actorId, string actorRole, DateTime now, CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE `CUSTOMER_ORDER_SESSIONS`
            SET `CUSTOMER_NAME` = COALESCE(@CustomerName, `CUSTOMER_NAME`),
                `MAX_NOMINATED_STAFF` = COALESCE(@MaxNominatedStaff, `MAX_NOMINATED_STAFF`),
                `REMAINING_MEAL_CREDIT` = COALESCE(@RemainingMealCredit, `REMAINING_MEAL_CREDIT`),
                `UPDATED_AT` = @Now, `UPDATED_BY` = @ActorId
            WHERE `ID` = @SessionId;
            INSERT INTO `ORDER_AUDIT_LOG`
                (`ID`, `SESSION_ID`, `ACTION_TYPE`, `AFTER_JSON`, `ACTOR_ID`, `ACTOR_ROLE`, `CREATED_AT`)
            VALUES (@AuditId, @SessionId, 'session.updated', @AfterJson, @ActorId, @ActorRole, @Now);
            """;
        await using var connection = await DbContext.CreateOpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(sql, new
        {
            SessionId = sessionId, CustomerName = customerName, MaxNominatedStaff = maxNominatedStaff,
            RemainingMealCredit = remainingMealCredit, Now = now, ActorId = actorId, ActorRole = actorRole,
            AuditId = NewId(), AfterJson = JsonSerializer.Serialize(new { customerName, maxNominatedStaff, remainingMealCredit })
        }, cancellationToken: cancellationToken));
    }

    public async Task<MenuProductRow?> GetMenuProductAsync(string referenceId, string kind, CancellationToken cancellationToken)
    {
        var sql = kind == "set"
            ? "SELECT `ID` AS Id, `SET_NAME` AS Name, `SET_PRICE` AS Price, `IS_AVAILABLE` AS IsAvailable FROM `MENU_SETS` WHERE `ID` = @Id LIMIT 1;"
            : "SELECT `ID` AS Id, `ITEM_NAME` AS Name, `PRICE` AS Price, `IS_AVAILABLE` AS IsAvailable FROM `MENU_ITEMS` WHERE `ID` = @Id LIMIT 1;";
        return await QuerySingleOrDefaultAsync<MenuProductRow>(sql, new { Id = referenceId }, cancellationToken);
    }

    public async Task<StaffOfferRow?> GetStaffOfferAsync(string staffId, string serviceId, DateOnly businessDate,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT M.`ID` AS StaffId, M.`DISPLAY_NAME` AS StaffName,
                   COALESCE(SC.`IS_WORKING`, TRUE) AS IsWorkingToday,
                   M.`IS_NOMINATABLE` AS StaffIsNominatable, M.`BUFFER_MINUTES` AS BufferMinutes,
                   SV.`ID` AS ServiceId, SV.`SERVICE_NAME` AS ServiceName, SV.`PRICE` AS Price,
                   SV.`DURATION_MINUTES` AS DurationMinutes, SV.`IS_NOMINATABLE` AS ServiceIsNominatable,
                   SV.`ADDITIONAL_PERSON_PRICE` AS AdditionalPersonPrice, SV.`IS_ENABLED` AS ServiceIsEnabled
            FROM `STAFF_MEMBERS` M
            JOIN `STAFF_SERVICES` SV ON SV.`STAFF_ID` = M.`ID`
            LEFT JOIN `STAFF_SCHEDULES` SC
                   ON SC.`STAFF_ID` = M.`ID` AND SC.`WORK_DATE` = @BusinessDate
            WHERE M.`ID` = @StaffId AND SV.`ID` = @ServiceId AND M.`IS_ACTIVE` = TRUE LIMIT 1;
            """;
        return await QuerySingleOrDefaultAsync<StaffOfferRow>(sql, new
        {
            StaffId = staffId,
            ServiceId = serviceId,
            BusinessDate = businessDate.ToDateTime(TimeOnly.MinValue)
        }, cancellationToken);
    }

    public async Task<StaffNominationRow?> GetStaffNominationAsync(string staffId, DateOnly businessDate,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT M.`ID` AS StaffId, M.`DISPLAY_NAME` AS StaffName,
                   COALESCE(SC.`IS_WORKING`, TRUE) AS IsWorkingToday,
                   M.`IS_NOMINATABLE` AS StaffIsNominatable, M.`BUFFER_MINUTES` AS BufferMinutes
            FROM `STAFF_MEMBERS` M
            LEFT JOIN `STAFF_SCHEDULES` SC
                   ON SC.`STAFF_ID` = M.`ID` AND SC.`WORK_DATE` = @BusinessDate
            WHERE M.`ID` = @StaffId AND M.`IS_ACTIVE` = TRUE LIMIT 1;
            """;
        return await QuerySingleOrDefaultAsync<StaffNominationRow>(sql, new
        {
            StaffId = staffId,
            BusinessDate = businessDate.ToDateTime(TimeOnly.MinValue)
        }, cancellationToken);
    }

    public async Task<BusinessPeriodRow?> GetActiveBusinessPeriodAsync(DateTime now,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT `ID` AS Id, `BUSINESS_DATE` AS BusinessDate, `STARTS_AT` AS StartsAt, `ENDS_AT` AS EndsAt
            FROM `BUSINESS_PERIODS`
            WHERE `PERIOD_STATUS` = 'open' AND `STARTS_AT` <= @Now AND `ENDS_AT` > @Now
            ORDER BY `STARTS_AT` DESC LIMIT 1;
            """;
        return await QuerySingleOrDefaultAsync<BusinessPeriodRow>(sql, new { Now = now }, cancellationToken);
    }

    public async Task<BusinessPeriodRow> GetOrCreateBusinessPeriodAsync(BusinessPeriodRow period,
        CancellationToken cancellationToken)
    {
        const string insertSql = """
            INSERT IGNORE INTO `BUSINESS_PERIODS`
                (`ID`, `BUSINESS_DATE`, `STARTS_AT`, `ENDS_AT`, `TIMEZONE`, `PERIOD_STATUS`, `CREATED_AT`, `UPDATED_AT`)
            VALUES (@Id, @BusinessDate, @StartsAt, @EndsAt, 'Asia/Taipei', 'open', @StartsAt, @StartsAt);
            """;
        const string selectSql = """
            SELECT `ID` AS Id, `BUSINESS_DATE` AS BusinessDate, `STARTS_AT` AS StartsAt, `ENDS_AT` AS EndsAt
            FROM `BUSINESS_PERIODS` WHERE `BUSINESS_DATE` = @BusinessDate LIMIT 1;
            """;
        await using var connection = await DbContext.CreateOpenConnectionAsync(cancellationToken);
        var parameters = new
        {
            period.Id,
            BusinessDate = period.BusinessDate.Date,
            period.StartsAt,
            period.EndsAt
        };
        await connection.ExecuteAsync(new CommandDefinition(insertSql, parameters, cancellationToken: cancellationToken));
        return await connection.QuerySingleAsync<BusinessPeriodRow>(new CommandDefinition(selectSql, parameters,
            cancellationToken: cancellationToken));
    }

    public async Task<string?> GetStaffNameAsync(string staffId, CancellationToken cancellationToken)
    {
        const string sql = "SELECT `DISPLAY_NAME` FROM `STAFF_MEMBERS` WHERE `ID` = @StaffId AND `IS_ACTIVE` = TRUE LIMIT 1;";
        return await QuerySingleOrDefaultAsync<string>(sql, new { StaffId = staffId }, cancellationToken);
    }

    public async Task<bool> IsStaffBusyAsync(string staffId, DateTime startsAt, DateTime endsAt, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT EXISTS(
                SELECT 1 FROM `STAFF_BUSY_BLOCKS`
                WHERE `STAFF_ID` = @StaffId AND `BLOCK_STATUS` = 'active'
                  AND `ENDS_AT` > @StartsAt AND `STARTS_AT` < @EndsAt
            );
            """;
        await using var connection = await DbContext.CreateOpenConnectionAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<bool>(new CommandDefinition(sql,
            new { StaffId = staffId, StartsAt = startsAt, EndsAt = endsAt }, cancellationToken: cancellationToken));
    }

    public async Task CreateOrderAsync(NewOrderAggregate order, CancellationToken cancellationToken)
    {
        await using var connection = await DbContext.CreateOpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            var remaining = await connection.ExecuteScalarAsync<int?>(new CommandDefinition(
                "SELECT `REMAINING_MEAL_CREDIT` FROM `CUSTOMER_ORDER_SESSIONS` WHERE `ID` = @SessionId AND `SESSION_STATUS` = 'active' FOR UPDATE;",
                new { order.SessionId }, transaction, cancellationToken: cancellationToken));
            if (!remaining.HasValue) throw new BusinessException("點餐碼已失效。", "ORDER_SESSION_INACTIVE");
            if (order.MealCreditApplied > remaining.Value)
                throw new BusinessException("信物折抵餘額已變更，請重新確認訂單。", "MEAL_CREDIT_CHANGED");

            const string insertOrder = """
                INSERT INTO `ORDERS`
                    (`ID`, `SESSION_ID`, `ORDER_NUMBER`, `ORDER_KIND`, `PARENT_NOMINEE_ID`,
                     `ORDER_STATUS`, `QUEUE_ENTERED_AT`, `SUBMITTED_AT`,
                     `CONFIRMED_AT`, `SUBTOTAL`, `MEAL_CREDIT_APPLIED`, `TOTAL_AMOUNT`, `CUSTOMER_NOTE`, `CREATED_AT`, `UPDATED_AT`)
                VALUES (@Id, @SessionId, @OrderNumber, @OrderKind, @ParentNomineeId, @Status, @QueueEnteredAt, @SubmittedAt,
                        CASE WHEN @Status = 'confirmed' THEN @SubmittedAt ELSE NULL END,
                        @Subtotal, @MealCreditApplied, @TotalAmount, @CustomerNote, @SubmittedAt, @SubmittedAt);
                UPDATE `CUSTOMER_ORDER_SESSIONS`
                SET `REMAINING_MEAL_CREDIT` = `REMAINING_MEAL_CREDIT` - @MealCreditApplied,
                    `UPDATED_AT` = @SubmittedAt
                WHERE `ID` = @SessionId;
                """;
            await connection.ExecuteAsync(new CommandDefinition(insertOrder, order, transaction, cancellationToken: cancellationToken));

            const string insertItem = """
                INSERT INTO `ORDER_ITEMS`
                    (`ID`, `ORDER_ID`, `ITEM_TYPE`, `REFERENCE_ID`, `PARENT_ITEM_ID`, `NAME_SNAPSHOT`, `UNIT_PRICE`,
                     `QUANTITY`, `SEGMENT_COUNT`, `DURATION_MINUTES`, `LINE_TOTAL`, `PRICE_RULE`, `SORT_ORDER`, `CREATED_AT`, `UPDATED_AT`)
                VALUES (@Id, @OrderId, @ItemType, @ReferenceId, @ParentItemId, @Name, @UnitPrice,
                        @Quantity, @SegmentCount, @DurationMinutes, @LineTotal, @PriceRule, @SortOrder, @Now, @Now);
                """;
            await connection.ExecuteAsync(new CommandDefinition(insertItem,
                order.Items.Select(item => new { item.Id, OrderId = order.Id, item.ItemType, item.ReferenceId,
                    item.ParentItemId, item.Name, item.UnitPrice, item.Quantity, item.SegmentCount,
                    item.DurationMinutes, item.LineTotal, item.PriceRule, item.SortOrder, Now = order.SubmittedAt }),
                transaction, cancellationToken: cancellationToken));

            if (order.Nominees.Count > 0)
            {
                const string insertNominee = """
                    INSERT INTO `ORDER_NOMINEES`
                        (`ID`, `ORDER_ID`, `STAFF_ID`, `STAFF_NAME_SNAPSHOT`, `SERVICE_ID`, `SERVICE_NAME_SNAPSHOT`,
                         `SEGMENT_COUNT`, `SERVICE_DURATION_MINUTES`, `REQUESTED_STARTS_AT`, `REQUESTED_SERVICE_ENDS_AT`,
                         `SEGMENT_MINUTES_SNAPSHOT`, `RESERVED_MINUTES`, `BUFFER_MINUTES_SNAPSHOT`,
                         `REQUESTED_BUSY_UNTIL`, `NOMINATION_MODE`, `CONFIRMATION_STATUS`, `CREATED_AT`, `UPDATED_AT`)
                    VALUES (@Id, @OrderId, @StaffId, @StaffName, @ServiceId, @ServiceName,
                            @SegmentCount, @ServiceDurationMinutes, @StartsAt, @ServiceEndsAt,
                            @SegmentMinutesSnapshot, @ReservedMinutes, @BufferMinutesSnapshot,
                            @BusyUntil, @NominationMode, 'waiting', @Now, @Now);
                    """;
                await connection.ExecuteAsync(new CommandDefinition(insertNominee,
                    order.Nominees.Select(item => new { item.Id, OrderId = order.Id, item.StaffId, item.StaffName,
                        item.ServiceId, item.ServiceName, item.SegmentCount, item.ServiceDurationMinutes,
                        item.SegmentMinutesSnapshot, item.ReservedMinutes, item.BufferMinutesSnapshot,
                        item.StartsAt, item.ServiceEndsAt, item.BusyUntil, item.NominationMode,
                        Now = order.SubmittedAt }),
                    transaction, cancellationToken: cancellationToken));
            }

            if (order.Tips.Count > 0)
            {
                const string insertTip = """
                    INSERT INTO `ORDER_TIPS`
                        (`ID`, `ORDER_ID`, `ORDER_ITEM_ID`, `STAFF_ID`, `STAFF_NAME_SNAPSHOT`, `TIP_AMOUNT`,
                         `STAFF_PERCENTAGE`, `STORE_PERCENTAGE`, `STAFF_AMOUNT`, `STORE_AMOUNT`, `CREATED_AT`, `UPDATED_AT`)
                    VALUES (@Id, @OrderId, @OrderItemId, @StaffId, @StaffName, @Amount,
                            @StaffPercentage, @StorePercentage, @StaffAmount, @StoreAmount, @Now, @Now);
                    """;
                await connection.ExecuteAsync(new CommandDefinition(insertTip,
                    order.Tips.Select(tip => new { tip.Id, OrderId = order.Id, tip.OrderItemId, tip.StaffId,
                        tip.StaffName, tip.Amount, tip.StaffPercentage, tip.StorePercentage,
                        tip.StaffAmount, tip.StoreAmount, Now = order.SubmittedAt }), transaction,
                    cancellationToken: cancellationToken));
            }

            await InsertHistoryAsync(connection, transaction, order.Id, null, order.Status,
                "顧客送出訂單", "customer", null, order.SubmittedAt, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public Task<OrderBundle> GetOrdersBySessionAsync(string sessionId, CancellationToken cancellationToken)
        => GetOrderBundleAsync("O.`SESSION_ID` = @Value", sessionId, cancellationToken);

    public Task<OrderBundle> GetOrderAsync(string orderId, CancellationToken cancellationToken)
        => GetOrderBundleAsync("O.`ID` = @Value", orderId, cancellationToken);

    private async Task<OrderBundle> GetOrderBundleAsync(string where, string value, CancellationToken cancellationToken)
    {
        var sql = $"""
            SELECT O.`ID` AS Id, O.`SESSION_ID` AS SessionId, O.`ORDER_NUMBER` AS OrderNumber,
                   O.`ORDER_KIND` AS OrderKind, O.`PARENT_NOMINEE_ID` AS ParentNomineeId,
                   O.`ORDER_STATUS` AS OrderStatus, O.`QUEUE_ENTERED_AT` AS QueueEnteredAt,
                   O.`SUBMITTED_AT` AS SubmittedAt, O.`CONFIRMED_AT` AS ConfirmedAt,
                   O.`STARTED_AT` AS StartedAt, O.`COMPLETED_AT` AS CompletedAt,
                   O.`CANCELLED_AT` AS CancelledAt, O.`SUBTOTAL` AS Subtotal,
                   O.`MEAL_CREDIT_APPLIED` AS MealCreditApplied, O.`TOTAL_AMOUNT` AS TotalAmount,
                   O.`CUSTOMER_NOTE` AS CustomerNote, O.`INTERNAL_NOTE` AS InternalNote
            FROM `ORDERS` O WHERE {where} ORDER BY O.`SUBMITTED_AT` DESC;
            SELECT I.`ID` AS Id, I.`ORDER_ID` AS OrderId, I.`ITEM_TYPE` AS ItemType,
                   I.`REFERENCE_ID` AS ReferenceId, I.`PARENT_ITEM_ID` AS ParentItemId,
                   I.`NAME_SNAPSHOT` AS NameSnapshot, I.`UNIT_PRICE` AS UnitPrice,
                   I.`QUANTITY` AS Quantity, I.`SEGMENT_COUNT` AS SegmentCount,
                   I.`DURATION_MINUTES` AS DurationMinutes, I.`LINE_TOTAL` AS LineTotal,
                   I.`PRICE_RULE` AS PriceRule, I.`SORT_ORDER` AS SortOrder
            FROM `ORDER_ITEMS` I JOIN `ORDERS` O ON O.`ID` = I.`ORDER_ID` WHERE {where}
            ORDER BY I.`ORDER_ID`, I.`SORT_ORDER`;
            SELECT N.`ID` AS Id, N.`ORDER_ID` AS OrderId, N.`STAFF_ID` AS StaffId,
                   N.`STAFF_NAME_SNAPSHOT` AS StaffNameSnapshot, N.`SERVICE_ID` AS ServiceId,
                   N.`SERVICE_NAME_SNAPSHOT` AS ServiceNameSnapshot, N.`NOMINATION_MODE` AS NominationMode,
                   N.`SEGMENT_COUNT` AS SegmentCount,
                   N.`SERVICE_DURATION_MINUTES` AS ServiceDurationMinutes,
                   N.`SEGMENT_MINUTES_SNAPSHOT` AS SegmentMinutesSnapshot,
                   N.`RESERVED_MINUTES` AS ReservedMinutes,
                   N.`BUFFER_MINUTES_SNAPSHOT` AS BufferMinutesSnapshot,
                   N.`REQUESTED_STARTS_AT` AS RequestedStartsAt,
                   N.`REQUESTED_SERVICE_ENDS_AT` AS RequestedServiceEndsAt,
                   N.`REQUESTED_BUSY_UNTIL` AS RequestedBusyUntil,
                   N.`CONFIRMATION_STATUS` AS ConfirmationStatus, N.`CONFIRMED_AT` AS ConfirmedAt
            FROM `ORDER_NOMINEES` N JOIN `ORDERS` O ON O.`ID` = N.`ORDER_ID` WHERE {where};
            SELECT T.`ID` AS Id, T.`ORDER_ID` AS OrderId, T.`ORDER_ITEM_ID` AS OrderItemId,
                   T.`STAFF_ID` AS StaffId, T.`STAFF_NAME_SNAPSHOT` AS StaffNameSnapshot,
                   T.`TIP_AMOUNT` AS TipAmount, T.`STAFF_PERCENTAGE` AS StaffPercentage,
                   T.`STORE_PERCENTAGE` AS StorePercentage, T.`STAFF_AMOUNT` AS StaffAmount,
                   T.`STORE_AMOUNT` AS StoreAmount
            FROM `ORDER_TIPS` T JOIN `ORDERS` O ON O.`ID` = T.`ORDER_ID` WHERE {where};
            SELECT H.`ORDER_ID` AS OrderId, COALESCE(H.`FROM_STATUS`, '') AS FromStatus,
                   H.`TO_STATUS` AS ToStatus, H.`REASON` AS Reason, H.`ACTOR_TYPE` AS ActorType,
                   H.`CREATED_AT` AS CreatedAt
            FROM `ORDER_STATUS_HISTORY` H JOIN `ORDERS` O ON O.`ID` = H.`ORDER_ID` WHERE {where}
            ORDER BY H.`CREATED_AT`;
            SELECT A.`ID` AS Id, A.`ORDER_ID` AS OrderId, A.`PARENT_NOMINEE_ID` AS ParentNomineeId,
                   A.`STAFF_ID` AS StaffId, A.`STAFF_NAME_SNAPSHOT` AS StaffNameSnapshot,
                   A.`SERVICE_ID` AS ServiceId, A.`SERVICE_NAME_SNAPSHOT` AS ServiceNameSnapshot,
                   A.`SEGMENT_COUNT` AS SegmentCount, A.`SERVICE_DURATION_MINUTES` AS ServiceDurationMinutes,
                   A.`PARTICIPANT_COUNT` AS ParticipantCount, A.`ADDON_STATUS` AS AddonStatus,
                   A.`CONFIRMED_AT` AS ConfirmedAt
            FROM `ORDER_SERVICE_ADDONS` A JOIN `ORDERS` O ON O.`ID` = A.`ORDER_ID` WHERE {where};
            """;
        await using var connection = await DbContext.CreateOpenConnectionAsync(cancellationToken);
        using var grid = await connection.QueryMultipleAsync(new CommandDefinition(sql, new { Value = value }, cancellationToken: cancellationToken));
        return new OrderBundle(
            (await grid.ReadAsync<OrderRow>()).AsList(),
            (await grid.ReadAsync<OrderItemRow>()).AsList(),
            (await grid.ReadAsync<OrderNomineeRow>()).AsList(),
            (await grid.ReadAsync<OrderTipRow>()).AsList(),
            (await grid.ReadAsync<OrderHistoryRow>()).AsList(),
            (await grid.ReadAsync<OrderAddonRow>()).AsList());
    }

    public async Task<IReadOnlyList<AdminOrderSessionRow>> GetAdminSessionsAsync(DateOnly businessDate, string? search,
        CancellationToken cancellationToken)
    {
        const string sql = $"""
            SELECT {SessionColumns}, COUNT(O.`ID`) AS OrderCount,
                   SUM(CASE WHEN O.`ORDER_STATUS` IN ('submitted', 'needs_reschedule', 'partially_confirmed') THEN 1 ELSE 0 END) AS WaitingOrderCount,
                   SUM(CASE WHEN O.`ORDER_STATUS` IN ('confirmed', 'in_service', 'completed') THEN 1 ELSE 0 END) AS ConfirmedOrderCount,
                   COALESCE(SUM(CASE WHEN O.`ORDER_STATUS` NOT IN ('cancelled', 'expired', 'rejected') THEN O.`TOTAL_AMOUNT` ELSE 0 END), 0) AS TotalAmount,
                   MAX(O.`SUBMITTED_AT`) AS LastOrderedAt
            FROM `CUSTOMER_ORDER_SESSIONS` S
            LEFT JOIN `ORDERS` O ON O.`SESSION_ID` = S.`ID`
            WHERE S.`BUSINESS_DATE` = @BusinessDate
              AND (@Search IS NULL OR S.`GAME_ID` LIKE @SearchLike OR S.`CUSTOMER_NAME` LIKE @SearchLike)
            GROUP BY S.`ID`
            ORDER BY WaitingOrderCount DESC, LastOrderedAt DESC, S.`CUSTOMER_NAME`;
            """;
        return await QueryAsync<AdminOrderSessionRow>(sql, new
        {
            BusinessDate = businessDate.ToDateTime(TimeOnly.MinValue),
            Search = string.IsNullOrWhiteSpace(search) ? null : search,
            SearchLike = $"%{search?.Trim()}%"
        }, cancellationToken);
    }

    public async Task<int> ExpireWaitingOrdersAsync(DateTime cutoff, DateTime now, CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE `CUSTOMER_ORDER_SESSIONS` S
            JOIN (
                SELECT O.`SESSION_ID`, SUM(O.`MEAL_CREDIT_APPLIED`) AS Credit
                FROM `ORDERS` O
                WHERE O.`ORDER_STATUS` IN ('submitted', 'partially_confirmed', 'needs_reschedule') AND O.`QUEUE_ENTERED_AT` <= @Cutoff
                GROUP BY O.`SESSION_ID`
            ) X ON X.`SESSION_ID` = S.`ID`
            SET S.`REMAINING_MEAL_CREDIT` = S.`REMAINING_MEAL_CREDIT` + X.Credit,
                S.`UPDATED_AT` = @Now;
            INSERT INTO `ORDER_STATUS_HISTORY`
                (`ID`, `ORDER_ID`, `FROM_STATUS`, `TO_STATUS`, `REASON`, `ACTOR_TYPE`, `CREATED_AT`)
            SELECT UUID(), O.`ID`, O.`ORDER_STATUS`, 'expired', '等待確認逾時，自動失效。', 'system', @Now
            FROM `ORDERS` O
            WHERE O.`ORDER_STATUS` IN ('submitted', 'partially_confirmed', 'needs_reschedule') AND O.`QUEUE_ENTERED_AT` <= @Cutoff;
            UPDATE `ORDERS` O
            SET O.`ORDER_STATUS` = 'expired', O.`CANCELLED_AT` = @Now, O.`UPDATED_AT` = @Now
            WHERE O.`ORDER_STATUS` IN ('submitted', 'partially_confirmed', 'needs_reschedule') AND O.`QUEUE_ENTERED_AT` <= @Cutoff;
            UPDATE `ORDER_NOMINEES` N JOIN `ORDERS` O ON O.`ID` = N.`ORDER_ID`
            SET N.`CONFIRMATION_STATUS` = 'expired', N.`UPDATED_AT` = @Now
            WHERE O.`ORDER_STATUS` = 'expired' AND N.`CONFIRMATION_STATUS` IN ('waiting', 'confirmed');
            UPDATE `ORDER_SERVICE_ADDONS` A JOIN `ORDERS` O ON O.`ID` = A.`ORDER_ID`
            SET A.`ADDON_STATUS` = 'expired', A.`UPDATED_AT` = @Now
            WHERE O.`ORDER_STATUS` = 'expired' AND A.`ADDON_STATUS` = 'waiting';
            """;
        await using var connection = await DbContext.CreateOpenConnectionAsync(cancellationToken);
        var command = new CommandDefinition(sql, new { Cutoff = cutoff, Now = now }, cancellationToken: cancellationToken);
        return await connection.ExecuteAsync(command);
    }

    public async Task<string> ConfirmNomineeAsync(string orderId, string staffId, string actorId, DateTime now,
        CancellationToken cancellationToken)
    {
        await using var connection = await DbContext.CreateOpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            var order = await connection.QuerySingleOrDefaultAsync<OrderRow>(new CommandDefinition(
                "SELECT `ID` AS Id, `ORDER_STATUS` AS OrderStatus FROM `ORDERS` WHERE `ID` = @OrderId FOR UPDATE;",
                new { OrderId = orderId }, transaction, cancellationToken: cancellationToken));
            if (order is null) throw new BusinessException("找不到訂單。", "ORDER_NOT_FOUND");
            if (order.OrderStatus is not ("submitted" or "partially_confirmed"))
                throw new BusinessException("此訂單目前不可確認。", "ORDER_NOT_CONFIRMABLE");

            var nominees = (await connection.QueryAsync<OrderNomineeRow>(new CommandDefinition("""
                SELECT `ID` AS Id, `ORDER_ID` AS OrderId, `STAFF_ID` AS StaffId,
                       `STAFF_NAME_SNAPSHOT` AS StaffNameSnapshot,
                       `REQUESTED_STARTS_AT` AS RequestedStartsAt,
                       `REQUESTED_SERVICE_ENDS_AT` AS RequestedServiceEndsAt,
                       `REQUESTED_BUSY_UNTIL` AS RequestedBusyUntil,
                       `CONFIRMATION_STATUS` AS ConfirmationStatus
                FROM `ORDER_NOMINEES` WHERE `ORDER_ID` = @OrderId FOR UPDATE;
                """, new { OrderId = orderId }, transaction, cancellationToken: cancellationToken))).AsList();
            await LockStaffRowsAsync(connection, transaction, nominees.Select(item => item.StaffId), cancellationToken);
            var nominee = nominees.SingleOrDefault(item => item.StaffId == staffId);
            if (nominee is null) throw new BusinessException("你不是此訂單的被指名店員。", "NOMINEE_SCOPE_FORBIDDEN");
            if (nominee.ConfirmationStatus == "confirmed") return order.OrderStatus;
            if (nominee.RequestedStartsAt < now)
            {
                await ReturnToRescheduleAsync(connection, transaction, orderId, order.OrderStatus,
                    "預定開始時間已過，請重新安排時段。", actorId, now, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return "needs_reschedule";
            }

            if (await HasConflictAsync(connection, transaction, staffId, nominee.RequestedStartsAt,
                    nominee.RequestedBusyUntil, cancellationToken))
            {
                await ReturnToRescheduleAsync(connection, transaction, orderId, order.OrderStatus,
                    "店員時段已被其他成立訂單占用。", actorId, now, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return "needs_reschedule";
            }

            await connection.ExecuteAsync(new CommandDefinition("""
                UPDATE `ORDER_NOMINEES` SET `CONFIRMATION_STATUS` = 'confirmed', `CONFIRMED_AT` = @Now,
                       `CONFIRMED_BY` = @ActorId, `UPDATED_AT` = @Now WHERE `ID` = @NomineeId;
                """, new { Now = now, ActorId = actorId, NomineeId = nominee.Id }, transaction,
                cancellationToken: cancellationToken));

            nominee.ConfirmationStatus = "confirmed";
            if (nominees.Any(item => item.ConfirmationStatus != "confirmed"))
            {
                await connection.ExecuteAsync(new CommandDefinition(
                    "UPDATE `ORDERS` SET `ORDER_STATUS` = 'partially_confirmed', `UPDATED_AT` = @Now WHERE `ID` = @OrderId;",
                    new { Now = now, OrderId = orderId }, transaction, cancellationToken: cancellationToken));
                await InsertHistoryAsync(connection, transaction, orderId, order.OrderStatus, "partially_confirmed",
                    "部分被指名店員已確認", "staff", actorId, now, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return "partially_confirmed";
            }

            foreach (var item in nominees)
            {
                if (await HasConflictAsync(connection, transaction, item.StaffId, item.RequestedStartsAt,
                        item.RequestedBusyUntil, cancellationToken))
                {
                    await ReturnToRescheduleAsync(connection, transaction, orderId, order.OrderStatus,
                        "最終確認時發現店員時段衝突，已退回等待重新安排。", actorId, now, cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                    return "needs_reschedule";
                }
            }

            const string insertBlocks = """
                INSERT INTO `STAFF_BUSY_BLOCKS`
                    (`ID`, `ORDER_ID`, `ORDER_NOMINEE_ID`, `STAFF_ID`, `STARTS_AT`, `SERVICE_ENDS_AT`, `ENDS_AT`,
                     `BLOCK_STATUS`, `CREATED_AT`, `UPDATED_AT`)
                VALUES (@Id, @OrderId, @NomineeId, @StaffId, @StartsAt, @ServiceEndsAt, @EndsAt,
                        'active', @Now, @Now);
                """;
            await connection.ExecuteAsync(new CommandDefinition(insertBlocks,
                nominees.Select(item => new { Id = NewId(), OrderId = orderId, NomineeId = item.Id,
                    item.StaffId, StartsAt = item.RequestedStartsAt, ServiceEndsAt = item.RequestedServiceEndsAt,
                    EndsAt = item.RequestedBusyUntil, Now = now }), transaction, cancellationToken: cancellationToken));
            await connection.ExecuteAsync(new CommandDefinition("""
                UPDATE `ORDERS` SET `ORDER_STATUS` = 'confirmed', `CONFIRMED_AT` = @Now, `UPDATED_AT` = @Now
                WHERE `ID` = @OrderId;
                """, new { Now = now, OrderId = orderId }, transaction, cancellationToken: cancellationToken));
            await InsertHistoryAsync(connection, transaction, orderId, order.OrderStatus, "confirmed",
                "所有被指名店員已確認，訂單成立。", "staff", actorId, now, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return "confirmed";
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task RescheduleOrderAsync(string orderId, DateTime startsAt, string actorId, string actorRole,
        DateTime now, CancellationToken cancellationToken)
    {
        await using var connection = await DbContext.CreateOpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var order = await connection.QuerySingleOrDefaultAsync<OrderRow>(new CommandDefinition(
            "SELECT `ID` AS Id, `SESSION_ID` AS SessionId, `ORDER_STATUS` AS OrderStatus, `MEAL_CREDIT_APPLIED` AS MealCreditApplied FROM `ORDERS` WHERE `ID` = @OrderId FOR UPDATE;",
            new { OrderId = orderId }, transaction, cancellationToken: cancellationToken));
        if (order is null)
            throw new BusinessException("找不到訂單。", "ORDER_NOT_FOUND");
        var status = order.OrderStatus;
        if (status is not ("submitted" or "partially_confirmed" or "needs_reschedule" or "expired"))
            throw new BusinessException("此訂單目前不可重新排程。", "ORDER_NOT_RESCHEDULABLE");
        var nominees = (await connection.QueryAsync<OrderNomineeRow>(new CommandDefinition("""
            SELECT `ID` AS Id, `RESERVED_MINUTES` AS ReservedMinutes,
                   `BUFFER_MINUTES_SNAPSHOT` AS BufferMinutesSnapshot
            FROM `ORDER_NOMINEES` WHERE `ORDER_ID` = @OrderId FOR UPDATE;
            """, new { OrderId = orderId }, transaction, cancellationToken: cancellationToken))).AsList();
        foreach (var nominee in nominees)
        {
            var serviceEnds = startsAt.AddMinutes(nominee.ReservedMinutes);
            var buffer = Math.Max(0, nominee.BufferMinutesSnapshot);
            await connection.ExecuteAsync(new CommandDefinition("""
                UPDATE `ORDER_NOMINEES`
                SET `REQUESTED_STARTS_AT` = @StartsAt, `REQUESTED_SERVICE_ENDS_AT` = @ServiceEnds,
                    `REQUESTED_BUSY_UNTIL` = @BusyUntil, `CONFIRMATION_STATUS` = 'waiting',
                    `CONFIRMED_AT` = NULL, `CONFIRMED_BY` = NULL, `UPDATED_AT` = @Now WHERE `ID` = @Id;
                """, new { StartsAt = startsAt, ServiceEnds = serviceEnds, BusyUntil = serviceEnds.AddMinutes(buffer),
                    Now = now, nominee.Id }, transaction, cancellationToken: cancellationToken));
        }
        if (status == "expired" && order.MealCreditApplied > 0)
        {
            var restoredCredit = await connection.ExecuteAsync(new CommandDefinition("""
                UPDATE `CUSTOMER_ORDER_SESSIONS`
                SET `REMAINING_MEAL_CREDIT` = `REMAINING_MEAL_CREDIT` - @Credit, `UPDATED_AT` = @Now
                WHERE `ID` = @SessionId AND `REMAINING_MEAL_CREDIT` >= @Credit;
                """, new { order.SessionId, Credit = order.MealCreditApplied, Now = now }, transaction,
                cancellationToken: cancellationToken));
            if (restoredCredit != 1)
                throw new BusinessException("顧客目前的信物餘額不足以重新啟動此訂單，請先由後台確認折抵金額。",
                    "MEAL_CREDIT_UNAVAILABLE_FOR_REACTIVATION");
        }
        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE `ORDERS` SET `ORDER_STATUS` = 'submitted', `QUEUE_ENTERED_AT` = @Now,
                   `CONFIRMED_AT` = NULL,
                   `CANCELLED_AT` = CASE WHEN @WasExpired = 1 THEN NULL ELSE `CANCELLED_AT` END,
                   `UPDATED_AT` = @Now, `UPDATED_BY` = @ActorId WHERE `ID` = @OrderId;
            """, new { Now = now, ActorId = actorId, OrderId = orderId, WasExpired = status == "expired" }, transaction,
            cancellationToken: cancellationToken));
        var historyReason = status == "expired" ? "已失效訂單由後台強制啟動並重新排程" : "重新安排指名時段";
        await InsertHistoryAsync(connection, transaction, orderId, status, "submitted", historyReason,
            "admin", actorId, now, cancellationToken);
        await InsertAuditAsync(connection, transaction, orderId, null,
            status == "expired" ? "order.force_reactivated" : "order.rescheduled", null,
            JsonSerializer.Serialize(new { startsAt, wasExpired = status == "expired" }), actorId, actorRole, now,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task BackfillServedOrderAsync(string orderId, string status, DateTime actualStartsAt,
        DateTime? actualEndsAt, string reason, string actorId, string actorRole, string? actorStaffId, DateTime now,
        CancellationToken cancellationToken)
    {
        await using var connection = await DbContext.CreateOpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            var order = await connection.QuerySingleOrDefaultAsync<OrderRow>(new CommandDefinition("""
                SELECT `ID` AS Id, `SESSION_ID` AS SessionId, `ORDER_KIND` AS OrderKind,
                       `ORDER_STATUS` AS OrderStatus, `MEAL_CREDIT_APPLIED` AS MealCreditApplied
                FROM `ORDERS` WHERE `ID` = @OrderId FOR UPDATE;
                """, new { OrderId = orderId }, transaction, cancellationToken: cancellationToken))
                ?? throw new BusinessException("找不到訂單。", "ORDER_NOT_FOUND");
            if (order.OrderStatus != "expired")
                throw new BusinessException("只有已失效訂單可以補登已接待。", "ORDER_BACKFILL_STATUS_INVALID");
            if (order.OrderKind == "service_addon")
                throw new BusinessException("附掛式加購服務請依原指名訂單處理，不可單獨補登。", "ORDER_BACKFILL_ADDON_FORBIDDEN");
            if (status is not ("in_service" or "completed"))
                throw new BusinessException("補登狀態不正確。", "ORDER_BACKFILL_STATUS_INVALID");

            var nominees = (await connection.QueryAsync<OrderNomineeRow>(new CommandDefinition("""
                SELECT `ID` AS Id, `ORDER_ID` AS OrderId, `STAFF_ID` AS StaffId,
                       `STAFF_NAME_SNAPSHOT` AS StaffNameSnapshot,
                       `REQUESTED_STARTS_AT` AS RequestedStartsAt,
                       `REQUESTED_SERVICE_ENDS_AT` AS RequestedServiceEndsAt,
                       `REQUESTED_BUSY_UNTIL` AS RequestedBusyUntil,
                       `RESERVED_MINUTES` AS ReservedMinutes,
                       `BUFFER_MINUTES_SNAPSHOT` AS BufferMinutesSnapshot,
                       `CONFIRMATION_STATUS` AS ConfirmationStatus
                FROM `ORDER_NOMINEES` WHERE `ORDER_ID` = @OrderId FOR UPDATE;
                """, new { OrderId = orderId }, transaction, cancellationToken: cancellationToken))).AsList();
            if (nominees.Count == 0)
                throw new BusinessException("此訂單沒有可補登的指名服務。", "ORDER_BACKFILL_NOMINEE_REQUIRED");
            await LockStaffRowsAsync(connection, transaction, nominees.Select(item => item.StaffId), cancellationToken);

            var privileged = actorRole is "manager" or "developer";
            if (!privileged && (actorStaffId is null || nominees.Count != 1 || nominees[0].StaffId != actorStaffId))
                throw new ForbiddenException("只有被指名店員本人可補登單人已接待訂單；多人訂單需由店經理或開發者處理。",
                    "ORDER_BACKFILL_SCOPE_FORBIDDEN");

            if (status == "in_service")
            {
                foreach (var nominee in nominees)
                {
                    if (nominee.RequestedServiceEndsAt <= now || nominee.RequestedBusyUntil <= now)
                        throw new BusinessException("原預約時段已結束，請改用「補登已完成」。", "BACKFILL_SERVICE_ALREADY_ENDED");
                    if (await HasConflictAsync(connection, transaction, nominee.StaffId, actualStartsAt,
                            nominee.RequestedBusyUntil, cancellationToken))
                        throw new BusinessException($"{nominee.StaffNameSnapshot} 目前已有其他忙碌區段，無法補登服務中。",
                            "BACKFILL_STAFF_BUSY");
                }
            }

            if (order.MealCreditApplied > 0)
            {
                var debited = await connection.ExecuteAsync(new CommandDefinition("""
                    UPDATE `CUSTOMER_ORDER_SESSIONS`
                    SET `REMAINING_MEAL_CREDIT` = `REMAINING_MEAL_CREDIT` - @Credit, `UPDATED_AT` = @Now
                    WHERE `ID` = @SessionId AND `REMAINING_MEAL_CREDIT` >= @Credit;
                    """, new { order.SessionId, Credit = order.MealCreditApplied, Now = now }, transaction,
                    cancellationToken: cancellationToken));
                if (debited != 1)
                    throw new BusinessException("顧客目前的信物餘額不足以補回此失效訂單折抵，請改由後台處理補收或調整。",
                        "MEAL_CREDIT_UNAVAILABLE_FOR_BACKFILL");
            }

            await connection.ExecuteAsync(new CommandDefinition("""
                UPDATE `STAFF_BUSY_BLOCKS` SET `BLOCK_STATUS` = 'released', `UPDATED_AT` = @Now
                WHERE `ORDER_ID` = @OrderId AND `BLOCK_STATUS` = 'active';
                UPDATE `ORDER_NOMINEES`
                SET `CONFIRMATION_STATUS` = 'confirmed', `CONFIRMED_AT` = @Now,
                    `CONFIRMED_BY` = @ActorId, `UPDATED_AT` = @Now
                WHERE `ORDER_ID` = @OrderId;
                UPDATE `ORDERS`
                SET `ORDER_STATUS` = @NextStatus, `QUEUE_ENTERED_AT` = NULL,
                    `CONFIRMED_AT` = @Now, `STARTED_AT` = @ActualStartsAt,
                    `COMPLETED_AT` = @ActualEndsAt, `CANCELLED_AT` = NULL,
                    `UPDATED_AT` = @Now, `UPDATED_BY` = @ActorId
                WHERE `ID` = @OrderId;
                """, new
                {
                    OrderId = orderId,
                    NextStatus = status,
                    ActualStartsAt = actualStartsAt,
                    ActualEndsAt = actualEndsAt,
                    Now = now,
                    ActorId = actorId
                }, transaction, cancellationToken: cancellationToken));

            var blockStatus = status == "completed" ? "completed" : "active";
            var blockRows = nominees.Select(nominee =>
            {
                var serviceEndsAt = status == "completed" ? actualEndsAt!.Value : nominee.RequestedServiceEndsAt;
                var endsAt = status == "completed"
                    ? serviceEndsAt.AddMinutes(Math.Max(0, nominee.BufferMinutesSnapshot))
                    : nominee.RequestedBusyUntil;
                return new
                {
                    Id = NewId(),
                    OrderId = orderId,
                    NomineeId = nominee.Id,
                    nominee.StaffId,
                    StartsAt = actualStartsAt,
                    ServiceEndsAt = serviceEndsAt,
                    EndsAt = endsAt,
                    BlockStatus = blockStatus,
                    Now = now
                };
            }).ToArray();
            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO `STAFF_BUSY_BLOCKS`
                    (`ID`, `ORDER_ID`, `ORDER_NOMINEE_ID`, `STAFF_ID`, `STARTS_AT`, `SERVICE_ENDS_AT`, `ENDS_AT`,
                     `BLOCK_STATUS`, `CREATED_AT`, `UPDATED_AT`)
                VALUES (@Id, @OrderId, @NomineeId, @StaffId, @StartsAt, @ServiceEndsAt, @EndsAt,
                        @BlockStatus, @Now, @Now)
                ON DUPLICATE KEY UPDATE
                    `ORDER_ID` = VALUES(`ORDER_ID`), `STAFF_ID` = VALUES(`STAFF_ID`),
                    `STARTS_AT` = VALUES(`STARTS_AT`), `SERVICE_ENDS_AT` = VALUES(`SERVICE_ENDS_AT`),
                    `ENDS_AT` = VALUES(`ENDS_AT`), `BLOCK_STATUS` = VALUES(`BLOCK_STATUS`),
                    `UPDATED_AT` = VALUES(`UPDATED_AT`);
                """, blockRows, transaction, cancellationToken: cancellationToken));

            var nextLabel = status == "completed" ? "已完成" : "服務中";
            var historyReason = $"失效訂單補登{nextLabel}。原因：{reason}";
            var actorType = privileged ? "admin" : "staff";
            await InsertHistoryAsync(connection, transaction, orderId, "expired", status, historyReason,
                actorType, actorId, now, cancellationToken);
            await InsertAuditAsync(connection, transaction, orderId, order.SessionId, "order.backfill_served",
                JsonSerializer.Serialize(new { previousStatus = "expired" }),
                JsonSerializer.Serialize(new
                {
                    status,
                    actualStartsAt,
                    actualEndsAt,
                    reason,
                    staffId = actorStaffId
                }), actorId, actorRole, now, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task UpdateOrderAsync(string orderId, string? customerNote, string? internalNote,
        string actorId, string actorRole, DateTime now, CancellationToken cancellationToken)
    {
        await using var connection = await DbContext.CreateOpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var current = await connection.QuerySingleOrDefaultAsync<OrderRow>(new CommandDefinition("""
            SELECT `ID` AS Id, `SESSION_ID` AS SessionId, `ORDER_STATUS` AS OrderStatus,
                   `MEAL_CREDIT_APPLIED` AS MealCreditApplied, `CUSTOMER_NOTE` AS CustomerNote,
                   `INTERNAL_NOTE` AS InternalNote FROM `ORDERS` WHERE `ID` = @OrderId FOR UPDATE;
            """, new { OrderId = orderId }, transaction, cancellationToken: cancellationToken))
            ?? throw new BusinessException("找不到訂單。", "ORDER_NOT_FOUND");
        if (current.OrderStatus is "completed" or "cancelled" or "expired" or "rejected")
            throw new BusinessException("已結案訂單不可修改。", "ORDER_LOCKED");
        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE `ORDERS` SET `CUSTOMER_NOTE` = COALESCE(@CustomerNote, `CUSTOMER_NOTE`),
                   `INTERNAL_NOTE` = COALESCE(@InternalNote, `INTERNAL_NOTE`),
                   `UPDATED_AT` = @Now, `UPDATED_BY` = @ActorId WHERE `ID` = @OrderId;
            """, new { CustomerNote = customerNote, InternalNote = internalNote,
                Now = now, ActorId = actorId, OrderId = orderId }, transaction, cancellationToken: cancellationToken));
        await InsertAuditAsync(connection, transaction, orderId, null, "order.updated", JsonSerializer.Serialize(current),
            JsonSerializer.Serialize(new { customerNote, internalNote }), actorId, actorRole, now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task ShortenNominationAsync(string orderId, string nomineeId, int segmentCount, string reason,
        string actorId, string actorRole, DateTime now, CancellationToken cancellationToken)
    {
        await using var connection = await DbContext.CreateOpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            var row = await connection.QuerySingleOrDefaultAsync<NominationEditRow>(new CommandDefinition("""
                SELECT N.`ID` AS NomineeId, N.`ORDER_ID` AS OrderId, N.`STAFF_ID` AS StaffId,
                       N.`SERVICE_ID` AS ServiceId, N.`SEGMENT_COUNT` AS SegmentCount,
                       N.`SERVICE_DURATION_MINUTES` AS ServiceDurationMinutes,
                       N.`SEGMENT_MINUTES_SNAPSHOT` AS SegmentMinutesSnapshot,
                       N.`RESERVED_MINUTES` AS ReservedMinutes,
                       N.`BUFFER_MINUTES_SNAPSHOT` AS BufferMinutesSnapshot,
                       N.`REQUESTED_STARTS_AT` AS RequestedStartsAt,
                       N.`REQUESTED_SERVICE_ENDS_AT` AS RequestedServiceEndsAt,
                       N.`REQUESTED_BUSY_UNTIL` AS RequestedBusyUntil,
                       O.`ORDER_STATUS` AS OrderStatus,
                       SI.`ID` AS ServiceItemId, COALESCE(SI.`PRICE_RULE`, 'per_segment') AS ServicePriceRule,
                       BI.`ID` AS BaseItemId
                FROM `ORDER_NOMINEES` N
                JOIN `ORDERS` O ON O.`ID` = N.`ORDER_ID`
                LEFT JOIN `ORDER_ITEMS` SI ON SI.`ORDER_ID` = N.`ORDER_ID`
                    AND SI.`ITEM_TYPE` = 'staff_service' AND SI.`REFERENCE_ID` = N.`SERVICE_ID`
                JOIN `ORDER_ITEMS` BI ON BI.`ORDER_ID` = N.`ORDER_ID`
                    AND BI.`ITEM_TYPE` = 'nomination_base' AND BI.`REFERENCE_ID` = N.`STAFF_ID`
                WHERE N.`ID` = @NomineeId AND N.`ORDER_ID` = @OrderId
                FOR UPDATE;
                """, new { NomineeId = nomineeId, OrderId = orderId }, transaction,
                cancellationToken: cancellationToken));
            if (row is null)
                throw new BusinessException("找不到可縮短的指名預約。", "ORDER_NOMINEE_NOT_FOUND");
            if (row.OrderStatus is not ("submitted" or "partially_confirmed" or "needs_reschedule" or "confirmed"))
                throw new BusinessException("服務開始後不可正式縮短預約；請使用實際提早完成功能。", "NOMINATION_SHORTEN_STATUS_INVALID");
            if (row.RequestedStartsAt <= now)
                throw new BusinessException("已到開始時間的指名不可修改預約；若提早結束請記錄實際完成。", "NOMINATION_ALREADY_STARTED");
            if (segmentCount >= row.SegmentCount)
                throw new BusinessException("正式縮短只能減少節數；需要延長時請另開新訂單。", "NOMINATION_EXTENSION_FORBIDDEN");

            var segmentMinutes = Math.Max(1, row.SegmentMinutesSnapshot);
            var minimumSegments = row.ServicePriceRule == "fixed_duration"
                ? (int)Math.Ceiling(row.ServiceDurationMinutes / (double)segmentMinutes)
                : 1;
            if (segmentCount < minimumSegments)
                throw new BusinessException($"此服務至少需要 {minimumSegments} 節，無法縮短至 {segmentCount} 節。",
                    "NOMINATION_MINIMUM_DURATION_REQUIRED");

            var reservedMinutes = checked(segmentCount * segmentMinutes);
            var serviceEndsAt = row.RequestedStartsAt.AddMinutes(reservedMinutes);
            var busyUntil = serviceEndsAt.AddMinutes(Math.Max(0, row.BufferMinutesSnapshot));
            await connection.ExecuteAsync(new CommandDefinition("""
                UPDATE `ORDER_NOMINEES`
                SET `SEGMENT_COUNT` = @SegmentCount, `RESERVED_MINUTES` = @ReservedMinutes,
                    `SERVICE_DURATION_MINUTES` = CASE WHEN @ServicePriceRule = 'per_segment' THEN @ReservedMinutes ELSE `SERVICE_DURATION_MINUTES` END,
                    `REQUESTED_SERVICE_ENDS_AT` = @ServiceEndsAt, `REQUESTED_BUSY_UNTIL` = @BusyUntil,
                    `UPDATED_AT` = @Now
                WHERE `ID` = @NomineeId;
                UPDATE `ORDER_ITEMS`
                SET `QUANTITY` = @SegmentCount, `SEGMENT_COUNT` = @SegmentCount,
                    `DURATION_MINUTES` = @ReservedMinutes, `LINE_TOTAL` = `UNIT_PRICE` * @SegmentCount,
                    `UPDATED_AT` = @Now, `UPDATED_BY` = @ActorId
                WHERE `ID` = @BaseItemId;
                UPDATE `ORDER_ITEMS`
                SET `QUANTITY` = CASE WHEN `PRICE_RULE` = 'per_segment' THEN @SegmentCount ELSE 1 END,
                    `SEGMENT_COUNT` = @SegmentCount,
                    `DURATION_MINUTES` = CASE WHEN `PRICE_RULE` = 'per_segment' THEN @ReservedMinutes ELSE `DURATION_MINUTES` END,
                    `LINE_TOTAL` = `UNIT_PRICE` * CASE WHEN `PRICE_RULE` = 'per_segment' THEN @SegmentCount ELSE 1 END,
                    `UPDATED_AT` = @Now, `UPDATED_BY` = @ActorId
                WHERE `ID` = @ServiceItemId;
                UPDATE `STAFF_BUSY_BLOCKS`
                SET `SERVICE_ENDS_AT` = @ServiceEndsAt, `ENDS_AT` = @BusyUntil, `UPDATED_AT` = @Now
                WHERE `ORDER_NOMINEE_ID` = @NomineeId AND `BLOCK_STATUS` = 'active';
                """, new
            {
                SegmentCount = segmentCount,
                ReservedMinutes = reservedMinutes,
                ServicePriceRule = row.ServicePriceRule,
                ServiceEndsAt = serviceEndsAt,
                BusyUntil = busyUntil,
                Now = now,
                ActorId = actorId,
                row.NomineeId,
                row.BaseItemId,
                row.ServiceItemId
            }, transaction, cancellationToken: cancellationToken));
            await RecalculateOrderAsync(connection, transaction, orderId, now, actorId, cancellationToken);
            await InsertHistoryAsync(connection, transaction, orderId, row.OrderStatus, row.OrderStatus,
                $"指名預約由 {row.SegmentCount} 節正式縮短為 {segmentCount} 節。原因：{reason}",
                "admin", actorId, now, cancellationToken);
            await InsertAuditAsync(connection, transaction, orderId, null, "order.nomination.shortened",
                JsonSerializer.Serialize(row), JsonSerializer.Serialize(new
                {
                    nomineeId,
                    previousSegmentCount = row.SegmentCount,
                    segmentCount,
                    reservedMinutes,
                    serviceEndsAt,
                    busyUntil,
                    reason
                }), actorId, actorRole, now, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<AddonParentRow?> GetAddonParentAsync(string nomineeId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT N.`ID` AS NomineeId, N.`ORDER_ID` AS ParentOrderId, O.`SESSION_ID` AS SessionId,
                   S.`BUSINESS_DATE` AS BusinessDate, O.`ORDER_STATUS` AS ParentOrderStatus,
                   N.`STAFF_ID` AS StaffId, N.`STAFF_NAME_SNAPSHOT` AS StaffName,
                   N.`REQUESTED_STARTS_AT` AS StartsAt, N.`REQUESTED_SERVICE_ENDS_AT` AS ServiceEndsAt,
                   N.`SEGMENT_MINUTES_SNAPSHOT` AS SegmentMinutes
            FROM `ORDER_NOMINEES` N
            JOIN `ORDERS` O ON O.`ID` = N.`ORDER_ID`
            JOIN `CUSTOMER_ORDER_SESSIONS` S ON S.`ID` = O.`SESSION_ID`
            WHERE N.`ID` = @NomineeId LIMIT 1;
            """;
        return await QuerySingleOrDefaultAsync<AddonParentRow>(sql, new { NomineeId = nomineeId }, cancellationToken);
    }

    public async Task CreateAddonOrderAsync(NewAddonAggregate addon, CancellationToken cancellationToken)
    {
        await using var connection = await DbContext.CreateOpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            var parent = await connection.QuerySingleOrDefaultAsync<AddonParentRow>(new CommandDefinition("""
                SELECT N.`ID` AS NomineeId, N.`ORDER_ID` AS ParentOrderId, O.`SESSION_ID` AS SessionId,
                       O.`ORDER_STATUS` AS ParentOrderStatus, N.`STAFF_ID` AS StaffId,
                       N.`REQUESTED_STARTS_AT` AS StartsAt,
                       N.`REQUESTED_SERVICE_ENDS_AT` AS ServiceEndsAt
                FROM `ORDER_NOMINEES` N JOIN `ORDERS` O ON O.`ID` = N.`ORDER_ID`
                WHERE N.`ID` = @ParentNomineeId FOR UPDATE;
                """, new { addon.ParentNomineeId }, transaction, cancellationToken: cancellationToken));
            if (parent is null || parent.SessionId != addon.SessionId || parent.StaffId != addon.StaffId)
                throw new BusinessException("原指名資料已變更，無法送出加購服務。", "ADDON_PARENT_CHANGED");
            if (parent.ParentOrderStatus is not ("confirmed" or "in_service") || parent.ServiceEndsAt <= addon.SubmittedAt)
                throw new BusinessException("原指名已不在可加購狀態。", "ADDON_PARENT_INACTIVE");
            var effectiveStart = parent.StartsAt > addon.SubmittedAt ? parent.StartsAt : addon.SubmittedAt;
            if (effectiveStart.AddMinutes(addon.ServiceDurationMinutes) > parent.ServiceEndsAt)
                throw new BusinessException("原指名的剩餘時段已變更，無法容納此加購服務。", "ADDON_EXCEEDS_REMAINING_TIME");

            const string sql = """
                INSERT INTO `ORDERS`
                    (`ID`, `SESSION_ID`, `ORDER_NUMBER`, `ORDER_KIND`, `PARENT_NOMINEE_ID`, `ORDER_STATUS`,
                     `QUEUE_ENTERED_AT`, `SUBMITTED_AT`, `CONFIRMED_AT`, `SUBTOTAL`, `MEAL_CREDIT_APPLIED`,
                     `TOTAL_AMOUNT`, `CREATED_AT`, `UPDATED_AT`)
                VALUES (@Id, @SessionId, @OrderNumber, 'service_addon', @ParentNomineeId, @Status,
                        @QueueEnteredAt, @SubmittedAt, CASE WHEN @Status = 'confirmed' THEN @SubmittedAt ELSE NULL END,
                        @TotalAmount, 0, @TotalAmount, @SubmittedAt, @SubmittedAt);
                INSERT INTO `ORDER_ITEMS`
                    (`ID`, `ORDER_ID`, `ITEM_TYPE`, `REFERENCE_ID`, `NAME_SNAPSHOT`, `UNIT_PRICE`, `QUANTITY`,
                     `SEGMENT_COUNT`, `DURATION_MINUTES`, `LINE_TOTAL`, `PRICE_RULE`, `SORT_ORDER`, `CREATED_AT`, `UPDATED_AT`)
                VALUES (@ItemId, @Id, 'staff_service_addon', @ServiceId, @ItemName, @UnitPrice, @Quantity,
                        @ItemSegmentCount, @ItemDurationMinutes, @ItemLineTotal, @ItemPriceRule, 0, @SubmittedAt, @SubmittedAt);
                INSERT INTO `ORDER_SERVICE_ADDONS`
                    (`ID`, `ORDER_ID`, `PARENT_NOMINEE_ID`, `STAFF_ID`, `STAFF_NAME_SNAPSHOT`, `SERVICE_ID`,
                     `SERVICE_NAME_SNAPSHOT`, `SEGMENT_COUNT`, `SERVICE_DURATION_MINUTES`, `PARTICIPANT_COUNT`,
                     `ADDON_STATUS`, `CONFIRMED_AT`, `CONFIRMED_BY`, `CREATED_AT`, `UPDATED_AT`)
                VALUES (@AddonId, @Id, @ParentNomineeId, @StaffId, @StaffName, @ServiceId,
                        @ServiceName, @SegmentCount, @ServiceDurationMinutes, @ParticipantCount,
                        @AddonStatus, CASE WHEN @AddonStatus = 'confirmed' THEN @SubmittedAt ELSE NULL END,
                        CASE WHEN @AddonStatus = 'confirmed' THEN @ActorId ELSE NULL END, @SubmittedAt, @SubmittedAt);
                """;
            await connection.ExecuteAsync(new CommandDefinition(sql, new
            {
                addon.Id,
                addon.SessionId,
                addon.OrderNumber,
                addon.ParentNomineeId,
                addon.Status,
                addon.QueueEnteredAt,
                addon.SubmittedAt,
                addon.TotalAmount,
                ItemId = addon.Item.Id,
                ItemName = addon.Item.Name,
                addon.Item.UnitPrice,
                addon.Item.Quantity,
                ItemSegmentCount = addon.Item.SegmentCount,
                ItemDurationMinutes = addon.Item.DurationMinutes,
                ItemLineTotal = addon.Item.LineTotal,
                ItemPriceRule = addon.Item.PriceRule,
                addon.ServiceId,
                addon.AddonId,
                addon.StaffId,
                addon.StaffName,
                addon.ServiceName,
                addon.SegmentCount,
                addon.ServiceDurationMinutes,
                addon.ParticipantCount,
                addon.AddonStatus,
                addon.ActorId
            }, transaction, cancellationToken: cancellationToken));
            await InsertHistoryAsync(connection, transaction, addon.Id, null, addon.Status,
                addon.ActorType == "customer" ? "顧客送出附掛式加購服務，等待被指名店員確認。" : "被指名店員代客送出並確認加購服務。",
                addon.ActorType, addon.ActorId, addon.SubmittedAt, cancellationToken);
            await InsertAuditAsync(connection, transaction, addon.Id, addon.SessionId, "order.addon.created", null,
                JsonSerializer.Serialize(addon), addon.ActorId ?? "customer", addon.ActorRole ?? addon.ActorType,
                addon.SubmittedAt, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task ConfirmAddonAsync(string orderId, string staffId, string actorId, DateTime now,
        CancellationToken cancellationToken)
    {
        await using var connection = await DbContext.CreateOpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            var addon = await connection.QuerySingleOrDefaultAsync<OrderAddonRow>(new CommandDefinition("""
                SELECT A.`ID` AS Id, A.`ORDER_ID` AS OrderId, A.`STAFF_ID` AS StaffId,
                       A.`ADDON_STATUS` AS AddonStatus, PO.`ORDER_STATUS` AS ParentOrderStatus,
                       N.`REQUESTED_SERVICE_ENDS_AT` AS ParentServiceEndsAt
                FROM `ORDER_SERVICE_ADDONS` A
                JOIN `ORDERS` O ON O.`ID` = A.`ORDER_ID`
                JOIN `ORDER_NOMINEES` N ON N.`ID` = A.`PARENT_NOMINEE_ID`
                JOIN `ORDERS` PO ON PO.`ID` = N.`ORDER_ID`
                WHERE A.`ORDER_ID` = @OrderId AND O.`ORDER_STATUS` = 'submitted' FOR UPDATE;
                """, new { OrderId = orderId }, transaction, cancellationToken: cancellationToken));
            if (addon is null) throw new BusinessException("找不到等待確認的加購服務。", "ADDON_NOT_CONFIRMABLE");
            if (addon.StaffId != staffId)
                throw new BusinessException("只有被指名店員本人可以確認此加購服務。", "ADDON_SCOPE_FORBIDDEN");
            if (addon.ParentOrderStatus is not ("confirmed" or "in_service") || addon.ParentServiceEndsAt <= now)
                throw new BusinessException("原指名已結束或失效，無法再確認此加購服務。", "ADDON_PARENT_INACTIVE");
            await connection.ExecuteAsync(new CommandDefinition("""
                UPDATE `ORDER_SERVICE_ADDONS`
                SET `ADDON_STATUS` = 'confirmed', `CONFIRMED_AT` = @Now, `CONFIRMED_BY` = @ActorId,
                    `UPDATED_AT` = @Now WHERE `ORDER_ID` = @OrderId;
                UPDATE `ORDERS`
                SET `ORDER_STATUS` = 'confirmed', `CONFIRMED_AT` = @Now, `UPDATED_AT` = @Now,
                    `UPDATED_BY` = @ActorId WHERE `ID` = @OrderId;
                """, new { Now = now, ActorId = actorId, OrderId = orderId }, transaction,
                cancellationToken: cancellationToken));
            await InsertHistoryAsync(connection, transaction, orderId, "submitted", "confirmed",
                "被指名店員已確認加購服務。", "staff", actorId, now, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task TransitionOrderAsync(string orderId, string action, string? reason,
        string actorId, string actorRole, DateTime now, CancellationToken cancellationToken)
    {
        await using var connection = await DbContext.CreateOpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            var order = await connection.QuerySingleOrDefaultAsync<OrderRow>(new CommandDefinition("""
                SELECT `ID` AS Id, `SESSION_ID` AS SessionId, `ORDER_KIND` AS OrderKind,
                       `ORDER_STATUS` AS OrderStatus,
                       `MEAL_CREDIT_APPLIED` AS MealCreditApplied
                FROM `ORDERS` WHERE `ID` = @OrderId FOR UPDATE;
                """, new { OrderId = orderId }, transaction, cancellationToken: cancellationToken))
                ?? throw new BusinessException("找不到訂單。", "ORDER_NOT_FOUND");
            var reservedEndsAt = await connection.ExecuteScalarAsync<DateTime?>(new CommandDefinition(
                "SELECT MAX(`REQUESTED_SERVICE_ENDS_AT`) FROM `ORDER_NOMINEES` WHERE `ORDER_ID` = @OrderId;",
                new { OrderId = orderId }, transaction, cancellationToken: cancellationToken));
            var isEarlyCompletion = action == "complete" && reservedEndsAt.HasValue && now < reservedEndsAt.Value;

            var (allowed, nextStatus, defaultReason) = action switch
            {
                "start" => (order.OrderStatus == "confirmed", "in_service", "店員開始執行訂單。"),
                "complete" => (order.OrderStatus == "in_service", "completed",
                    isEarlyCompletion ? "服務於預約結束前提早完成。" : "店員完成訂單。"),
                "cancel" => (order.OrderStatus is "submitted" or "partially_confirmed" or "needs_reschedule" or "confirmed",
                    "cancelled", "店員取消尚未完成的訂單。"),
                "reject" => (order.OrderStatus is "submitted" or "partially_confirmed" or "needs_reschedule",
                    "rejected", "店員退回等待訂單。"),
                "return_to_reschedule" => (order.OrderKind != "service_addon" && order.OrderStatus == "confirmed",
                    "needs_reschedule", "店員退回並要求重新排程。"),
                _ => (false, string.Empty, string.Empty)
            };
            if (!allowed)
                throw new BusinessException($"目前狀態「{order.OrderStatus}」不可執行此操作。", "ORDER_TRANSITION_INVALID");

            var transitionReason = string.IsNullOrWhiteSpace(reason) ? defaultReason : reason.Trim();
            if (action is "cancel" or "reject" or "return_to_reschedule" && string.IsNullOrWhiteSpace(reason))
                throw new BusinessException("取消、退回或要求重新排程時必須填寫原因。", "ORDER_TRANSITION_REASON_REQUIRED");
            if (isEarlyCompletion && string.IsNullOrWhiteSpace(reason))
                throw new BusinessException("實際提早完成必須填寫原因；此操作不會自動改變已成立訂單金額。",
                    "EARLY_COMPLETION_REASON_REQUIRED");

            await connection.ExecuteAsync(new CommandDefinition("""
                UPDATE `ORDERS`
                SET `ORDER_STATUS` = @NextStatus,
                    `QUEUE_ENTERED_AT` = CASE WHEN @Action = 'return_to_reschedule' THEN @Now ELSE `QUEUE_ENTERED_AT` END,
                    `CONFIRMED_AT` = CASE WHEN @Action = 'return_to_reschedule' THEN NULL ELSE `CONFIRMED_AT` END,
                    `STARTED_AT` = CASE WHEN @Action = 'start' THEN COALESCE(`STARTED_AT`, @Now) ELSE `STARTED_AT` END,
                    `COMPLETED_AT` = CASE WHEN @Action = 'complete' THEN COALESCE(`COMPLETED_AT`, @Now) ELSE `COMPLETED_AT` END,
                    `CANCELLED_AT` = CASE WHEN @Action IN ('cancel','reject') THEN COALESCE(`CANCELLED_AT`, @Now) ELSE `CANCELLED_AT` END,
                    `UPDATED_AT` = @Now, `UPDATED_BY` = @ActorId
                WHERE `ID` = @OrderId;
                """, new { NextStatus = nextStatus, Action = action, Now = now, ActorId = actorId, OrderId = orderId },
                transaction, cancellationToken: cancellationToken));

            if (order.OrderKind == "service_addon")
            {
                await connection.ExecuteAsync(new CommandDefinition("""
                    UPDATE `ORDER_SERVICE_ADDONS`
                    SET `ADDON_STATUS` = @NextStatus, `UPDATED_AT` = @Now
                    WHERE `ORDER_ID` = @OrderId;
                    """, new { NextStatus = nextStatus, Now = now, OrderId = orderId }, transaction,
                    cancellationToken: cancellationToken));
            }

            if (action is "cancel" or "reject")
            {
                await connection.ExecuteAsync(new CommandDefinition("""
                    UPDATE `STAFF_BUSY_BLOCKS` SET `BLOCK_STATUS` = 'released', `UPDATED_AT` = @Now
                    WHERE `ORDER_ID` = @OrderId AND `BLOCK_STATUS` = 'active';
                    UPDATE `ORDER_NOMINEES` SET `CONFIRMATION_STATUS` = @NextStatus, `UPDATED_AT` = @Now
                    WHERE `ORDER_ID` = @OrderId;
                    UPDATE `ORDER_SERVICE_ADDONS` SET `ADDON_STATUS` = @NextStatus, `UPDATED_AT` = @Now
                    WHERE `ORDER_ID` = @OrderId;
                    UPDATE `CUSTOMER_ORDER_SESSIONS`
                    SET `REMAINING_MEAL_CREDIT` = `REMAINING_MEAL_CREDIT` + @Credit, `UPDATED_AT` = @Now
                    WHERE `ID` = @SessionId;
                    """, new { Now = now, OrderId = orderId, NextStatus = nextStatus,
                        Credit = order.MealCreditApplied, order.SessionId }, transaction,
                    cancellationToken: cancellationToken));
            }
            else if (action == "return_to_reschedule")
            {
                await connection.ExecuteAsync(new CommandDefinition("""
                    UPDATE `STAFF_BUSY_BLOCKS` SET `BLOCK_STATUS` = 'released', `UPDATED_AT` = @Now
                    WHERE `ORDER_ID` = @OrderId AND `BLOCK_STATUS` = 'active';
                    UPDATE `ORDER_NOMINEES`
                    SET `CONFIRMATION_STATUS` = 'waiting', `CONFIRMED_AT` = NULL,
                        `CONFIRMED_BY` = NULL, `UPDATED_AT` = @Now
                    WHERE `ORDER_ID` = @OrderId;
                    """, new { Now = now, OrderId = orderId }, transaction, cancellationToken: cancellationToken));
            }
            else if (action == "complete")
            {
                await connection.ExecuteAsync(new CommandDefinition("""
                    UPDATE `STAFF_BUSY_BLOCKS` SET `BLOCK_STATUS` = 'completed', `UPDATED_AT` = @Now
                    WHERE `ORDER_ID` = @OrderId AND `BLOCK_STATUS` = 'active';
                    """, new { Now = now, OrderId = orderId }, transaction, cancellationToken: cancellationToken));
            }

            await InsertHistoryAsync(connection, transaction, orderId, order.OrderStatus, nextStatus,
                transitionReason, "admin", actorId, now, cancellationToken);
            var auditAction = isEarlyCompletion ? "order.complete_early" : $"order.{action}";
            await InsertAuditAsync(connection, transaction, orderId, order.SessionId, auditAction,
                JsonSerializer.Serialize(order), JsonSerializer.Serialize(new
                {
                    action,
                    reason = transitionReason,
                    actualCompletedAt = action == "complete" ? now : (DateTime?)null,
                    reservedEndsAt,
                    isEarlyCompletion
                }),
                actorId, actorRole, now, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task UpdateOrderItemAsync(string orderId, string itemId, string? name, int? unitPrice, int? quantity,
        string actorId, string actorRole, DateTime now, CancellationToken cancellationToken)
    {
        await using var connection = await DbContext.CreateOpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var item = await LockEditableItemAsync(connection, transaction, orderId, itemId, cancellationToken);
        if (item.ItemType is "nomination_base" or "staff_service" && quantity.HasValue && quantity != item.Quantity)
            throw new BusinessException("已送出的指名訂單不可延長節數，請另開新訂單。", "NOMINATION_EXTENSION_FORBIDDEN");
        var nextName = string.IsNullOrWhiteSpace(name) ? item.NameSnapshot : name.Trim();
        var nextPrice = unitPrice ?? item.UnitPrice;
        var nextQuantity = quantity ?? item.Quantity;
        var lineTotal = nextPrice * nextQuantity;
        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE `ORDER_ITEMS` SET `NAME_SNAPSHOT` = @Name, `UNIT_PRICE` = @UnitPrice,
                   `QUANTITY` = @Quantity, `LINE_TOTAL` = @LineTotal, `UPDATED_AT` = @Now,
                   `UPDATED_BY` = @ActorId WHERE `ID` = @ItemId;
            """, new { Name = nextName, UnitPrice = nextPrice, Quantity = nextQuantity, LineTotal = lineTotal,
                Now = now, ActorId = actorId, ItemId = itemId }, transaction, cancellationToken: cancellationToken));
        await RecalculateOrderAsync(connection, transaction, orderId, now, actorId, cancellationToken);
        await InsertAuditAsync(connection, transaction, orderId, null, "order.item.updated", JsonSerializer.Serialize(item),
            JsonSerializer.Serialize(new { name = nextName, unitPrice = nextPrice, quantity = nextQuantity }), actorId,
            actorRole, now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task DeleteOrderItemAsync(string orderId, string itemId, string actorId, string actorRole,
        DateTime now, CancellationToken cancellationToken)
    {
        await using var connection = await DbContext.CreateOpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var item = await LockEditableItemAsync(connection, transaction, orderId, itemId, cancellationToken);
        if (item.ItemType == "nomination_base")
        {
            var childCount = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                "SELECT COUNT(*) FROM `ORDER_ITEMS` WHERE `ORDER_ID` = @OrderId AND `PARENT_ITEM_ID` = @ItemId;",
                new { OrderId = orderId, ItemId = itemId }, transaction, cancellationToken: cancellationToken));
            if (childCount > 0)
                throw new BusinessException("請先刪除服務項目，才能刪除基礎指名費。", "DELETE_SERVICE_BEFORE_BASE_FEE");
            await connection.ExecuteAsync(new CommandDefinition("""
                UPDATE `STAFF_BUSY_BLOCKS` B
                JOIN `ORDER_NOMINEES` N ON N.`ID` = B.`ORDER_NOMINEE_ID`
                SET B.`BLOCK_STATUS` = 'released', B.`UPDATED_AT` = @Now
                WHERE N.`ORDER_ID` = @OrderId AND N.`STAFF_ID` = @StaffId
                  AND B.`BLOCK_STATUS` = 'active';
                DELETE FROM `ORDER_NOMINEES`
                WHERE `ORDER_ID` = @OrderId AND `STAFF_ID` = @StaffId;
                """, new { OrderId = orderId, StaffId = item.ReferenceId, Now = now }, transaction,
                cancellationToken: cancellationToken));
        }
        if (item.ItemType == "staff_service")
        {
            await connection.ExecuteAsync(new CommandDefinition("""
                UPDATE `STAFF_BUSY_BLOCKS` B
                JOIN `ORDER_NOMINEES` N ON N.`ID` = B.`ORDER_NOMINEE_ID`
                SET B.`BLOCK_STATUS` = 'released', B.`UPDATED_AT` = @Now
                WHERE N.`ORDER_ID` = @OrderId AND N.`SERVICE_ID` = @ServiceId
                  AND B.`BLOCK_STATUS` = 'active';
                DELETE FROM `ORDER_NOMINEES` WHERE `ORDER_ID` = @OrderId AND `SERVICE_ID` = @ServiceId;
                """, new { OrderId = orderId, ServiceId = item.ReferenceId, Now = now }, transaction,
                cancellationToken: cancellationToken));
        }
        if (item.ItemType == "tip")
            await connection.ExecuteAsync(new CommandDefinition("DELETE FROM `ORDER_TIPS` WHERE `ORDER_ITEM_ID` = @ItemId;",
                new { ItemId = itemId }, transaction, cancellationToken: cancellationToken));
        await connection.ExecuteAsync(new CommandDefinition("DELETE FROM `ORDER_ITEMS` WHERE `ID` = @ItemId;",
            new { ItemId = itemId }, transaction, cancellationToken: cancellationToken));
        await RecalculateOrderAsync(connection, transaction, orderId, now, actorId, cancellationToken);
        var remainingNominees = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM `ORDER_NOMINEES` WHERE `ORDER_ID` = @OrderId;",
            new { OrderId = orderId }, transaction, cancellationToken: cancellationToken));
        if (remainingNominees == 0)
        {
            var currentStatus = await connection.ExecuteScalarAsync<string>(new CommandDefinition(
                "SELECT `ORDER_STATUS` FROM `ORDERS` WHERE `ID` = @OrderId FOR UPDATE;",
                new { OrderId = orderId }, transaction, cancellationToken: cancellationToken));
            if (currentStatus is "submitted" or "partially_confirmed" or "needs_reschedule")
            {
                await connection.ExecuteAsync(new CommandDefinition("""
                    UPDATE `ORDERS` SET `ORDER_STATUS` = 'confirmed', `QUEUE_ENTERED_AT` = NULL,
                           `CONFIRMED_AT` = @Now, `UPDATED_AT` = @Now WHERE `ID` = @OrderId;
                    """, new { Now = now, OrderId = orderId }, transaction, cancellationToken: cancellationToken));
                await InsertHistoryAsync(connection, transaction, orderId, currentStatus, "confirmed",
                    "指名服務已移除，其餘訂單內容直接成立。", "admin", actorId, now, cancellationToken);
            }
        }
        await InsertAuditAsync(connection, transaction, orderId, null, "order.item.deleted", JsonSerializer.Serialize(item),
            null, actorId, actorRole, now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task CancelPendingOrderAsync(string orderId, string? reason, string actorId, string actorRole,
        DateTime now, CancellationToken cancellationToken)
    {
        await using var connection = await DbContext.CreateOpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var order = await connection.QuerySingleOrDefaultAsync<OrderRow>(new CommandDefinition("""
            SELECT `ID` AS Id, `SESSION_ID` AS SessionId, `ORDER_STATUS` AS OrderStatus,
                   `MEAL_CREDIT_APPLIED` AS MealCreditApplied FROM `ORDERS` WHERE `ID` = @OrderId FOR UPDATE;
            """, new { OrderId = orderId }, transaction, cancellationToken: cancellationToken))
            ?? throw new BusinessException("找不到訂單。", "ORDER_NOT_FOUND");
        if (order.OrderStatus is not ("submitted" or "partially_confirmed" or "needs_reschedule"))
            throw new BusinessException("只能刪除尚未執行的等待訂單。", "ORDER_DELETE_FORBIDDEN");
        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE `ORDERS` SET `ORDER_STATUS` = 'cancelled', `CANCELLED_AT` = @Now,
                   `UPDATED_AT` = @Now, `UPDATED_BY` = @ActorId WHERE `ID` = @OrderId;
            UPDATE `ORDER_NOMINEES` SET `CONFIRMATION_STATUS` = 'cancelled', `UPDATED_AT` = @Now
            WHERE `ORDER_ID` = @OrderId;
            UPDATE `ORDER_SERVICE_ADDONS` SET `ADDON_STATUS` = 'cancelled', `UPDATED_AT` = @Now
            WHERE `ORDER_ID` = @OrderId;
            UPDATE `CUSTOMER_ORDER_SESSIONS`
            SET `REMAINING_MEAL_CREDIT` = `REMAINING_MEAL_CREDIT` + @Credit, `UPDATED_AT` = @Now
            WHERE `ID` = @SessionId;
            """, new { Now = now, ActorId = actorId, OrderId = orderId, Credit = order.MealCreditApplied,
                order.SessionId }, transaction, cancellationToken: cancellationToken));
        await InsertHistoryAsync(connection, transaction, orderId, order.OrderStatus, "cancelled", reason,
            "admin", actorId, now, cancellationToken);
        await InsertAuditAsync(connection, transaction, orderId, order.SessionId, "order.cancelled", null,
            JsonSerializer.Serialize(new { reason }), actorId, actorRole, now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<(DateOnly BusinessDate, int OrderCount, int GrossAmount, int MealCreditApplied,
        int NetAmount, int StaffTipAmount, int StoreTipAmount)>> GetReportAsync(DateOnly from, DateOnly to,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT S.`BUSINESS_DATE` AS BusinessDate, COUNT(DISTINCT O.`ID`) AS OrderCount,
                   COALESCE(SUM(O.`SUBTOTAL`), 0) AS GrossAmount,
                   COALESCE(SUM(O.`MEAL_CREDIT_APPLIED`), 0) AS MealCreditApplied,
                   COALESCE(SUM(O.`TOTAL_AMOUNT`), 0) AS NetAmount,
                   COALESCE((SELECT SUM(T.`STAFF_AMOUNT`) FROM `ORDER_TIPS` T JOIN `ORDERS` OT ON OT.`ID` = T.`ORDER_ID`
                       JOIN `CUSTOMER_ORDER_SESSIONS` ST ON ST.`ID` = OT.`SESSION_ID`
                       WHERE ST.`BUSINESS_DATE` = S.`BUSINESS_DATE` AND OT.`ORDER_STATUS` NOT IN ('cancelled','expired','rejected')), 0) AS StaffTipAmount,
                   COALESCE((SELECT SUM(T.`STORE_AMOUNT`) FROM `ORDER_TIPS` T JOIN `ORDERS` OT ON OT.`ID` = T.`ORDER_ID`
                       JOIN `CUSTOMER_ORDER_SESSIONS` ST ON ST.`ID` = OT.`SESSION_ID`
                       WHERE ST.`BUSINESS_DATE` = S.`BUSINESS_DATE` AND OT.`ORDER_STATUS` NOT IN ('cancelled','expired','rejected')), 0) AS StoreTipAmount
            FROM `CUSTOMER_ORDER_SESSIONS` S JOIN `ORDERS` O ON O.`SESSION_ID` = S.`ID`
            WHERE S.`BUSINESS_DATE` BETWEEN @From AND @To AND O.`ORDER_STATUS` NOT IN ('cancelled','expired','rejected')
            GROUP BY S.`BUSINESS_DATE` ORDER BY S.`BUSINESS_DATE`;
            """;
        await using var connection = await DbContext.CreateOpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<ReportRow>(new CommandDefinition(sql,
            new { From = from.ToDateTime(TimeOnly.MinValue), To = to.ToDateTime(TimeOnly.MinValue) },
            cancellationToken: cancellationToken));
        return rows.Select(row => (DateOnly.FromDateTime(row.BusinessDate), row.OrderCount, row.GrossAmount,
            row.MealCreditApplied, row.NetAmount, row.StaffTipAmount, row.StoreTipAmount)).ToArray();
    }

    private static async Task<OrderItemRow> LockEditableItemAsync(MySqlConnection connection, MySqlTransaction transaction,
        string orderId, string itemId, CancellationToken cancellationToken)
    {
        var item = await connection.QuerySingleOrDefaultAsync<OrderItemRow>(new CommandDefinition("""
            SELECT I.`ID` AS Id, I.`ORDER_ID` AS OrderId, I.`ITEM_TYPE` AS ItemType,
                   I.`REFERENCE_ID` AS ReferenceId, I.`PARENT_ITEM_ID` AS ParentItemId,
                   I.`NAME_SNAPSHOT` AS NameSnapshot, I.`UNIT_PRICE` AS UnitPrice,
                   I.`QUANTITY` AS Quantity, I.`LINE_TOTAL` AS LineTotal
            FROM `ORDER_ITEMS` I JOIN `ORDERS` O ON O.`ID` = I.`ORDER_ID`
            WHERE I.`ID` = @ItemId AND I.`ORDER_ID` = @OrderId
              AND O.`ORDER_STATUS` NOT IN ('in_service','completed','cancelled','expired','rejected') FOR UPDATE;
            """, new { ItemId = itemId, OrderId = orderId }, transaction, cancellationToken: cancellationToken));
        return item ?? throw new BusinessException("找不到可修改的訂單項目。", "ORDER_ITEM_LOCKED");
    }

    private static async Task RecalculateOrderAsync(MySqlConnection connection, MySqlTransaction transaction,
        string orderId, DateTime now, string actorId, CancellationToken cancellationToken)
    {
        var order = await connection.QuerySingleAsync<OrderRow>(new CommandDefinition("""
            SELECT `ID` AS Id, `SESSION_ID` AS SessionId, `MEAL_CREDIT_APPLIED` AS MealCreditApplied
            FROM `ORDERS` WHERE `ID` = @OrderId FOR UPDATE;
            """, new { OrderId = orderId }, transaction, cancellationToken: cancellationToken));
        var mealSubtotal = await connection.ExecuteScalarAsync<int>(new CommandDefinition("""
            SELECT COALESCE(SUM(`LINE_TOTAL`), 0) FROM `ORDER_ITEMS`
            WHERE `ORDER_ID` = @OrderId AND `ITEM_TYPE` IN ('menu_item', 'menu_set');
            """, new { OrderId = orderId }, transaction, cancellationToken: cancellationToken));
        var nextCredit = Math.Min(order.MealCreditApplied, mealSubtotal);
        var creditRefund = order.MealCreditApplied - nextCredit;
        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE `ORDERS` O SET
                O.`SUBTOTAL` = (SELECT COALESCE(SUM(I.`LINE_TOTAL`), 0) FROM `ORDER_ITEMS` I WHERE I.`ORDER_ID` = O.`ID`),
                O.`MEAL_CREDIT_APPLIED` = @NextCredit,
                O.`TOTAL_AMOUNT` = GREATEST(0, (SELECT COALESCE(SUM(I.`LINE_TOTAL`), 0) FROM `ORDER_ITEMS` I WHERE I.`ORDER_ID` = O.`ID`) - @NextCredit),
                O.`UPDATED_AT` = @Now, O.`UPDATED_BY` = @ActorId WHERE O.`ID` = @OrderId;
            UPDATE `CUSTOMER_ORDER_SESSIONS`
            SET `REMAINING_MEAL_CREDIT` = `REMAINING_MEAL_CREDIT` + @CreditRefund, `UPDATED_AT` = @Now
            WHERE `ID` = @SessionId AND @CreditRefund > 0;
            """, new { Now = now, ActorId = actorId, OrderId = orderId, NextCredit = nextCredit,
                CreditRefund = creditRefund, order.SessionId }, transaction,
            cancellationToken: cancellationToken));
    }

    private static async Task<bool> HasConflictAsync(MySqlConnection connection, MySqlTransaction transaction,
        string staffId, DateTime startsAt, DateTime endsAt, CancellationToken cancellationToken)
        => await connection.ExecuteScalarAsync<int>(new CommandDefinition("""
            SELECT COUNT(*) FROM `STAFF_BUSY_BLOCKS`
            WHERE `STAFF_ID` = @StaffId AND `BLOCK_STATUS` = 'active'
              AND `ENDS_AT` > @StartsAt AND `STARTS_AT` < @EndsAt;
            """, new { StaffId = staffId, StartsAt = startsAt, EndsAt = endsAt }, transaction,
            cancellationToken: cancellationToken)) > 0;

    private static async Task LockStaffRowsAsync(MySqlConnection connection, MySqlTransaction transaction,
        IEnumerable<string> staffIds, CancellationToken cancellationToken)
    {
        foreach (var staffId in staffIds.Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal))
        {
            var lockedId = await connection.ExecuteScalarAsync<string?>(new CommandDefinition(
                "SELECT `ID` FROM `STAFF_MEMBERS` WHERE `ID` = @StaffId FOR UPDATE;",
                new { StaffId = staffId }, transaction, cancellationToken: cancellationToken));
            if (lockedId is null)
                throw new BusinessException("被指名店員資料已不存在。", "NOMINEE_STAFF_NOT_FOUND");
        }
    }

    private static async Task ReturnToRescheduleAsync(MySqlConnection connection, MySqlTransaction transaction,
        string orderId, string fromStatus, string reason, string actorId, DateTime now,
        CancellationToken cancellationToken)
    {
        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE `ORDERS`
            SET `ORDER_STATUS` = 'needs_reschedule', `QUEUE_ENTERED_AT` = @Now,
                `CONFIRMED_AT` = NULL, `UPDATED_AT` = @Now
            WHERE `ID` = @OrderId;
            UPDATE `STAFF_BUSY_BLOCKS` SET `BLOCK_STATUS` = 'released', `UPDATED_AT` = @Now
            WHERE `ORDER_ID` = @OrderId AND `BLOCK_STATUS` = 'active';
            UPDATE `ORDER_NOMINEES` SET `CONFIRMATION_STATUS` = 'waiting', `CONFIRMED_AT` = NULL,
                   `CONFIRMED_BY` = NULL, `UPDATED_AT` = @Now WHERE `ORDER_ID` = @OrderId;
            """, new { Now = now, OrderId = orderId }, transaction, cancellationToken: cancellationToken));
        await InsertHistoryAsync(connection, transaction, orderId, fromStatus, "needs_reschedule", reason,
            "staff", actorId, now, cancellationToken);
    }

    private static async Task InsertHistoryAsync(MySqlConnection connection, MySqlTransaction transaction,
        string orderId, string? fromStatus, string toStatus, string? reason, string actorType,
        string? actorId, DateTime now, CancellationToken cancellationToken)
        => await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO `ORDER_STATUS_HISTORY`
                (`ID`, `ORDER_ID`, `FROM_STATUS`, `TO_STATUS`, `REASON`, `ACTOR_TYPE`, `ACTOR_ID`, `CREATED_AT`)
            VALUES (@Id, @OrderId, @FromStatus, @ToStatus, @Reason, @ActorType, @ActorId, @Now);
            """, new { Id = NewId(), OrderId = orderId, FromStatus = fromStatus, ToStatus = toStatus,
                Reason = reason, ActorType = actorType, ActorId = actorId, Now = now }, transaction,
            cancellationToken: cancellationToken));

    private static async Task InsertAuditAsync(MySqlConnection connection, MySqlTransaction transaction,
        string? orderId, string? sessionId, string action, string? beforeJson, string? afterJson,
        string actorId, string actorRole, DateTime now, CancellationToken cancellationToken)
        => await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO `ORDER_AUDIT_LOG`
                (`ID`, `ORDER_ID`, `SESSION_ID`, `ACTION_TYPE`, `BEFORE_JSON`, `AFTER_JSON`,
                 `ACTOR_ID`, `ACTOR_ROLE`, `CREATED_AT`)
            VALUES (@Id, @OrderId, @SessionId, @Action, @BeforeJson, @AfterJson, @ActorId, @ActorRole, @Now);
            """, new { Id = NewId(), OrderId = orderId, SessionId = sessionId, Action = action,
                BeforeJson = beforeJson, AfterJson = afterJson, ActorId = actorId, ActorRole = actorRole, Now = now },
            transaction, cancellationToken: cancellationToken));

    private const string SessionColumns = """
        S.`ID` AS Id, S.`GAME_ID` AS GameId, S.`CUSTOMER_NAME` AS CustomerName,
        S.`BUSINESS_DATE` AS BusinessDate, S.`ACCESS_TOKEN_HASH` AS AccessTokenHash,
        S.`RECOVERY_CODE_HASH` AS RecoveryCodeHash, S.`MAX_NOMINATED_STAFF` AS MaxNominatedStaff,
        S.`PREPAID_MEAL_CREDIT` AS PrepaidMealCredit, S.`REMAINING_MEAL_CREDIT` AS RemainingMealCredit,
        S.`SESSION_STATUS` AS SessionStatus, S.`LAST_ACCESSED_AT` AS LastAccessedAt, S.`CREATED_AT` AS CreatedAt
        """;

    private static string NewId() => Guid.NewGuid().ToString("D");

    private sealed class ReportRow
    {
        public DateTime BusinessDate { get; set; }
        public int OrderCount { get; set; }
        public int GrossAmount { get; set; }
        public int MealCreditApplied { get; set; }
        public int NetAmount { get; set; }
        public int StaffTipAmount { get; set; }
        public int StoreTipAmount { get; set; }
    }

    private sealed class NominationEditRow
    {
        public string NomineeId { get; set; } = string.Empty;
        public string OrderId { get; set; } = string.Empty;
        public string StaffId { get; set; } = string.Empty;
        public string ServiceId { get; set; } = string.Empty;
        public string OrderStatus { get; set; } = string.Empty;
        public int SegmentCount { get; set; }
        public int ServiceDurationMinutes { get; set; }
        public int SegmentMinutesSnapshot { get; set; }
        public int ReservedMinutes { get; set; }
        public int BufferMinutesSnapshot { get; set; }
        public DateTime RequestedStartsAt { get; set; }
        public DateTime RequestedServiceEndsAt { get; set; }
        public DateTime RequestedBusyUntil { get; set; }
        public string? ServiceItemId { get; set; }
        public string ServicePriceRule { get; set; } = string.Empty;
        public string BaseItemId { get; set; } = string.Empty;
    }
}
