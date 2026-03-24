using FlatPlanet.Security.Domain.Entities;

namespace FlatPlanet.Security.Application.Interfaces.Repositories;

public interface IUserRepository
{
    Task<User?> GetByIdAsync(Guid id);
    Task<User?> GetByEmailAsync(string email);
    Task UpdateLastSeenAtAsync(Guid userId, DateTime lastSeenAt);
    Task UpdateStatusAsync(Guid userId, string status);
}
