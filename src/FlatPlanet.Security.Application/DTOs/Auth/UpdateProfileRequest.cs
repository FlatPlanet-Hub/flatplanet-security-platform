using System.ComponentModel.DataAnnotations;

namespace FlatPlanet.Security.Application.DTOs.Auth;

public class UpdateProfileRequest
{
    [StringLength(150, MinimumLength = 1)]
    public string? FullName { get; set; }

    [EmailAddress]
    [StringLength(254)]
    public string? Email { get; set; }
}
