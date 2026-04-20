using System.ComponentModel.DataAnnotations;

namespace FlatPlanet.Security.Application.DTOs.Mfa;

public class RequestTotpFallbackRequest
{
    [Required]
    public Guid UserId { get; set; }
}
