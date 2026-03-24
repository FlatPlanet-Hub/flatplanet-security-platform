namespace FlatPlanet.Security.Application.DTOs.Admin;

public class CreateRoleRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
}

public class UpdateRoleRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
}

public class AssignPermissionRequest
{
    public Guid PermissionId { get; set; }
}

public class RoleResponse
{
    public Guid Id { get; set; }
    public Guid? AppId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsPlatformRole { get; set; }
    public DateTime CreatedAt { get; set; }
}
