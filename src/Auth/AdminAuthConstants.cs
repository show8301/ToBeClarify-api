namespace ToBeClarify.Api.Auth;

public static class AdminAuthConstants
{
    public const string CookieName = "tbc_admin_access_token";
    public const string RoleClaimType = "role";
    public const string UserIdClaimType = "user_id";
    public const string LoginNameClaimType = "login_name";
    public const string DisplayNameClaimType = "display_name";
    public const string RoleLevelClaimType = "role_level";
    public const string RoleLabelClaimType = "role_label";
    public const string StaffMemberIdClaimType = "staff_member_id";
    public const string TokenVersionClaimType = "token_version";
    public const string AdminPolicy = "AdminOnly";
    public const string DeveloperPolicy = "AdminDeveloper";
}
