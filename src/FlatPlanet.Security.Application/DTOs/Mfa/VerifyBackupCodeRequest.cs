using System.ComponentModel.DataAnnotations;

namespace FlatPlanet.Security.Application.DTOs.Mfa;

public class VerifyBackupCodeRequest
{
    [Required]
    public Guid UserId { get; set; }

    [Required]
    [StringLength(10, MinimumLength = 10)]
    public string BackupCode { get; set; } = string.Empty;
}
