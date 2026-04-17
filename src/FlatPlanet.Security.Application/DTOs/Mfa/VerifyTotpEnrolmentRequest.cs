using System.ComponentModel.DataAnnotations;

namespace FlatPlanet.Security.Application.DTOs.Mfa;

public class VerifyTotpEnrolmentRequest
{
    [Required]
    [StringLength(8, MinimumLength = 6)]
    public string TotpCode { get; set; } = string.Empty;
}
