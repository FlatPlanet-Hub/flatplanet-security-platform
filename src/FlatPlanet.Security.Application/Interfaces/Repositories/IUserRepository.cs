using System.Data;
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
    Task UpdateStatusAsync(Guid userId, string status, IDbConnection conn, IDbTransaction tx);
    Task SuspendByCompanyIdAsync(Guid companyId);
    Task SuspendByCompanyIdAsync(Guid companyId, IDbConnection conn, IDbTransaction tx);
    Task DeactivateAllByCompanyIdAsync(Guid companyId, IDbConnection conn, IDbTransaction tx);
    Task<User> CreateAsync(User user);
    Task UpdatePasswordHashAsync(Guid userId, string passwordHash);
    Task UpdatePasswordHashAsync(Guid userId, string passwordHash, System.Data.IDbConnection conn, System.Data.IDbTransaction tx);
    Task UpdateMfaEnabledAsync(Guid userId, bool enabled);
    Task UpdateMfaTotpSecretAsync(Guid userId, string encryptedSecret);
    Task SetMfaTotpEnrolledAsync(Guid userId, bool enrolled);
    Task UpdateMfaTotpLastUsedStepAsync(Guid userId, long step);
    /// <summary>Returns false if the user was not found.</summary>
    Task<bool> ResetMfaColumnsAsync(Guid userId);
}
