using System.ComponentModel.DataAnnotations;

namespace FlatPlanet.Security.Application.DTOs.Admin;

public class CreateCompanyRequest
{
    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [MaxLength(10)]
    public string CountryCode { get; set; } = string.Empty;

    [MaxLength(50)]
    public string? Code { get; set; }
}

public class UpdateCompanyRequest
{
    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [MaxLength(10)]
    public string CountryCode { get; set; } = string.Empty;

    [MaxLength(50)]
    public string? Code { get; set; }
}

public class UpdateCompanyStatusRequest
{
    [Required]
    [RegularExpression("^(active|suspended|inactive)$")]
    public string Status { get; set; } = string.Empty;
}

public class CompanyResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string CountryCode { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? Code { get; set; }
    public DateTime CreatedAt { get; set; }
}
