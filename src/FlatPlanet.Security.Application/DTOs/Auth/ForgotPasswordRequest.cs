using System.ComponentModel.DataAnnotations;

namespace FlatPlanet.Security.Application.DTOs.Auth;

public class ForgotPasswordRequest
{
    [Required]
    [EmailAddress]
    [MaxLength(256)]
    public string Email { get; set; } = string.Empty;

    [Required]
    [MaxLength(100)]
    public string AppSlug { get; set; } = string.Empty;
}
