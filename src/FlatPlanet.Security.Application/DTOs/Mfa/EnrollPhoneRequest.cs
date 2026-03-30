using System.ComponentModel.DataAnnotations;

namespace FlatPlanet.Security.Application.DTOs.Mfa;

public class EnrollPhoneRequest
{
    [Required]
    [Phone]
    public string PhoneNumber { get; set; } = string.Empty;
}
