using FlatPlanet.Security.Domain.Entities;

namespace FlatPlanet.Security.Application.Interfaces.Repositories;

public interface ILoginAttemptRepository
{
    Task RecordAsync(LoginAttempt attempt);
    Task<int> CountRecentByEmailAsync(string email, DateTime since);
    Task<int> CountRecentFailuresByEmailAsync(string email, DateTime since);
    Task<int> CountRecentFailuresByIpAsync(string ipAddress, DateTime since);
    Task DeleteOlderThanAsync(int retentionDays);
}
