using System.ComponentModel.DataAnnotations;

namespace FlatPlanet.Security.Application.DTOs.Auth;

public class LoginRequest
{
    [Required]
    [EmailAddress]
    [MaxLength(256)]
    public string Email { get; set; } = string.Empty;

    [Required]
    [MaxLength(128)]
    public string Password { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? AppSlug { get; set; }
}
