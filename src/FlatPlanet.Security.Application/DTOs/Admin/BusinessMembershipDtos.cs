using System.ComponentModel.DataAnnotations;

namespace FlatPlanet.Security.Application.DTOs.Admin;

public class AddMemberRequest
{
    [Required]
    public Guid UserId { get; set; }
    public string Role { get; set; } = "member";
}

public class MemberResponse
{
    public Guid UserId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime JoinedAt { get; set; }
}
