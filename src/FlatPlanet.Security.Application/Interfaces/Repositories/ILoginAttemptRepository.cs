using FlatPlanet.Security.Domain.Entities;

namespace FlatPlanet.Security.Application.Interfaces.Repositories;

public record LoginCheckCounts(int IpFailures, int EmailAttempts, int LockoutFailures);

public interface ILoginAttemptRepository
{
    Task RecordAsync(LoginAttempt attempt);
    Task<LoginCheckCounts> GetLoginChecksAsync(string email, string? ipAddress, DateTime ipSince, DateTime emailSince, DateTime lockoutSince);
    Task<int> CountRecentByEmailAsync(string email, DateTime since);
    Task<int> CountRecentFailuresByEmailAsync(string email, DateTime since);
    Task<int> CountRecentFailuresByIpAsync(string ipAddress, DateTime since);
    Task DeleteOlderThanAsync(int retentionDays);
}
