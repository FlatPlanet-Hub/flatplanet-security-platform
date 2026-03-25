namespace FlatPlanet.Security.Application.DTOs.Access;

public class AccessReviewItemDto
{
    public Guid GrantId { get; set; }
    public Guid UserId { get; set; }
    public string UserEmail { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;
    public Guid AppId { get; set; }
    public string AppName { get; set; } = string.Empty;
    public string RoleName { get; set; } = string.Empty;
    public DateTime GrantedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public int DaysSinceGranted { get; set; }
}
