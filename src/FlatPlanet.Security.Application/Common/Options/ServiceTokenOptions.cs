namespace FlatPlanet.Security.Application.Common.Options;

public class ServiceTokenOptions
{
    public const string Section = "ServiceToken";

    public string Token { get; set; } = string.Empty;
}
