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
    public const string PasswordResetForcedByAdmin = "password_reset_forced_by_admin";
    public const string MfaOtpIssued = "mfa_otp_issued";
    public const string MfaTotpFallbackRequested = "mfa_totp_fallback_requested";
    public const string MfaVerified = "mfa_verified";
    public const string MfaFailed = "mfa_failed";
    public const string MfaLoginVerified = "mfa_login_verified";
    public const string MfaEnrolmentComplete = "mfa_enrolment_complete";
    public const string MfaDisabled = "mfa_disabled";
    public const string MfaReset = "mfa_reset";
    public const string MfaBackupCodesGenerated = "mfa_backup_codes_generated";
    public const string MfaMethodSet = "mfa_method_set";
    public const string IdentityVerificationCompleted = "identity_verification_completed";
    public const string ProfileNameUpdated = "profile_name_updated";
    public const string ProfileEmailUpdated = "profile_email_updated";

    // ── Federated login ────────────────────────────────────────────────────
    public const string FederatedLogin = "federated_login";

    // ── Per-app session policy ─────────────────────────────────────────────
    /// <summary>
    /// A login supplied an appSlug whose session timeout policy was refused — unknown
    /// app, inactive app, or no active grant. The login itself still succeeded with
    /// platform default timeouts.
    /// </summary>
    public const string SessionPolicyDenied = "session_policy_denied";

    // ── Service tokens (per-service auth) ──────────────────────────────────
    public const string ServiceTokenMinted        = "service_token_minted";
    public const string ServiceTokenRevoked       = "service_token_revoked";
    public const string ServiceTokenScopesChanged = "service_token_scopes_changed";
    // RESERVED — constant defined for forward-compat; not yet emitted.
    // To be wired by the first-use-per-day-per-token sampling work (separate PR).
    public const string ServiceTokenUsed          = "service_token_used";          // sampled: first use per token per UTC day
    public const string ServiceTokenScopeDenied   = "service_token_scope_denied";  // any 403 from scope mismatch
    public const string ServiceTokenCacheFlushed  = "service_token_cache_flushed";
}
