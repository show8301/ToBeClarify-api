using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ToBeClarify.Api.Auth;
using ToBeClarify.Api.Exceptions;
using ToBeClarify.Api.Infrastructure;
using ToBeClarify.Api.Models.Dtos;
using ToBeClarify.Api.Repositories.Ordering;

namespace ToBeClarify.Api.Services.Ordering;

public sealed class AdmissionContractService(IAdmissionContractRepository repository,
    IOrderingService ordering, IOrderingTokenService tokens, IAppClock clock, IConfiguration configuration)
{
    public static readonly TimeSpan PreviewLifetime = TimeSpan.FromMinutes(5);
    public const long MaximumAmount = 1_000_000_000_000L;

    public async Task<AdmissionPolicyDto> GetPolicyAsync(CancellationToken ct)
        => Map(await repository.GetPolicyAsync(ct));

    public async Task<AdmissionPolicyDto> SavePolicyAsync(SaveAdmissionPolicyRequest request, ClaimsPrincipal actor, CancellationToken ct)
    {
        if (!IsManager(actor)) throw new ForbiddenException("入場收費設定需由經理確認。", "ADMISSION_POLICY_FORBIDDEN");
        if (request.AdmissionAmount is < 0 or > MaximumAmount || request.ExpectedVersion < 0 ||
            string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > 500)
            throw new BusinessException("入場費、版本或變更原因無效。", "ADMISSION_POLICY_INVALID");
        return Map(await repository.SavePolicyAsync(request, ActorId(actor), clock.LocalDateTime, ct));
    }

    public async Task<AdmissionPreviewDto> PreviewAsync(ClaimsPrincipal actor, CancellationToken ct)
    {
        var context = await ordering.GetBusinessContextAsync(ct);
        var policy = await repository.GetPolicyAsync(ct);
        return BuildPreview(policy, context, actor, clock.Now.Add(PreviewLifetime));
    }

    // The creation writer calls this inside its transaction using locked, current
    // policy/settings/period rows, after checking for an already committed operation.
    public void ValidateConfirmation(AdmissionConfirmation confirmation, AdmissionPolicySnapshot policy,
        OrderingBusinessContextDto context, ClaimsPrincipal actor)
    {
        if (confirmation.ExpiresAt <= clock.Now)
            throw new ConflictException("入場收費預覽已到期，請重新確認。", "ADMISSION_PREVIEW_EXPIRED");
        var expected = BuildPreview(policy, context, actor, confirmation.ExpiresAt);
        if (confirmation.BusinessPeriodId != expected.BusinessPeriodId ||
            !SameRevision(confirmation.PricingRevision, expected.PricingRevision))
            throw new ConflictException("收費、折抵或營業期已變更，請重新確認。", "ADMISSION_PREVIEW_CHANGED");
        if (confirmation.Mode is not ("received" or "unpaid" or "waived") ||
            confirmation.DiscountAmount < 0 || confirmation.DiscountAmount > expected.AdmissionAmount ||
            confirmation.Mode == "waived" && confirmation.DiscountAmount != expected.AdmissionAmount ||
            confirmation.Reason?.Length > 500)
            throw new BusinessException("入場收款內容無效。", "ADMISSION_CONFIRMATION_INVALID");
        if ((confirmation.Mode != "received" || confirmation.DiscountAmount > 0) &&
            (!IsManager(actor) || string.IsNullOrWhiteSpace(confirmation.Reason)))
            throw new ForbiddenException("入場優惠、免收或未收款需由經理填寫原因確認。", "ADMISSION_EXCEPTION_FORBIDDEN");
        var received = confirmation.Mode == "received" ? expected.AdmissionAmount - confirmation.DiscountAmount : 0;
        if (confirmation.ConfirmedReceivedAmount != received ||
            confirmation.PaymentConfirmed != (confirmation.Mode == "received"))
            throw new BusinessException("請確認本次實際收到的入場費。", "ADMISSION_PAYMENT_CONFIRMATION_REQUIRED");
    }

    public async Task<AdmissionCreationResultDto> GetOperationAsync(Guid operationId, ClaimsPrincipal actor, CancellationToken ct)
    {
        var actorId = ActorId(actor);
        var row = operationId == Guid.Empty ? null : await repository.GetOperationAsync(operationId, ct);
        if (row is null || row.RecordedBy != actorId && !IsManager(actor))
            throw new NotFoundException("尚未查到已提交的開碼結果，請沿用原操作識別碼確認。", "ADMISSION_CREATION_OPERATION_NOT_FOUND");
        return new(operationId, row.SessionId, row.AdmissionId, row.BusinessPeriodId,
            DateOnly.FromDateTime(row.BusinessDate), row.AdmissionAmount, row.DiscountAmount,
            row.MealCreditAmount, row.AdmissionStatus, row.ReceivedAmount, row.ChargeRecordId,
            row.ReceiptRecordId, row.CashPeriodId, row.AccountVersion,
            new DateTimeOffset(row.RecordedAt, TimeSpan.FromHours(8)), row.RecordedBy, "explicit_reissue_if_needed");
    }

    private AdmissionPreviewDto BuildPreview(AdmissionPolicySnapshot policy, OrderingBusinessContextDto context,
        ClaimsPrincipal actor, DateTimeOffset expiresAt)
    {
        var actorId = ActorId(actor);
        if (context.PeriodStatus != "open" || context.CurrentBusinessDate is null || context.BusinessPeriodId is null || context.SettledAt is not null)
            throw new ConflictException("目前沒有可開立入場碼的營業期。", "BUSINESS_PERIOD_NOT_OPEN");
        if (context.FlowVersion < 2)
            throw new ConflictException("此營業期尚未啟用新版入場帳。", "FINANCE_LEGACY_READ_ONLY");
        if (policy.AdmissionAmount is null or < 0 or > MaximumAmount || policy.MealCreditAmount < 0)
            throw new BusinessException("請先由經理設定數值入場費及有效折抵額度。", "ADMISSION_POLICY_NOT_CONFIGURED");
        var revision = tokens.Hash(JsonSerializer.Serialize(new { Contract = "admission-creation-v1", ActorId = actorId,
            context.BusinessPeriodId, BusinessDate = context.CurrentBusinessDate.Value.ToString("yyyy-MM-dd"),
            PeriodVersion = context.Version, context.FlowVersion, PolicyVersion = policy.Version, policy.AdmissionAmount, policy.MealCreditAmount,
            ExpiresAt = expiresAt.ToUniversalTime().ToString("O") }));
        return new(1, context.BusinessPeriodId, context.CurrentBusinessDate.Value, context.Version, policy.Version,
            policy.AdmissionAmount.Value, policy.MealCreditAmount, expiresAt, revision,
            ConfirmedCreationAvailable: true,
            LegacyCreationAllowed: !configuration.GetValue<bool>("OrderingAdmission:RequireConfirmedCreation"));
    }

    private static AdmissionPolicyDto Map(AdmissionPolicySnapshot policy)
        => new(policy.AdmissionAmount, policy.Version, policy.MealCreditAmount,
            "ordering_admission_policy", "ordering_settings.minimum_meal_credit");

    private static bool IsManager(ClaimsPrincipal actor)
        => actor.FindFirstValue(AdminAuthConstants.RoleClaimType) is AdminRole.Manager or AdminRole.Developer;

    private static string ActorId(ClaimsPrincipal actor)
        => actor.FindFirstValue(AdminAuthConstants.UserIdClaimType) ?? actor.FindFirstValue(ClaimTypes.NameIdentifier) ?? actor.FindFirstValue("sub")
            ?? throw new UnauthorizedException();

    private static bool SameRevision(string? actual, string expected)
        => actual is { Length: 64 } && CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(actual), Encoding.UTF8.GetBytes(expected));
}
