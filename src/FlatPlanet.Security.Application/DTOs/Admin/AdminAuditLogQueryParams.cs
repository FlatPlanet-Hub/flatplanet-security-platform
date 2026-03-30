namespace FlatPlanet.Security.Application.DTOs.Admin;

public class AdminAuditLogQueryParams
{
    public int       Page       { get; set; } = 1;
    public int       PageSize   { get; set; } = 50;
    public string?   Action     { get; set; }
    public string?   TargetType { get; set; }
    public Guid?     ActorId    { get; set; }
    public DateTime? From       { get; set; }
    public DateTime? To         { get; set; }
}
