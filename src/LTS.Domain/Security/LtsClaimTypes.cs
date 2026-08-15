namespace LTS.Domain.Security;

/// <summary>
/// Claims added to the sign-in cookie. They carry the facts that are needed on every request —
/// who the user is and which company they belong to — so the common path does not hit the
/// database, while the changeable part (their grants) is looked up and cached separately.
/// </summary>
public static class LtsClaimTypes
{
    public const string UserType = "lts:user-type";
    public const string PartnerId = "lts:partner-id";
    public const string FullName = "lts:full-name";
    public const string MustChangePassword = "lts:must-change-password";
}
