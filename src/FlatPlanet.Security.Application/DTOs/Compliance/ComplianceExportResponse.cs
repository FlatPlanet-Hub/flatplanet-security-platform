using FlatPlanet.Security.Application.DTOs.Admin;
using FlatPlanet.Security.Application.DTOs.Audit;

namespace FlatPlanet.Security.Application.DTOs.Compliance;

public class ComplianceExportResponse
{
    public UserResponse User { get; set; } = new();
    public IEnumerable<UserAccessResponse> AppRoles { get; set; } = Enumerable.Empty<UserAccessResponse>();
    public IEnumerable<SessionDto> Sessions { get; set; } = Enumerable.Empty<SessionDto>();
    public IEnumerable<AuditLogResponse> AuditEvents { get; set; } = Enumerable.Empty<AuditLogResponse>();
    public DateTime ExportedAt { get; set; } = DateTime.UtcNow;
}

public class SessionDto
{
    public Guid Id { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public bool IsActive { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? LastActiveAt { get; set; }
}
