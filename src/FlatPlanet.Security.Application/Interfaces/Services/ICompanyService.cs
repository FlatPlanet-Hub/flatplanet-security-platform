using FlatPlanet.Security.Application.DTOs.Admin;

namespace FlatPlanet.Security.Application.Interfaces.Services;

public interface ICompanyService
{
    Task<IEnumerable<CompanyResponse>> GetAllAsync();
    Task<CompanyResponse> GetByIdAsync(Guid id);
    Task<CompanyResponse> CreateAsync(CreateCompanyRequest request);
    Task<CompanyResponse> UpdateAsync(Guid id, UpdateCompanyRequest request);
    Task UpdateStatusAsync(Guid id, string status);
}
