namespace FlatPlanet.Security.Domain.Enums;

public static class AuditEventType
{
    public const string LoginSuccess = "login_success";
    public const string LoginFailure = "login_failure";
    public const string Logout = "logout";
    public const string TokenRefresh = "token_refresh";
    public const string TokenRevoke = "token_revoke";
    public const string SessionStart = "session_start";
    public const string SessionEnd = "session_end";
    public const string SessionIdleTimeout = "session_idle_timeout";
    public const string SessionAbsoluteTimeout = "session_absolute_timeout";
    public const string RoleGranted = "role_granted";
    public const string RoleRevoked = "role_revoked";
    public const string UserCreated = "user_created";
    public const string UserDeactivated = "user_deactivated";
    public const string UserOffboarded = "user_offboarded";
    public const string AccountLocked = "account_locked";
    public const string AccountUnlocked = "account_unlocked";
}
