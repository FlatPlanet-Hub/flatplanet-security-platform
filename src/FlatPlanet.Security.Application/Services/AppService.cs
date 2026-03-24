using FlatPlanet.Security.Application.DTOs.Admin;
using FlatPlanet.Security.Application.Interfaces.Repositories;
using FlatPlanet.Security.Application.Interfaces.Services;
using FlatPlanet.Security.Domain.Entities;

namespace FlatPlanet.Security.Application.Services;

public class AppService : IAppService
{
    private readonly IAppRepository _apps;

    public AppService(IAppRepository apps) => _apps = apps;

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
            CompanyId = request.CompanyId,
            Name = request.Name,
            Slug = request.Slug,
            BaseUrl = request.BaseUrl,
            Status = "active",
            RegisteredBy = registeredBy
        };
        var created = await _apps.CreateAsync(app);
        return Map(created);
    }

    public async Task<AppResponse> UpdateAsync(Guid id, UpdateAppRequest request)
    {
        var app = await _apps.GetByIdAsync(id)
            ?? throw new KeyNotFoundException("App not found.");
        app.Name = request.Name;
        app.BaseUrl = request.BaseUrl;
        app.Status = request.Status;
        await _apps.UpdateAsync(app);
        return Map(app);
    }

    private static AppResponse Map(App a) => new()
    {
        Id = a.Id,
        CompanyId = a.CompanyId,
        Name = a.Name,
        Slug = a.Slug,
        BaseUrl = a.BaseUrl,
        Status = a.Status,
        RegisteredAt = a.RegisteredAt
    };
}
