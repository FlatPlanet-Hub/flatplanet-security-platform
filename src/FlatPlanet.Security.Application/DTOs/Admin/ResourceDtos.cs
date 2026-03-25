using System.ComponentModel.DataAnnotations;

namespace FlatPlanet.Security.Application.DTOs.Admin;

public class CreateResourceRequest
{
    [Required]
    public Guid ResourceTypeId { get; set; }

    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [MaxLength(200)]
    public string Identifier { get; set; } = string.Empty;
}

public class UpdateResourceRequest
{
    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [MaxLength(200)]
    public string Identifier { get; set; } = string.Empty;

    [Required]
    [RegularExpression("^(active|inactive)$")]
    public string Status { get; set; } = string.Empty;
}

public class ResourceResponse
{
    public Guid Id { get; set; }
    public Guid AppId { get; set; }
    public Guid ResourceTypeId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Identifier { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
