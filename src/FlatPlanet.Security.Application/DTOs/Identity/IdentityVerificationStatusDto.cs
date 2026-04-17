namespace FlatPlanet.Security.Application.DTOs.Identity;

public class IdentityVerificationStatusDto
{
    public bool MfaVerified { get; set; }
    public bool VideoVerified { get; set; }
    public bool FullyVerified { get; set; }
    public DateTime? VerifiedAt { get; set; }
}
