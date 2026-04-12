using System.ComponentModel.DataAnnotations;

namespace FlatPlanet.Security.Application.DTOs.Auth;

public class ChangePasswordRequest
{
    [Required]
    [MaxLength(128)]
    public string CurrentPassword { get; set; } = string.Empty;

    [Required]
    [MaxLength(128)]
    public string NewPassword { get; set; } = string.Empty;

    [Required]
    [MaxLength(128)]
    public string ConfirmPassword { get; set; } = string.Empty;
}
