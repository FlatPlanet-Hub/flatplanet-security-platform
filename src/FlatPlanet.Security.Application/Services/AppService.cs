using FlatPlanet.Security.Application.DTOs.Admin;
using FlatPlanet.Security.Application.Helpers;
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
            ActorContext.GetActorId(_httpContext), ActorContext.GetActorEmail(_httpContext), AdminAction.AppRegister,
            "app", created.Id,
            null,
            new { created.Id, created.Name, created.Slug, created.Status },
            ActorContext.GetIpAddress(_httpContext));

        return Map(created);
    }

    public async Task<AppResponse> UpdateAsync(Guid id, UpdateAppRequest request)
    {
        var app = await _apps.GetByIdAsync(id)
            ?? throw new KeyNotFoundException("App not found.");

        var before = new { app.Name, app.Slug, app.BaseUrl, app.Status };
        app.Name   = request.Name;
        app.Status = request.Status;
        if (request.BaseUrl is not null)
            app.BaseUrl = request.BaseUrl;

        await _apps.UpdateAsync(app);

        // Slug update is separate — prevents accidental overwrites during normal app updates.
        if (!string.IsNullOrWhiteSpace(request.Slug) && request.Slug != app.Slug)
            await _apps.UpdateSlugAsync(app.Id, request.Slug);

        var action = request.Status == "inactive" ? AdminAction.AppDeactivate : AdminAction.AppUpdate;

        await _adminAudit.LogAsync(
            ActorContext.GetActorId(_httpContext), ActorContext.GetActorEmail(_httpContext), action,
            "app", id,
            before,
            new { app.Name, app.BaseUrl, app.Status },
            ActorContext.GetIpAddress(_httpContext));

        return Map(app);
    }

    public async Task DeleteAsync(Guid id)
    {
        var app = await _apps.GetByIdAsync(id)
            ?? throw new KeyNotFoundException("App not found.");

        if (app.Status != "inactive")
            throw new InvalidOperationException("Only inactive apps can be hard-deleted. Deactivate the app first.");

        await _adminAudit.LogAsync(
            ActorContext.GetActorId(_httpContext), ActorContext.GetActorEmail(_httpContext), AdminAction.AppDelete,
            "app", id,
            new { app.Id, app.Name, app.Slug, app.Status },
            null,
            ActorContext.GetIpAddress(_httpContext));

        await _apps.DeleteAsync(id);
    }

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
