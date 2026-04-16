using System.ComponentModel.DataAnnotations;

namespace FlatPlanet.Security.Application.DTOs.Mfa;

public class VerifyOtpRequest
{
    [Required]
    [StringLength(8, MinimumLength = 4)]
    public string Code { get; set; } = string.Empty;
}
