namespace FlatPlanet.Security.Application.DTOs.Admin;

public class AdminAuditLogDto
{
    public Guid     Id         { get; set; }
    public string   ActorEmail { get; set; } = string.Empty;
    public string   Action     { get; set; } = string.Empty;
    public string   TargetType { get; set; } = string.Empty;
    public Guid?    TargetId   { get; set; }
    public DateTime CreatedAt  { get; set; }
}

public class AdminAuditLogDetailDto : AdminAuditLogDto
{
    public string? BeforeState { get; set; }
    public string? AfterState  { get; set; }
    public string? IpAddress   { get; set; }
}
