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
    public const string AuthorizeAllowed = "authorize_allowed";
    public const string AuthorizeDenied = "authorize_denied";
    public const string UserAnonymized = "user_anonymized";
    public const string CompanySuspended = "company_suspended";
    public const string CompanyDeactivated = "company_deactivated";
    public const string PasswordChanged = "password_changed";
    public const string PasswordResetRequested = "password_reset_requested";
    public const string PasswordResetCompleted = "password_reset_completed";
}
