namespace FlatPlanet.Security.Domain.Entities;

public class RefreshToken
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid? SessionId { get; set; }
    public string TokenHash { get; set; } = string.Empty;  // SHA256 hash, never plaintext
    public DateTime ExpiresAt { get; set; }
    public bool Revoked { get; set; }
    public DateTime? RevokedAt { get; set; }
    public string? RevokedReason { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? ReplacedByTokenHash { get; set; }
    public DateTime? RotatedAt { get; set; }
}
