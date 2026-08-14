using System.ComponentModel.DataAnnotations;

namespace FlatPlanet.Security.Application.DTOs.Mfa;

public class VerifyTotpEnrolmentRequest
{
    [Required]
    [StringLength(8, MinimumLength = 6)]
    public string TotpCode { get; set; } = string.Empty;

    /// <summary>Optional SP app slug this session belongs to. Selects per-app session timeouts.</summary>
    [MaxLength(100)]
    public string? AppSlug { get; set; }
}
