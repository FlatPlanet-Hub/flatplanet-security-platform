using FlatPlanet.Security.Application.DTOs.Compliance;

namespace FlatPlanet.Security.Application.Interfaces.Services;

public interface IComplianceService
{
    Task<ComplianceExportResponse> ExportUserDataAsync(Guid userId);
    Task AnonymizeUserAsync(Guid userId, Guid requestedBy);
}
