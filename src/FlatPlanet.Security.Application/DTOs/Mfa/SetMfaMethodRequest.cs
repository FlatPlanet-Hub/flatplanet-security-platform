using System.ComponentModel.DataAnnotations;

namespace FlatPlanet.Security.Application.DTOs.Mfa;

public class SetMfaMethodRequest
{
    [Required]
    [RegularExpression("^(email_otp|totp)$", ErrorMessage = "Method must be 'email_otp' or 'totp'.")]
    public string Method { get; set; } = string.Empty;
}
