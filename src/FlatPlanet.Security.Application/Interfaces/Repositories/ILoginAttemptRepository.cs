using FlatPlanet.Security.Domain.Entities;

namespace FlatPlanet.Security.Application.Interfaces.Repositories;

public interface ILoginAttemptRepository
{
    Task RecordAsync(LoginAttempt attempt);
    Task<int> CountRecentFailuresByEmailAsync(string email, DateTime since);
    Task<int> CountRecentByIpAsync(string ipAddress, DateTime since);
}
