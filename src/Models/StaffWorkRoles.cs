using System.Text.Json;

namespace ToBeClarify.Api.Models;

public static class StaffWorkRoles
{
    public static IReadOnlyList<string> AllowedSupportRoles(bool isNominatable, bool canUseBackstage = false)
        => new[] { "service", isNominatable ? "designated" : null, canUseBackstage ? "backstage" : null }
            .OfType<string>().ToArray();

    public static string[] ReadSupportRoles(string? json)
    {
        try
        {
            return (JsonSerializer.Deserialize<string[]>(json ?? "[]") ?? [])
                .Where(role => role is "service" or "designated" or "backstage")
                .Distinct(StringComparer.Ordinal).ToArray();
        }
        catch (JsonException) { return []; }
    }

    public static string? UnambiguousLegacyPayrollRole(IEnumerable<string> roles)
    {
        var values = roles.Distinct(StringComparer.Ordinal).ToArray();
        var front = values.Where(role => role is "service" or "designated" or "manager").ToArray();
        return front.Length == 1 ? front[0]
            : front.Length == 0 && values.SequenceEqual(["backstage"]) ? "backstage" : null;
    }
}
