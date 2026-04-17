using System.ComponentModel.DataAnnotations;

namespace FlatPlanet.Security.Application.DTOs.Mfa;

public class ResendEmailOtpRequest
{
    [Required]
    public Guid UserId { get; set; }
}
