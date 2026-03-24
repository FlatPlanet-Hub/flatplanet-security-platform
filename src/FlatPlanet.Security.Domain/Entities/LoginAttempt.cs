namespace FlatPlanet.Security.Domain.Entities;

public class LoginAttempt
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string? IpAddress { get; set; }
    public bool Success { get; set; }
    public DateTime AttemptedAt { get; set; }
}
