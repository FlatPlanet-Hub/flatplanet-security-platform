namespace FlatPlanet.Security.Application.Common.Options;

public class DatabaseOptions
{
    public const string Section = "Database";

    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 5432;
    public string Name { get; set; } = string.Empty;
    public string User { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;

    public string BuildConnectionString() =>
        $"Host={Host};Port={Port};Database={Name};Username={User};Password={Password};" +
        $"SslMode=Require;Trust Server Certificate=true;" +
        $"No Reset On Close=true;Max Auto Prepare=0;" +
        $"Command Timeout=30;Timeout=30;" +
        $"Keepalive=30;" +
        $"Minimum Pool Size=2;Maximum Pool Size=20;";
}
