using FlatPlanet.Security.Domain.Entities;

namespace FlatPlanet.Security.Application.Interfaces.Services;

public interface IJwtService
{
    string IssueAccessToken(User user);
    (string token, string hash) GenerateRefreshToken();
    string HashToken(string token);
}
