namespace FlatPlanet.Security.Application.DTOs.Mfa;

public class UserMfaStatusResponse
{
    public bool MfaEnabled { get; set; }
    public string? MfaMethod { get; set; }
    public bool MfaTotpEnrolled { get; set; }
    public int BackupCodesRemaining { get; set; }
}
