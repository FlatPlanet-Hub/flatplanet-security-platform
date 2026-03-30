using System.Security.Claims;
using FlatPlanet.Security.Application.DTOs.Admin;
using FlatPlanet.Security.Application.Interfaces.Repositories;
using FlatPlanet.Security.Application.Interfaces.Services;
using FlatPlanet.Security.Domain.Entities;
using FlatPlanet.Security.Domain.Enums;
using Microsoft.AspNetCore.Http;

namespace FlatPlanet.Security.Application.Services;

public class AppService : IAppService
{
    private readonly IAppRepository _apps;
    private readonly IAdminAuditLogRepository _adminAudit;
    private readonly IHttpContextAccessor _httpContext;

    public AppService(IAppRepository apps, IAdminAuditLogRepository adminAudit, IHttpContextAccessor httpContext)
    {
        _apps = apps;
        _adminAudit = adminAudit;
        _httpContext = httpContext;
    }

    public async Task<IEnumerable<AppResponse>> GetAllAsync()
    {
        var apps = await _apps.GetAllAsync();
        return apps.Select(Map);
    }

    public async Task<AppResponse> GetByIdAsync(Guid id)
    {
        var app = await _apps.GetByIdAsync(id)
            ?? throw new KeyNotFoundException("App not found.");
        return Map(app);
    }

    public async Task<AppResponse> CreateAsync(CreateAppRequest request, Guid registeredBy)
    {
        var app = new App
        {
            CompanyId    = request.CompanyId,
            Name         = request.Name,
            Slug         = request.Slug,
            BaseUrl      = request.BaseUrl,
            Status       = "active",
            RegisteredBy = registeredBy
        };
        var created = await _apps.CreateAsync(app);

        await _adminAudit.LogAsync(
            GetActorId(), GetActorEmail(), AdminAction.AppRegister,
            "app", created.Id,
            null,
            new { created.Id, created.Name, created.Slug, created.Status },
            GetIpAddress());

        return Map(created);
    }

    public async Task<AppResponse> UpdateAsync(Guid id, UpdateAppRequest request)
    {
        var app = await _apps.GetByIdAsync(id)
            ?? throw new KeyNotFoundException("App not found.");

        var before = new { app.Name, app.BaseUrl, app.Status };
        app.Name    = request.Name;
        app.BaseUrl = request.BaseUrl;
        app.Status  = request.Status;
        await _apps.UpdateAsync(app);

        var action = request.Status == "inactive" ? AdminAction.AppDeactivate : AdminAction.AppUpdate;

        await _adminAudit.LogAsync(
            GetActorId(), GetActorEmail(), action,
            "app", id,
            before,
            new { app.Name, app.BaseUrl, app.Status },
            GetIpAddress());

        return Map(app);
    }

    private Guid GetActorId()
    {
        var sub = _httpContext.HttpContext?.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
               ?? _httpContext.HttpContext?.User.FindFirst("sub")?.Value;
        return Guid.TryParse(sub, out var id) ? id : Guid.Empty;
    }

    private string GetActorEmail() =>
        _httpContext.HttpContext?.User.FindFirst(ClaimTypes.Email)?.Value
        ?? _httpContext.HttpContext?.User.FindFirst("email")?.Value
        ?? "unknown";

    private string? GetIpAddress() =>
        _httpContext.HttpContext?.Connection.RemoteIpAddress?.ToString();

    private static AppResponse Map(App a) => new()
    {
        Id           = a.Id,
        CompanyId    = a.CompanyId,
        Name         = a.Name,
        Slug         = a.Slug,
        BaseUrl      = a.BaseUrl,
        Status       = a.Status,
        RegisteredAt = a.RegisteredAt
    };
}
