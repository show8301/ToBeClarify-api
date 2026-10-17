using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using ToBeClarify.Api.Auth;
using ToBeClarify.Api.Exceptions;
using ToBeClarify.Api.Infrastructure;
using ToBeClarify.Api.Models.Dtos;
using ToBeClarify.Api.Repositories.Ordering;
using ToBeClarify.Api.Services.Ordering;

// Pure in-memory contract checks: no host, database, receipts or real credentials.
var clock = new FixedClock();
var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();
var repository = new MemoryRepository();
var ordering = DispatchProxy.Create<IOrderingService, ContextProxy>();
var contextProxy = (ContextProxy)(object)ordering;
contextProxy.Context = new(new DateOnly(2026, 10, 17), new DateOnly(2026, 10, 17),
    clock.Now.AddHours(-1), clock.Now.AddHours(4), true, PeriodStatus: "open")
    { BusinessPeriodId = "period-a", FlowVersion = 2, Version = 3 };
var tokens = new OrderingTokenService(Options.Create(new OrderingTokenOptions
    { Secret = "isolated-test-only-secret-32-characters-long", PublicWebBaseUrl = "https://example.invalid/order" }), config);
var service = new AdmissionContractService(repository, ordering, tokens, clock, config);
ClaimsPrincipal Actor(string id, string role = AdminRole.Clerk) => new(new ClaimsIdentity([
    new(AdminAuthConstants.UserIdClaimType, id), new(AdminAuthConstants.RoleClaimType, role)
], "contract-test"));
var clerk = Actor("clerk-a");
var manager = Actor("manager-a", AdminRole.Manager);
var passed = 0;
void Check(bool condition, string label)
{
    if (!condition) throw new Exception(label);
    passed++; Console.WriteLine($"PASS {label}");
}
async Task Reject(Func<Task> action, string expectedCode)
{
    try { await action(); throw new Exception($"Expected {expectedCode}"); }
    catch (Exception error) when ((error as BusinessException)?.ErrorCode == expectedCode ||
                                  (error as ConflictException)?.ErrorCode == expectedCode ||
                                  (error as ForbiddenException)?.ErrorCode == expectedCode ||
                                  (error as NotFoundException)?.ErrorCode == expectedCode)
    { passed++; Console.WriteLine($"PASS {expectedCode}"); }
}
Task Validate(AdmissionConfirmation confirmation, AdmissionPolicySnapshot? policy = null, ClaimsPrincipal? actor = null)
{
    service.ValidateConfirmation(confirmation, policy ?? repository.Policy, contextProxy.Context, actor ?? clerk);
    return Task.CompletedTask;
}

var preview = await service.PreviewAsync(clerk, default);
Check(preview.AdmissionAmount == 100_000 && preview.MealCreditAmount == 60_000, "fee and credit are independent numeric sources");
Check(preview.ConfirmedCreationAvailable && preview.LegacyCreationAllowed, "confirmed writer is available while legacy remains compatible");
var confirmation = new AdmissionConfirmation { BusinessPeriodId = preview.BusinessPeriodId,
    PricingRevision = preview.PricingRevision, ExpiresAt = preview.ExpiresAt,
    PaymentConfirmed = true, ConfirmedReceivedAmount = 100_000 };
await Validate(confirmation);
Check(true, "normal confirmation matches actual received fee");
await Reject(() => Validate(confirmation with { ConfirmedReceivedAmount = 60_000 }), "ADMISSION_PAYMENT_CONFIRMATION_REQUIRED");
await Reject(() => Validate(confirmation with { PaymentConfirmed = false }), "ADMISSION_PAYMENT_CONFIRMATION_REQUIRED");
await Reject(() => Validate(confirmation with { ConfirmedReceivedAmount = null }), "ADMISSION_PAYMENT_CONFIRMATION_REQUIRED");
await Reject(() => Validate(confirmation, repository.Policy with { AdmissionAmount = 110_000 }), "ADMISSION_PREVIEW_CHANGED");
await Reject(() => Validate(confirmation, repository.Policy with { MealCreditAmount = 50_000 }), "ADMISSION_PREVIEW_CHANGED");
await Reject(() => Validate(confirmation, repository.Policy with { Version = 2 }), "ADMISSION_PREVIEW_CHANGED");
await Reject(() => Validate(confirmation, actor: Actor("clerk-b")), "ADMISSION_PREVIEW_CHANGED");
await Reject(() => Validate(confirmation with { ExpiresAt = confirmation.ExpiresAt.AddHours(1) }), "ADMISSION_PREVIEW_CHANGED");
contextProxy.Context = contextProxy.Context with { Version = 4 };
await Reject(() => Validate(confirmation), "ADMISSION_PREVIEW_CHANGED");
contextProxy.Context = contextProxy.Context with { Version = 3, PeriodStatus = "closed" };
await Reject(() => Validate(confirmation), "BUSINESS_PERIOD_NOT_OPEN");
contextProxy.Context = contextProxy.Context with { PeriodStatus = "open", FlowVersion = 1 };
await Reject(() => Validate(confirmation), "FINANCE_LEGACY_READ_ONLY");
contextProxy.Context = contextProxy.Context with { FlowVersion = 2 };
clock.Now = preview.ExpiresAt;
await Reject(() => Validate(confirmation), "ADMISSION_PREVIEW_EXPIRED");
clock.Now = preview.ExpiresAt.Subtract(AdmissionContractService.PreviewLifetime);
await Reject(() => Validate(confirmation, repository.Policy with { AdmissionAmount = null }), "ADMISSION_POLICY_NOT_CONFIGURED");
repository.Policy = repository.Policy with { AdmissionAmount = 0 };
var freePreview = await service.PreviewAsync(clerk, default);
await Validate(confirmation with { PricingRevision = freePreview.PricingRevision, ConfirmedReceivedAmount = 0 });
Check(freePreview.AdmissionAmount == 0 && freePreview.MealCreditAmount == 60_000, "explicit free admission does not erase meal credit");
repository.Policy = repository.Policy with { AdmissionAmount = 100_000 };
await Reject(() => Validate(confirmation with { Mode = "unpaid", PaymentConfirmed = false, ConfirmedReceivedAmount = 0, Reason = "exception" }), "ADMISSION_EXCEPTION_FORBIDDEN");
var managerPreview = await service.PreviewAsync(manager, default);
var managerConfirmation = confirmation with { PricingRevision = managerPreview.PricingRevision };
await Validate(managerConfirmation with { Mode = "unpaid", PaymentConfirmed = false, ConfirmedReceivedAmount = 0, Reason = "manager approved unpaid" }, actor: manager);
await Validate(managerConfirmation with { Mode = "waived", DiscountAmount = 100_000, PaymentConfirmed = false, ConfirmedReceivedAmount = 0, Reason = "manager approved waived" }, actor: manager);
Check(true, "unpaid and waived never manufacture a receipt");
await Reject(() => service.SavePolicyAsync(new() { AdmissionAmount = 200_000, ExpectedVersion = 1, Reason = "change" }, clerk, default), "ADMISSION_POLICY_FORBIDDEN");
await Reject(() => service.GetOperationAsync(Guid.NewGuid(), clerk, default), "ADMISSION_CREATION_OPERATION_NOT_FOUND");
repository.Operation = new() { OperationId = Guid.NewGuid().ToString("D"), SessionId = "session-a",
    AdmissionId = "admission-a", BusinessPeriodId = "period-a", BusinessDate = new(2026, 10, 17),
    AdmissionAmount = 100_000, MealCreditAmount = 60_000, ReceivedAmount = 100_000,
    AdmissionStatus = "received", ReceiptRecordId = "receipt-a", CashPeriodId = "period-a",
    RecordedBy = "clerk-a", RecordedAt = clock.LocalDateTime };
var operationId = Guid.Parse(repository.Operation.OperationId);
await Reject(() => service.GetOperationAsync(operationId, Actor("clerk-b"), default), "ADMISSION_CREATION_OPERATION_NOT_FOUND");
var result = await service.GetOperationAsync(operationId, clerk, default);
Check(result.ReceivedAmount == 100_000 && result.CredentialAction == "explicit_reissue_if_needed", "lookup returns only committed accounting result");
Check(typeof(AdmissionCreationResultDto).GetProperties().All(p => !new[] { "OrderToken", "OrderUrl", "RecoveryCode", "ShortCode" }.Contains(p.Name)), "durable lookup contract has no ordering credentials");
Check((await service.GetOperationAsync(operationId, manager, default)).SessionId == "session-a", "manager can inspect another operator result");
config["OrderingAdmission:RequireConfirmedCreation"] = "true";
Check(!(await service.PreviewAsync(clerk, default)).LegacyCreationAllowed, "explicit cutover is visible in preview");
var legacy = new OrderingService(null!, null!, null!, null!, null!, clock, null!, config, null!, null!, null!, null!);
await Reject(() => legacy.CreateSessionAsync(new() { GameId = "unused" }, clerk, default), "ADMISSION_CLIENT_UPGRADE_REQUIRED");
Check(repository.Saves == 0, "preview, confirmation and lookup perform no writes");
var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
foreach (var json in new[] { "{\"expectedVersion\":0,\"reason\":\"test\"}", "{\"admissionAmount\":0,\"reason\":\"test\"}" })
{
    try { JsonSerializer.Deserialize<SaveAdmissionPolicyRequest>(json, jsonOptions); throw new Exception("Required policy field accepted"); }
    catch (JsonException) { Check(true, "missing policy field cannot silently revoke or overwrite configuration"); }
}
Check(JsonSerializer.Deserialize<SaveAdmissionPolicyRequest>("{\"admissionAmount\":null,\"expectedVersion\":0,\"reason\":\"clear\"}", jsonOptions)?.AdmissionAmount is null,
    "explicit null policy is distinct from omitted amount");
var creation = new AdmissionCreationService(null!, service, null!, tokens, clock);
await Reject(() => creation.CreateAsync(new() { OperationId = Guid.Empty, Admission = confirmation }, clerk, default), "ADMISSION_CONFIRMATION_REQUIRED");
await Reject(() => creation.CreateAsync(new() { OperationId = Guid.NewGuid() }, clerk, default), "ADMISSION_CONFIRMATION_REQUIRED");
await Reject(() => creation.CreateAsync(new() { OperationId = Guid.NewGuid(), GameId = "測試", ServerName = "巴哈姆特", CustomerUid = "C-UNKNOWN", Admission = confirmation }, clerk, default), "CUSTOMER_LINK_FORBIDDEN");
await Reject(() => creation.CreateAsync(new() { OperationId = Guid.NewGuid(), GameId = "測試", ServerName = "巴哈姆特", MaxNominatedStaff = 101, Admission = confirmation }, clerk, default), "ADMISSION_CUSTOMER_INVALID");
Console.WriteLine($"{passed} checks passed; no database or HTTP mutations.");

public class ContextProxy : DispatchProxy
{
    public OrderingBusinessContextDto Context { get; set; } = null!;
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        => targetMethod?.Name == nameof(IOrderingService.GetBusinessContextAsync)
            ? Task.FromResult(Context) : throw new NotSupportedException(targetMethod?.Name);
}
public sealed class FixedClock : IAppClock
{
    public DateTimeOffset Now { get; set; } = new(2026, 10, 17, 12, 0, 0, TimeSpan.FromHours(8));
    public DateTime LocalDateTime => Now.ToOffset(TimeSpan.FromHours(8)).DateTime;
}
public sealed class MemoryRepository : IAdmissionContractRepository
{
    public AdmissionPolicySnapshot Policy { get; set; } = new(100_000, 1, 60_000);
    public AdmissionCreationOperationRow? Operation { get; set; }
    public int Saves { get; private set; }
    public Task<AdmissionPolicySnapshot> GetPolicyAsync(CancellationToken ct) => Task.FromResult(Policy);
    public Task<AdmissionPolicySnapshot> SavePolicyAsync(SaveAdmissionPolicyRequest request, string actorId, DateTime now, CancellationToken ct)
    { Saves++; return Task.FromResult(Policy); }
    public Task<AdmissionCreationOperationRow?> GetOperationAsync(Guid operationId, CancellationToken ct)
        => Task.FromResult(Operation?.OperationId == operationId.ToString("D") ? Operation : null);
}
