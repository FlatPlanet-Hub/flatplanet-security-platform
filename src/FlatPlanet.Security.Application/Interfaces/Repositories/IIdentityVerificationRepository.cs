using FlatPlanet.Security.Domain.Entities;

namespace FlatPlanet.Security.Application.Interfaces.Repositories;

public interface IIdentityVerificationRepository
{
    Task<IdentityVerificationStatus?> GetByUserIdAsync(Guid userId);
    Task UpsertAsync(IdentityVerificationStatus status);
}
