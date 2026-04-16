using System.ComponentModel.DataAnnotations;

namespace FlatPlanet.Security.Application.DTOs.Auth;

public class ResetPasswordRequest
{
    [Required]
    [MaxLength(256)]
    public string Token { get; set; } = string.Empty;

    [Required]
    [MaxLength(128)]
    public string NewPassword { get; set; } = string.Empty;

    [Required]
    [MaxLength(128)]
    public string ConfirmPassword { get; set; } = string.Empty;
}
