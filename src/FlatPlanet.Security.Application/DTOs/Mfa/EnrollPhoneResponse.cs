namespace FlatPlanet.Security.Application.DTOs.Mfa;

public class EnrollPhoneResponse
{
    public string MaskedPhone { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
}
