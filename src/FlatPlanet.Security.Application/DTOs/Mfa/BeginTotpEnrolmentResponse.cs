namespace FlatPlanet.Security.Application.DTOs.Mfa;

public class BeginTotpEnrolmentResponse
{
    public string QrCodeUri { get; set; } = string.Empty;
}
