using System.ComponentModel.DataAnnotations;

namespace FlatPlanet.Security.Application.DTOs.Admin;

public class CreateAppRequest
{
    [Required]
    public Guid CompanyId { get; set; }

    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [MaxLength(100)]
    [RegularExpression("^[a-z0-9-]+$", ErrorMessage = "Slug must contain only lowercase letters, digits, and hyphens.")]
    public string Slug { get; set; } = string.Empty;

    [MaxLength(500)]
    public string BaseUrl { get; set; } = string.Empty;
}

public class UpdateAppRequest
{
    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Optional — if null or omitted, the existing base URL is preserved.
    /// </summary>
    [MaxLength(500)]
    public string? BaseUrl { get; set; }

    [Required]
    [RegularExpression("^(active|suspended|inactive)$")]
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// Optional — only provide when renaming the slug (e.g. on deactivation).
    /// Must be lowercase letters, digits, and hyphens only.
    /// Max 300 chars — generous headroom for long project names plus the
    /// -deleted-{yyyyMMddHHmmssfff} suffix appended on deactivation.
    /// </summary>
    [MaxLength(300)]
    [RegularExpression("^[a-z0-9-]+$", ErrorMessage = "Slug must contain only lowercase letters, digits, and hyphens.")]
    public string? Slug { get; set; }
}

public class AppResponse
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime RegisteredAt { get; set; }
}
