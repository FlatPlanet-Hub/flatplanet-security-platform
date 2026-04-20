namespace FlatPlanet.Security.Application.DTOs.Auth;

public class UpdateProfileResponse
{
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    /// <summary>
    /// True when the email was changed. The access token email claim is now stale —
    /// the caller must log in again to get a fresh token.
    /// </summary>
    public bool RequiresReLogin { get; set; }
}
