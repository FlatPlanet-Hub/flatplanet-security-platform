using System.ComponentModel.DataAnnotations;

namespace FlatPlanet.Security.Application.Common.Options;

public class AzureAdOptions
{
    public const string Section = "AzureAd";

    [Required(AllowEmptyStrings = false)]
    public string TenantId { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string ClientId { get; set; } = string.Empty;
}
