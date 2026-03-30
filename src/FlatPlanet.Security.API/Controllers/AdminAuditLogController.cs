using FlatPlanet.Security.Application.DTOs.Admin;
using FlatPlanet.Security.Application.Interfaces.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FlatPlanet.Security.API.Controllers;

[ApiController]
[Route("api/v1/admin/audit-log")]
[Authorize(Policy = "PlatformOwner")]
public class AdminAuditLogController : ApiController
{
    private readonly IAdminAuditLogRepository _adminAudit;

    public AdminAuditLogController(IAdminAuditLogRepository adminAudit) => _adminAudit = adminAudit;

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] AdminAuditLogQueryParams query)
    {
        var items = await _adminAudit.GetPagedAsync(query);
        return OkData(items);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var item = await _adminAudit.GetByIdAsync(id);
        if (item is null) return NotFound(new { success = false, message = "Audit log entry not found." });
        return OkData(item);
    }
}
