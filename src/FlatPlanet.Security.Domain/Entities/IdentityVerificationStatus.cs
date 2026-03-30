namespace FlatPlanet.Security.Domain.Entities;

public class IdentityVerificationStatus
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public bool OtpVerified { get; set; }
    public bool VideoVerified { get; set; }
    public bool FullyVerified { get; set; }
    public DateTime? VerifiedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
