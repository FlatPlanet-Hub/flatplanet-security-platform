using FlatPlanet.Security.Domain.Entities;

namespace FlatPlanet.Security.Application.Interfaces.Services;

public interface IJwtService
{
    Task<string> IssueAccessTokenAsync(User user, Guid sessionId, IEnumerable<string> roles);
    (string token, string hash) GenerateRefreshToken();
    string HashToken(string token);
}
