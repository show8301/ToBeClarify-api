using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using ToBeClarify.Api.Auth;
using ToBeClarify.Api.Exceptions;
using ToBeClarify.Api.Models.Dtos;
using ToBeClarify.Api.Repositories.Customers;

namespace ToBeClarify.Api.Services.Customers;

public sealed class CustomerIdentityService(CustomerDeliveryRepository repository)
{
    public static string Hash(string value, string purpose)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{purpose}:{value}")));

    public static string NormalizeGameId(string value)
        => string.Join(' ', (value ?? "").Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .ToUpperInvariant();

    public static string NormalizeUid(string value)
        => (value ?? "").Trim().ToUpperInvariant();

    public static string ActorId(ClaimsPrincipal actor) => actor.FindFirstValue(AdminAuthConstants.UserIdClaimType)
        ?? actor.FindFirstValue("sub") ?? throw new UnauthorizedException();

    public static bool IsManager(ClaimsPrincipal actor)
        => actor.IsInRole(AdminRole.Manager) || actor.IsInRole(AdminRole.Developer);

    public async Task<CustomerIdentityCandidatesDto> Candidates(string gameId, CancellationToken ct)
    {
        var normalized = NormalizeGameId(gameId);
        if (normalized.Length is 0 or > 100)
            throw new BusinessException("遊戲 ID 不正確。", "GAME_ID_REQUIRED");
        return new(gameId.Trim(), await repository.IdentityCandidates(normalized, ct));
    }

    public async Task<CustomerProfileDto> ResolveUid(string? uid, CancellationToken ct)
    {
        var normalized = NormalizeUid(uid ?? "");
        if (normalized.Length is 0 or > 40)
            throw new UnauthorizedException("顧客 UID 無效。", "CUSTOMER_UID_INVALID");
        return await repository.GetProfile(normalized, ct);
    }

    public async Task<CustomerProfileDto> LinkFromAdmin(
        string sessionId, LinkCustomerProfileRequest request, ClaimsPrincipal actor, CancellationToken ct)
    {
        var uid = string.IsNullOrWhiteSpace(request.CustomerUid) ? null : NormalizeUid(request.CustomerUid);
        if (uid is not null && !IsManager(actor))
            throw new ForbiddenException("連結既有 UID 需要管理員核對顧客。", "CUSTOMER_LINK_FORBIDDEN");
        return await Link(sessionId, uid, ActorId(actor), ct);
    }

    // Called by the staff-only ordering flow after a new Session is created.
    // A unique historical UID is reused; ambiguous matches remain unlinked so
    // staff can choose explicitly in the customer-history UI.
    public async Task<CustomerProfileDto?> LinkFromOrdering(
        string sessionId, string gameId, string actorId, CancellationToken ct)
    {
        var normalized = NormalizeGameId(gameId);
        if (normalized.Length is 0 or > 100) return null;
        var candidates = await repository.IdentityCandidates(normalized, ct);
        if (candidates.Count > 1) return null;
        return await Link(sessionId, candidates.Count == 1 ? candidates[0].Uid : null, actorId, ct);
    }

    private async Task<CustomerProfileDto> Link(string sessionId, string? existingUid, string actorId, CancellationToken ct)
    {
        var uid = existingUid is null ? NewUid() : NormalizeUid(existingUid);
        if (uid.Length is 0 or > 40) throw new BusinessException("顧客 UID 不正確。", "CUSTOMER_UID_INVALID");
        return await repository.LinkProfile(sessionId, existingUid, uid, actorId, ct);
    }

    private static string NewUid()
        => "C-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(8));

    public Task<CustomerHistoryPage> History(DateOnly? date, string? search, int page, int size, CancellationToken ct)
    {
        if (page is < 1 or > 100000 || size is < 1 or > 100 || search?.Length > 100)
            throw new BusinessException("分頁或搜尋參數不正確。", "INVALID_PAGE");
        return repository.History(date, string.IsNullOrWhiteSpace(search) ? null : search.Trim(), page, size, null, ct);
    }

    public async Task<CustomerDetailDto> Detail(string uid, CancellationToken ct)
    {
        var normalized = NormalizeUid(uid);
        if (normalized.Length is 0 or > 40)
            throw new BusinessException("顧客 UID 不正確。", "CUSTOMER_UID_INVALID");
        return new(await repository.GetProfile(normalized, ct), await repository.Summary(normalized, ct),
            (await repository.History(null, null, 1, 200, normalized, ct)).Items);
    }
}
