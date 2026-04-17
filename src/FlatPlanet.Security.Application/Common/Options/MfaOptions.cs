namespace FlatPlanet.Security.Application.Common.Options;

public class MfaOptions
{
    public const string Section = "Mfa";

    public string TotpEncryptionKey { get; set; } = string.Empty;
}
