namespace FlatPlanet.Security.Application.Common.Options;

public class SmsOptions
{
    public const string Section = "Sms";
    public string AccountSid { get; set; } = string.Empty;
    public string AuthToken { get; set; } = string.Empty;
    public string FromNumber { get; set; } = string.Empty;
}
