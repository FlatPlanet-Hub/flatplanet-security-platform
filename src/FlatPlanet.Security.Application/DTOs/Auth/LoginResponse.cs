namespace FlatPlanet.Security.Application.DTOs.Auth;

public class LoginResponse
{
    public bool RequiresMfa { get; set; }
    public string? MfaMethod { get; set; }
    public bool MfaEnrolmentPending { get; set; }
    public Guid? ChallengeId { get; set; }
    /// <summary>
    /// True only when this response is the result of a successful TOTP enrolment completion.
    /// Always false on standard login and MFA-verify responses — does not indicate whether
    /// the user has MFA configured; use GET /mfa/status for that.
    /// </summary>
    public bool MfaEnrolled { get; set; }
    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public int ExpiresIn { get; set; }
    public int IdleTimeoutMinutes { get; set; }
    public UserProfileDto User { get; set; } = new();
}

public class UserProfileDto
{
    public Guid UserId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string CompanyId { get; set; } = string.Empty;
}
