using System.ComponentModel.DataAnnotations;

namespace FlatPlanet.Security.Application.DTOs.Mfa;

public class VerifyLoginTotpRequest
{
    [Required]
    public Guid UserId { get; set; }

    [Required]
    [StringLength(8, MinimumLength = 6)]
    public string TotpCode { get; set; } = string.Empty;
}
