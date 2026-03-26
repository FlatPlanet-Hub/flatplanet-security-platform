using FlatPlanet.Security.Application.DTOs.Audit;
using FlatPlanet.Security.Application.Interfaces.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FlatPlanet.Security.API.Controllers;

[ApiController]
[Route("api/v1/audit")]
[Authorize(Policy = "AdminAccess")]
public class AuditController : ApiController
{
    private readonly IAuditLogRepository _auditLog;

    public AuditController(IAuditLogRepository auditLog) => _auditLog = auditLog;

    [HttpGet]
    public async Task<IActionResult> Query([FromQuery] AuditQueryParams p)
    {
        if (p.Page < 1) p.Page = 1;
        if (p.PageSize is < 1 or > 200) p.PageSize = 50;

        var (items, total) = await _auditLog.QueryAsync(
            p.UserId, p.AppId, p.EventType, p.From, p.To, p.Page, p.PageSize);

        var response = new PagedResult<AuditLogResponse>
        {
            Items = items.Select(a => new AuditLogResponse
            {
                Id = a.Id,
                UserId = a.UserId,
                AppId = a.AppId,
                EventType = a.EventType,
                IpAddress = a.IpAddress,
                UserAgent = a.UserAgent,
                Details = a.Details,
                CreatedAt = a.CreatedAt
            }),
            TotalCount = total,
            Page = p.Page,
            PageSize = p.PageSize
        };

        return OkData(response);
    }
}
