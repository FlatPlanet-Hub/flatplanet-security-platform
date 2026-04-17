using System.ComponentModel.DataAnnotations;

namespace FlatPlanet.Security.Application.DTOs.Mfa;

public class MfaLoginVerifyRequest
{
    [Required]
    public Guid ChallengeId { get; set; }

    [Required]
    [StringLength(8, MinimumLength = 4)]
    public string OtpCode { get; set; } = string.Empty;
}
