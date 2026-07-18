namespace ToBeClarify.Api.Auth;

public static class AdminRole
{
    public const string Developer = "developer";
    public const string Manager = "manager";
    public const string Clerk = "clerk";

    public static readonly string[] All = [Developer, Manager, Clerk];

    public static string GetLabel(string role) => role switch
    {
        Developer => "開發者",
        Manager => "經理",
        Clerk => "店員",
        _ => role
    };

    public static bool IsValid(string role) => All.Contains(role, StringComparer.Ordinal);
}
