namespace ToBeClarify.Api.Services.Admin.Auth;

public interface IOneTimeTokenService
{
    Task<OneTimeTokenIssueResult> IssueAsync(
        string purpose,
        string? targetUserId,
        string issuedBy,
        CancellationToken cancellationToken);

    Task<T> ConsumeAsync<T>(
        string submittedKey,
        string expectedPurpose,
        string? expectedTargetUserId,
        Func<OneTimeTokenContext, Task<T>> action,
        CancellationToken cancellationToken);
}

public sealed record OneTimeTokenIssueResult(string Key, DateTimeOffset ExpiresAt);

public sealed record OneTimeTokenContext(string Purpose, string? TargetUserId, string IssuedBy);

public static class OneTimeTokenPurpose
{
    public const string StaffRegister = "staff-register";
    public const string PasswordReset = "password-reset";
}
