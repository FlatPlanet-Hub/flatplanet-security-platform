namespace FlatPlanet.Security.Application.DTOs.Users;

public class UserQueryParams
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public Guid? CompanyId { get; set; }
    public string? Status { get; set; }
    public string? Search { get; set; }
}
