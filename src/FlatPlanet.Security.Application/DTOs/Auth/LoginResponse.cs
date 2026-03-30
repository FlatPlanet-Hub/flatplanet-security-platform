namespace FlatPlanet.Security.Application.DTOs.Auth;

public class LoginResponse
{
    public bool RequiresMfa { get; set; }
    public string? ChallengeId { get; set; }
    public string? AccessToken { get; set; }
    public string? RefreshToken { get; set; }
    public int? ExpiresIn { get; set; }
    public UserProfileDto? User { get; set; }
}

public class UserProfileDto
{
    public Guid UserId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string CompanyId { get; set; } = string.Empty;
}
