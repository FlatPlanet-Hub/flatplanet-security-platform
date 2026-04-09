namespace FlatPlanet.Security.Domain.Entities;

public class UserBusinessMembership
{
    public Guid Id { get; init; }
    public Guid UserId { get; init; }
    public Guid CompanyId { get; init; }
    public string Role { get; init; } = "member";
    public string Status { get; init; } = "active";
    public Guid? InvitedBy { get; init; }
    public DateTime JoinedAt { get; init; }
    public DateTime? ExpiresAt { get; init; }

    // Joined from companies table
    public string? BusinessCode { get; init; }
    public string? BusinessName { get; init; }
}
