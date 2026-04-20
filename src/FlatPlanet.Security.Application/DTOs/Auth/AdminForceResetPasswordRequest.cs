using System.ComponentModel.DataAnnotations;

namespace FlatPlanet.Security.Application.DTOs.Auth;

public class AdminForceResetPasswordRequest
{
    [Required]
    public string AppSlug { get; set; } = string.Empty;
}
