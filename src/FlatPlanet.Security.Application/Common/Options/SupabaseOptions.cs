namespace FlatPlanet.Security.Application.Common.Options;

public class SupabaseOptions
{
    public const string Section = "Supabase";

    public string Url { get; set; } = string.Empty;
    public string ServiceRoleKey { get; set; } = string.Empty;
    public string JwtSecret { get; set; } = string.Empty;
    public string DbHost { get; set; } = string.Empty;
    public int DbPort { get; set; } = 6543;
    public string DbName { get; set; } = string.Empty;
    public string DbUser { get; set; } = string.Empty;
    public string DbPassword { get; set; } = string.Empty;

    public string BuildConnectionString() =>
        $"Host={DbHost};Port={DbPort};Database={DbName};Username={DbUser};Password={DbPassword}";
}
