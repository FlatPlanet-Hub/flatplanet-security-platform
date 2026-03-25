using FlatPlanet.Security.Application.DTOs.Users;
using FlatPlanet.Security.Domain.Entities;

namespace FlatPlanet.Security.Application.Interfaces.Repositories;

public interface IUserRepository
{
    Task<User?> GetByIdAsync(Guid id);
    Task<User?> GetByEmailAsync(string email);
    Task<IEnumerable<User>> GetAllAsync();
    Task<IEnumerable<User>> GetByCompanyIdAsync(Guid companyId);
    Task<PagedResult<User>> GetPagedAsync(UserQueryParams query);
    Task UpdateAsync(User user);
    Task UpdateLastSeenAtAsync(Guid userId, DateTime lastSeenAt);
    Task UpdateStatusAsync(Guid userId, string status);
    Task SuspendByCompanyIdAsync(Guid companyId);
}
