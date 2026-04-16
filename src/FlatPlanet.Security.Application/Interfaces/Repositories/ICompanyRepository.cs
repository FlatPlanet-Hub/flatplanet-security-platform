using FlatPlanet.Security.Domain.Entities;

namespace FlatPlanet.Security.Application.Interfaces.Repositories;

public interface ICompanyRepository
{
    Task<Company?> GetByIdAsync(Guid id);
    Task<IEnumerable<Company>> GetAllAsync();
    Task<Company> CreateAsync(Company company);
    Task UpdateAsync(Company company);
    Task UpdateStatusAsync(Guid id, string status);
    Task UpdateStatusAsync(Guid id, string status, System.Data.IDbConnection conn, System.Data.IDbTransaction tx);
}
