namespace FlatPlanet.Security.Domain.Entities;

public class MfaChallenge
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;
    public string OtpHash { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public DateTime? VerifiedAt { get; set; }
    public int Attempts { get; set; }
    public DateTime CreatedAt { get; set; }
}
