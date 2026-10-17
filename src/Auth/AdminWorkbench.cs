namespace ToBeClarify.Api.Auth;

public static class AdminWorkbench
{
    public static IReadOnlyList<string> AvailableFor(string accountRole) => accountRole switch
    {
        AdminRole.Developer or AdminRole.Manager => ["service", "designated", "manager"],
        AdminRole.Clerk => ["service", "designated"],
        _ => []
    };

    public static string? ValidPreference(string accountRole, string? preference)
        => preference is not null && AvailableFor(accountRole).Contains(preference, StringComparer.Ordinal)
            ? preference : null;
}
