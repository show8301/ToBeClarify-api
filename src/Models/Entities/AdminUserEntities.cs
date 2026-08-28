namespace ToBeClarify.Api.Models.Entities;

public sealed class AdminUserRow
{
    public string Id { get; set; } = string.Empty;
    public string LoginName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string RoleLevel { get; set; } = string.Empty;
    public string? StaffMemberId { get; set; }
    public bool IsActive { get; set; }
    public int TokenVersion { get; set; }
}

public sealed class AdminUserListRow
{
    public string Id { get; set; } = string.Empty;
    public string LoginName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
}

public sealed class AdminTokenStateRow
{
    public string Id { get; set; } = string.Empty;
    public string RoleLevel { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public int TokenVersion { get; set; }
}
