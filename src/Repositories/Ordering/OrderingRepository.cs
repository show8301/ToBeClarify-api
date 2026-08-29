using System.Text.Json;
using Dapper;
using MySqlConnector;
using ToBeClarify.Api.Exceptions;
using ToBeClarify.Api.Infrastructure;
using ToBeClarify.Api.Models.Entities;
using ToBeClarify.Api.Repositories.Shared;

namespace ToBeClarify.Api.Repositories.Ordering;

public sealed class OrderingRepository : DapperRepositoryBase, IOrderingRepository
{
    public OrderingRepository(AppDbContext dbContext) : base(dbContext) { }

    public async Task<OrderingSettingsRow> GetSettingsAsync(CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT `MINIMUM_MEAL_CREDIT` AS MinimumMealCredit, `BASE_NOMINATION_FEE` AS BaseNominationFee,
                   `SEGMENT_MINUTES` AS SegmentMinutes, `REMINDER_AFTER_MINUTES` AS ReminderAfterMinutes,
                   `ESCALATE_AFTER_MINUTES` AS EscalateAfterMinutes, `EXPIRE_AFTER_MINUTES` AS ExpireAfterMinutes,
                   `NOMINATION_PAUSED_UNTIL` AS NominationPausedUntil
            FROM `ORDERING_SETTINGS` WHERE `ID` = 'default' LIMIT 1;
            """;
        return await QuerySingleOrDefaultAsync<OrderingSettingsRow>(sql, null, cancellationToken)
            ?? new OrderingSettingsRow { SegmentMinutes = 20, ReminderAfterMinutes = 5, EscalateAfterMinutes = 10, ExpireAfterMinutes = 20 };
    }

    public async Task SaveSettingsAsync(OrderingSettingsRow settings, string actorId, DateTime now, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO `ORDERING_SETTINGS`
                (`ID`, `MINIMUM_MEAL_CREDIT`, `BASE_NOMINATION_FEE`, `SEGMENT_MINUTES`,
                 `REMINDER_AFTER_MINUTES`, `ESCALATE_AFTER_MINUTES`, `EXPIRE_AFTER_MINUTES`, `UPDATED_AT`, `UPDATED_BY`)
            VALUES ('default', @MinimumMealCredit, @BaseNominationFee, @SegmentMinutes,
                    @ReminderAfterMinutes, @EscalateAfterMinutes, @ExpireAfterMinutes, @Now, @ActorId)
            ON DUPLICATE KEY UPDATE
                `MINIMUM_MEAL_CREDIT` = VALUES(`MINIMUM_MEAL_CREDIT`),
                `BASE_NOMINATION_FEE` = VALUES(`BASE_NOMINATION_FEE`),
                `SEGMENT_MINUTES` = VALUES(`SEGMENT_MINUTES`),
                `REMINDER_AFTER_MINUTES` = VALUES(`REMINDER_AFTER_MINUTES`),
                `ESCALATE_AFTER_MINUTES` = VALUES(`ESCALATE_AFTER_MINUTES`),
                `EXPIRE_AFTER_MINUTES` = VALUES(`EXPIRE_AFTER_MINUTES`),
                `UPDATED_AT` = VALUES(`UPDATED_AT`), `UPDATED_BY` = VALUES(`UPDATED_BY`);
            """;
        await using var connection = await DbContext.CreateOpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(sql, new
        {
            settings.MinimumMealCredit, settings.BaseNominationFee, settings.SegmentMinutes,
            settings.ReminderAfterMinutes, settings.EscalateAfterMinutes, settings.ExpireAfterMinutes,
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

    public async Task<StaffOfferRow?> GetStaffOfferAsync(string staffId, string serviceId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT M.`ID` AS StaffId, M.`DISPLAY_NAME` AS StaffName, M.`IS_WORKING_TODAY` AS IsWorkingToday,
                   M.`IS_NOMINATABLE` AS StaffIsNominatable, M.`BUFFER_MINUTES` AS BufferMinutes,
                   S.`ID` AS ServiceId, S.`SERVICE_NAME` AS ServiceName, S.`PRICE` AS Price,
                   S.`DURATION_MINUTES` AS DurationMinutes, S.`IS_NOMINATABLE` AS ServiceIsNominatable,
                   S.`ADDITIONAL_PERSON_PRICE` AS AdditionalPersonPrice, S.`IS_ENABLED` AS ServiceIsEnabled
            FROM `STAFF_MEMBERS` M
            JOIN `STAFF_SERVICES` S ON S.`STAFF_ID` = M.`ID`
            WHERE M.`ID` = @StaffId AND S.`ID` = @ServiceId AND M.`IS_ACTIVE` = TRUE LIMIT 1;
            """;
        return await QuerySingleOrDefaultAsync<StaffOfferRow>(sql, new { StaffId = staffId, ServiceId = serviceId }, cancellationToken);
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
                    (`ID`, `SESSION_ID`, `ORDER_NUMBER`, `ORDER_STATUS`, `QUEUE_ENTERED_AT`, `SUBMITTED_AT`,
                     `CONFIRMED_AT`, `SUBTOTAL`, `MEAL_CREDIT_APPLIED`, `TOTAL_AMOUNT`, `CUSTOMER_NOTE`, `CREATED_AT`, `UPDATED_AT`)
                VALUES (@Id, @SessionId, @OrderNumber, @Status, @QueueEnteredAt, @SubmittedAt,
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
                         `REQUESTED_BUSY_UNTIL`, `CONFIRMATION_STATUS`, `CREATED_AT`, `UPDATED_AT`)
                    VALUES (@Id, @OrderId, @StaffId, @StaffName, @ServiceId, @ServiceName,
                            @SegmentCount, @ServiceDurationMinutes, @StartsAt, @ServiceEndsAt,
                            @BusyUntil, 'waiting', @Now, @Now);
                    """;
                await connection.ExecuteAsync(new CommandDefinition(insertNominee,
                    order.Nominees.Select(item => new { item.Id, OrderId = order.Id, item.StaffId, item.StaffName,
                        item.ServiceId, item.ServiceName, item.SegmentCount, item.ServiceDurationMinutes,
                        item.StartsAt, item.ServiceEndsAt, item.BusyUntil, Now = order.SubmittedAt }),
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
                   N.`SERVICE_NAME_SNAPSHOT` AS ServiceNameSnapshot, N.`SEGMENT_COUNT` AS SegmentCount,
                   N.`SERVICE_DURATION_MINUTES` AS ServiceDurationMinutes,
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
            """;
        await using var connection = await DbContext.CreateOpenConnectionAsync(cancellationToken);
        using var grid = await connection.QueryMultipleAsync(new CommandDefinition(sql, new { Value = value }, cancellationToken: cancellationToken));
        return new OrderBundle(
            (await grid.ReadAsync<OrderRow>()).AsList(),
            (await grid.ReadAsync<OrderItemRow>()).AsList(),
            (await grid.ReadAsync<OrderNomineeRow>()).AsList(),
            (await grid.ReadAsync<OrderTipRow>()).AsList(),
            (await grid.ReadAsync<OrderHistoryRow>()).AsList());
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

            var nominee = await connection.QuerySingleOrDefaultAsync<OrderNomineeRow>(new CommandDefinition("""
                SELECT `ID` AS Id, `ORDER_ID` AS OrderId, `STAFF_ID` AS StaffId,
                       `REQUESTED_STARTS_AT` AS RequestedStartsAt,
                       `REQUESTED_SERVICE_ENDS_AT` AS RequestedServiceEndsAt,
                       `REQUESTED_BUSY_UNTIL` AS RequestedBusyUntil,
                       `CONFIRMATION_STATUS` AS ConfirmationStatus
                FROM `ORDER_NOMINEES` WHERE `ORDER_ID` = @OrderId AND `STAFF_ID` = @StaffId FOR UPDATE;
                """, new { OrderId = orderId, StaffId = staffId }, transaction, cancellationToken: cancellationToken));
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

            var nominees = (await connection.QueryAsync<OrderNomineeRow>(new CommandDefinition("""
                SELECT `ID` AS Id, `ORDER_ID` AS OrderId, `STAFF_ID` AS StaffId,
                       `REQUESTED_STARTS_AT` AS RequestedStartsAt,
                       `REQUESTED_SERVICE_ENDS_AT` AS RequestedServiceEndsAt,
                       `REQUESTED_BUSY_UNTIL` AS RequestedBusyUntil,
                       `CONFIRMATION_STATUS` AS ConfirmationStatus
                FROM `ORDER_NOMINEES` WHERE `ORDER_ID` = @OrderId FOR UPDATE;
                """, new { OrderId = orderId }, transaction, cancellationToken: cancellationToken))).AsList();
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
        var status = await connection.ExecuteScalarAsync<string?>(new CommandDefinition(
            "SELECT `ORDER_STATUS` FROM `ORDERS` WHERE `ID` = @OrderId FOR UPDATE;", new { OrderId = orderId },
            transaction, cancellationToken: cancellationToken));
        if (status is not ("submitted" or "partially_confirmed" or "needs_reschedule"))
            throw new BusinessException("此訂單目前不可重新排程。", "ORDER_NOT_RESCHEDULABLE");
        var nominees = (await connection.QueryAsync<OrderNomineeRow>(new CommandDefinition("""
            SELECT `ID` AS Id, `SERVICE_DURATION_MINUTES` AS ServiceDurationMinutes,
                   TIMESTAMPDIFF(MINUTE, `REQUESTED_SERVICE_ENDS_AT`, `REQUESTED_BUSY_UNTIL`) AS SegmentCount
            FROM `ORDER_NOMINEES` WHERE `ORDER_ID` = @OrderId FOR UPDATE;
            """, new { OrderId = orderId }, transaction, cancellationToken: cancellationToken))).AsList();
        foreach (var nominee in nominees)
        {
            var serviceEnds = startsAt.AddMinutes(nominee.ServiceDurationMinutes);
            var buffer = Math.Max(0, nominee.SegmentCount);
            await connection.ExecuteAsync(new CommandDefinition("""
                UPDATE `ORDER_NOMINEES`
                SET `REQUESTED_STARTS_AT` = @StartsAt, `REQUESTED_SERVICE_ENDS_AT` = @ServiceEnds,
                    `REQUESTED_BUSY_UNTIL` = @BusyUntil, `CONFIRMATION_STATUS` = 'waiting',
                    `CONFIRMED_AT` = NULL, `CONFIRMED_BY` = NULL, `UPDATED_AT` = @Now WHERE `ID` = @Id;
                """, new { StartsAt = startsAt, ServiceEnds = serviceEnds, BusyUntil = serviceEnds.AddMinutes(buffer),
                    Now = now, nominee.Id }, transaction, cancellationToken: cancellationToken));
        }
        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE `ORDERS` SET `ORDER_STATUS` = 'submitted', `QUEUE_ENTERED_AT` = @Now,
                   `CONFIRMED_AT` = NULL, `UPDATED_AT` = @Now, `UPDATED_BY` = @ActorId WHERE `ID` = @OrderId;
            """, new { Now = now, ActorId = actorId, OrderId = orderId }, transaction, cancellationToken: cancellationToken));
        await InsertHistoryAsync(connection, transaction, orderId, status, "submitted", "重新安排指名時段",
            "admin", actorId, now, cancellationToken);
        await InsertAuditAsync(connection, transaction, orderId, null, "order.rescheduled", null,
            JsonSerializer.Serialize(new { startsAt }), actorId, actorRole, now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task UpdateOrderAsync(string orderId, string? customerNote, string? internalNote, string? status,
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
        var nextStatus = status ?? current.OrderStatus;
        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE `ORDERS` SET `CUSTOMER_NOTE` = COALESCE(@CustomerNote, `CUSTOMER_NOTE`),
                   `INTERNAL_NOTE` = COALESCE(@InternalNote, `INTERNAL_NOTE`),
                   `ORDER_STATUS` = @Status,
                   `STARTED_AT` = CASE WHEN @Status = 'in_service' THEN COALESCE(`STARTED_AT`, @Now) ELSE `STARTED_AT` END,
                   `COMPLETED_AT` = CASE WHEN @Status = 'completed' THEN COALESCE(`COMPLETED_AT`, @Now) ELSE `COMPLETED_AT` END,
                   `CANCELLED_AT` = CASE WHEN @Status IN ('cancelled', 'rejected') THEN COALESCE(`CANCELLED_AT`, @Now) ELSE `CANCELLED_AT` END,
                   `UPDATED_AT` = @Now, `UPDATED_BY` = @ActorId WHERE `ID` = @OrderId;
            """, new { CustomerNote = customerNote, InternalNote = internalNote, Status = nextStatus,
                Now = now, ActorId = actorId, OrderId = orderId }, transaction, cancellationToken: cancellationToken));
        if (nextStatus is "cancelled" or "rejected")
        {
            await connection.ExecuteAsync(new CommandDefinition("""
                UPDATE `STAFF_BUSY_BLOCKS` SET `BLOCK_STATUS` = 'released', `UPDATED_AT` = @Now
                WHERE `ORDER_ID` = @OrderId AND `BLOCK_STATUS` = 'active';
                UPDATE `ORDER_NOMINEES` SET `CONFIRMATION_STATUS` = @Status, `UPDATED_AT` = @Now
                WHERE `ORDER_ID` = @OrderId;
                UPDATE `CUSTOMER_ORDER_SESSIONS`
                SET `REMAINING_MEAL_CREDIT` = `REMAINING_MEAL_CREDIT` + @Credit, `UPDATED_AT` = @Now
                WHERE `ID` = @SessionId;
                """, new { Now = now, OrderId = orderId, Status = nextStatus,
                    Credit = current.MealCreditApplied, current.SessionId }, transaction,
                cancellationToken: cancellationToken));
        }
        else if (nextStatus == "completed")
        {
            await connection.ExecuteAsync(new CommandDefinition("""
                UPDATE `STAFF_BUSY_BLOCKS` SET `BLOCK_STATUS` = 'completed', `UPDATED_AT` = @Now
                WHERE `ORDER_ID` = @OrderId AND `BLOCK_STATUS` = 'active';
                """, new { Now = now, OrderId = orderId }, transaction, cancellationToken: cancellationToken));
        }
        if (nextStatus != current.OrderStatus)
            await InsertHistoryAsync(connection, transaction, orderId, current.OrderStatus, nextStatus,
                "店員緊急調整訂單狀態", "admin", actorId, now, cancellationToken);
        await InsertAuditAsync(connection, transaction, orderId, null, "order.updated", JsonSerializer.Serialize(current),
            JsonSerializer.Serialize(new { customerNote, internalNote, status = nextStatus }), actorId, actorRole, now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
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
        }
        if (item.ItemType == "staff_service")
        {
            await connection.ExecuteAsync(new CommandDefinition("""
                DELETE FROM `ORDER_NOMINEES` WHERE `ORDER_ID` = @OrderId AND `SERVICE_ID` = @ServiceId;
                """, new { OrderId = orderId, ServiceId = item.ReferenceId }, transaction, cancellationToken: cancellationToken));
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
              AND `ENDS_AT` > @StartsAt AND `STARTS_AT` < @EndsAt FOR UPDATE;
            """, new { StaffId = staffId, StartsAt = startsAt, EndsAt = endsAt }, transaction,
            cancellationToken: cancellationToken)) > 0;

    private static async Task ReturnToRescheduleAsync(MySqlConnection connection, MySqlTransaction transaction,
        string orderId, string fromStatus, string reason, string actorId, DateTime now,
        CancellationToken cancellationToken)
    {
        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE `ORDERS` SET `ORDER_STATUS` = 'needs_reschedule', `UPDATED_AT` = @Now WHERE `ID` = @OrderId;
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
}
