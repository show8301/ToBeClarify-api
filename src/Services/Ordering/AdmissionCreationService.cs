using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;
using ToBeClarify.Api.Auth;
using ToBeClarify.Api.Exceptions;
using ToBeClarify.Api.Infrastructure;
using ToBeClarify.Api.Models.Dtos;
using ToBeClarify.Api.Models.Entities;
using ToBeClarify.Api.Repositories.Customers;
using ToBeClarify.Api.Repositories.Ordering;
using ToBeClarify.Api.Services.Customers;

namespace ToBeClarify.Api.Services.Ordering;

public sealed class AdmissionCreationService(AppDbContext db, AdmissionContractService contracts,
    OrderingFinanceRepository finance, IOrderingTokenService tokens, IAppClock clock)
{
    public async Task<AdmissionCreationIssuedDto> CreateAsync(CreateAdmissionSessionRequest request,
        ClaimsPrincipal actor, CancellationToken ct)
    {
        if (request.OperationId is null || request.OperationId == Guid.Empty || request.Admission is null)
            throw new BusinessException("請提供開碼操作識別碼及收款確認。", "ADMISSION_CONFIRMATION_REQUIRED");
        var actorId = CustomerIdentityService.ActorId(actor);
        var identity = CharacterIdentity.Parse(request.GameId, request.ServerName, true);
        var uid = string.IsNullOrWhiteSpace(request.CustomerUid) ? null : CustomerIdentityService.NormalizeUid(request.CustomerUid);
        if (uid is not null && !CustomerIdentityService.IsManager(actor))
            throw new ForbiddenException("連結既有 UID 需要管理員核對。", "CUSTOMER_LINK_FORBIDDEN");
        var customerName = string.IsNullOrWhiteSpace(request.CustomerName) ? identity.CharacterName : request.CustomerName.Trim();
        var max = request.MaxNominatedStaff ?? 1;
        if (max is < 0 or > 100 || customerName.Length > 100 || uid?.Length > 40)
            throw new BusinessException("顧客資料不正確。", "ADMISSION_CUSTOMER_INVALID");
        var operationId = request.OperationId.Value.ToString("D");
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new {
            ActorId = actorId, identity.GameId, CustomerName = customerName, CustomerUid = uid, Max = max,
            request.Admission
        })))).ToLowerInvariant();
        await using var c = await db.CreateOpenConnectionAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        // Same global lock as legacy creation and business-period transitions.
        var operatingLock = await c.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
            "SELECT ID FROM ORDERING_RUNTIME_LOCKS WHERE ID='operating_period' FOR UPDATE;", transaction: tx, cancellationToken: ct));
        if (operatingLock is null)
            throw new ConflictException("營業鎖定資料尚未準備完成，請聯絡管理員。", "ADMISSION_SCHEMA_NOT_READY");
        var prior = await c.QuerySingleOrDefaultAsync<CreationReplayRow>(new CommandDefinition("""
            SELECT PAYLOAD_HASH AS PayloadHash, CREATED_BY AS ActorId
            FROM ORDERING_SESSION_CREATE_OPERATIONS WHERE OPERATION_ID=@OperationId FOR UPDATE;
            """, new { OperationId = operationId }, tx, cancellationToken: ct));
        if (prior is not null)
        {
            if (prior.ActorId != actorId || prior.PayloadHash != hash)
                throw new ConflictException("相同操作識別碼已用於不同內容。", "ADMISSION_CREATION_OPERATION_CONFLICT");
            await tx.CommitAsync(ct);
            return new(null, await contracts.GetOperationAsync(request.OperationId.Value, actor, ct));
        }
        var period = await c.QuerySingleOrDefaultAsync<BusinessPeriodRow>(new CommandDefinition("""
            SELECT ID AS Id, BUSINESS_DATE AS BusinessDate, PERIOD_STATUS AS PeriodStatus,
                   FLOW_VERSION AS FlowVersion, VERSION AS Version, SETTLED_AT AS SettledAt
            FROM BUSINESS_PERIODS WHERE ID=@Id FOR UPDATE;
            """, new { Id = request.Admission.BusinessPeriodId }, tx, cancellationToken: ct))
            ?? throw new ConflictException("營業期已變更。", "BUSINESS_PERIOD_NOT_OPEN");
        var policy = await c.QuerySingleOrDefaultAsync<AdmissionPolicySnapshot>(new CommandDefinition("""
            SELECT P.ADMISSION_AMOUNT AS AdmissionAmount,P.VERSION AS Version,S.MINIMUM_MEAL_CREDIT AS MealCreditAmount
            FROM ORDERING_ADMISSION_POLICY P JOIN ORDERING_SETTINGS S ON S.ID='default'
            WHERE P.ID='default' FOR UPDATE;
            """, transaction: tx, cancellationToken: ct))
            ?? throw new BusinessException("請先設定入場收費。", "ADMISSION_POLICY_NOT_CONFIGURED");
        var day = DateOnly.FromDateTime(period.BusinessDate);
        var context = new OrderingBusinessContextDto(period.PeriodStatus == "open" ? day : null, day,
            clock.Now, clock.Now, period.PeriodStatus == "open", PeriodStatus: period.PeriodStatus,
            SettledAt: period.SettledAt is { } settled ? Offset(settled) : null)
            { BusinessPeriodId = period.Id, Version = period.Version, FlowVersion = period.FlowVersion };
        contracts.ValidateConfirmation(request.Admission, policy, context, actor);
        var now = clock.LocalDateTime;
        var id = Guid.NewGuid().ToString("D");
        var token = tokens.Create(id, identity.GameId, day);
        var shortCode = tokens.CreateShortCode();
        var recovery = tokens.CreateRecoveryCode();
        var session = new OrderSessionRow { Id = id, GameId = identity.GameId, CustomerName = customerName,
            BusinessDate = period.BusinessDate, BusinessPeriodId = period.Id, FlowVersion = period.FlowVersion,
            AccessTokenHash = tokens.Hash(token), ShortCodeHash = tokens.Hash(shortCode), RecoveryCodeHash = tokens.Hash(recovery),
            RecoveryCodeIssuedAt = now, RecoveryCodeVersion = 1, MaxNominatedStaff = max,
            PrepaidMealCredit = policy.MealCreditAmount, RemainingMealCredit = policy.MealCreditAmount,
            SessionStatus = "active", CreatedAt = now };
        await OrderingRepository.CreateSessionInTransactionAsync(c, tx, session, actorId, ct);
        var a = request.Admission;
        var admission = await finance.SaveAdmissionInTransactionAsync(c, tx, id, new() {
            OperationId = operationId, ExpectedVersion = 0, Amount = policy.AdmissionAmount!.Value,
            DiscountAmount = a.DiscountAmount, CreditAmount = policy.MealCreditAmount, Mode = a.Mode,
            CashPeriodId = period.Id, Reason = a.Reason?.Trim() is { Length: > 0 } reason ? reason : "開碼入場實收"
        }, hash, actorId, ct);
        if (uid is null)
        {
            var candidates = (await c.QueryAsync<string>(new CommandDefinition("""
                SELECT DISTINCT V.CUSTOMER_UID FROM CUSTOMER_VISITS V
                JOIN CUSTOMER_ORDER_SESSIONS S ON S.ID=V.SESSION_ID
                WHERE UPPER(TRIM(S.GAME_ID))=@GameId LIMIT 2;
                """, new { GameId = CustomerIdentityService.NormalizeGameId(identity.GameId) }, tx, cancellationToken: ct))).AsList();
            if (candidates.Count == 1) uid = candidates[0];
            if (candidates.Count < 2)
                await CustomerDeliveryRepository.LinkProfileInTransaction(c, tx, id, uid,
                    "C-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(8)), actorId, ct);
        }
        else await CustomerDeliveryRepository.LinkProfileInTransaction(c, tx, id, uid, uid, actorId, ct);
        await c.ExecuteAsync(new CommandDefinition("""
            INSERT INTO ORDERING_SESSION_CREATE_OPERATIONS
                (OPERATION_ID,PAYLOAD_HASH,SESSION_ID,ADMISSION_ID,BUSINESS_PERIOD_ID,BUSINESS_DATE,
                 POLICY_VERSION,PRICING_REVISION,ADMISSION_AMOUNT,DISCOUNT_AMOUNT,MEAL_CREDIT_AMOUNT,
                 ADMISSION_STATUS,RECEIVED_AMOUNT,CHARGE_RECORD_ID,RECEIPT_RECORD_ID,CASH_PERIOD_ID,
                 ACCOUNT_VERSION,CREATED_AT,CREATED_BY)
            VALUES (@OperationId,@Hash,@SessionId,@AdmissionId,@PeriodId,@Day,@PolicyVersion,@PricingRevision,
                    @Amount,@DiscountAmount,@Credit,@Mode,@Received,@ChargeRecordId,@ReceiptRecordId,@PeriodId,
                    @AccountVersion,@Now,@ActorId);
            """, new { OperationId = operationId, Hash = hash, SessionId = id, AdmissionId = admission.AdmissionId,
                PeriodId = period.Id, Day = period.BusinessDate, PolicyVersion = policy.Version, a.PricingRevision,
                Amount = policy.AdmissionAmount.Value, a.DiscountAmount, Credit = policy.MealCreditAmount, a.Mode,
                Received = a.ConfirmedReceivedAmount!.Value, admission.ChargeRecordId, admission.ReceiptRecordId,
                admission.AccountVersion, Now = now, ActorId = actorId }, tx, cancellationToken: ct));
        await tx.CommitAsync(ct);
        var result = new AdmissionCreationResultDto(request.OperationId.Value, id, admission.AdmissionId, period.Id,
            day, policy.AdmissionAmount.Value, a.DiscountAmount, policy.MealCreditAmount, a.Mode,
            a.ConfirmedReceivedAmount.Value, admission.ChargeRecordId, admission.ReceiptRecordId, period.Id,
            admission.AccountVersion, Offset(now), actorId, "explicit_reissue_if_needed");
        return new(new(new(id, identity.GameId, customerName, day, max, policy.MealCreditAmount, policy.MealCreditAmount, "active")
            { BusinessPeriodId = period.Id, FlowVersion = period.FlowVersion }, token, tokens.BuildOrderUrl(shortCode), recovery), result);
    }

    private static DateTimeOffset Offset(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Unspecified), TimeSpan.FromHours(8));
    private sealed class CreationReplayRow
    {
        public string PayloadHash { get; set; } = "";
        public string ActorId { get; set; } = "";
    }
}
