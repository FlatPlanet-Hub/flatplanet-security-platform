namespace FlatPlanet.Security.Application.Interfaces.Services;

public interface ISupabaseAuthClient
{
    Task<SupabaseAuthResult?> SignInAsync(string email, string password);
}

public class SupabaseAuthResult
{
    public Guid UserId { get; set; }
    public string Email { get; set; } = string.Empty;
}
