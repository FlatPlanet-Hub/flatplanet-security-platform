using FlatPlanet.Security.Domain.Entities;

namespace FlatPlanet.Security.Application.Interfaces.Repositories;

public interface IMfaBackupCodeRepository
{
    Task CreateManyAsync(IEnumerable<MfaBackupCode> codes);
    Task<MfaBackupCode?> GetUnusedByUserAndHashAsync(Guid userId, string codeHash);
    Task MarkUsedAsync(Guid id);
    Task DeleteAllByUserAsync(Guid userId);
    Task<int> CountUnusedByUserAsync(Guid userId);
}
