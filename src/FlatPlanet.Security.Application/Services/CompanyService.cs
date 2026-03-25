using System.Text.Json;
using FlatPlanet.Security.Application.DTOs.Admin;
using FlatPlanet.Security.Application.Interfaces.Repositories;
using FlatPlanet.Security.Application.Interfaces.Services;
using FlatPlanet.Security.Domain.Entities;
using FlatPlanet.Security.Domain.Enums;

namespace FlatPlanet.Security.Application.Services;

public class CompanyService : ICompanyService
{
    private readonly ICompanyRepository _companies;
    private readonly IUserRepository _users;
    private readonly IUserAppRoleRepository _userAppRoles;
    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly IAuditLogRepository _auditLog;

    public CompanyService(
        ICompanyRepository companies,
        IUserRepository users,
        IUserAppRoleRepository userAppRoles,
        IRefreshTokenRepository refreshTokens,
        IAuditLogRepository auditLog)
    {
        _companies = companies;
        _users = users;
        _userAppRoles = userAppRoles;
        _refreshTokens = refreshTokens;
        _auditLog = auditLog;
    }

    public async Task<IEnumerable<CompanyResponse>> GetAllAsync()
    {
        var companies = await _companies.GetAllAsync();
        return companies.Select(Map);
    }

    public async Task<CompanyResponse> GetByIdAsync(Guid id)
    {
        var company = await _companies.GetByIdAsync(id)
            ?? throw new KeyNotFoundException("Company not found.");
        return Map(company);
    }

    public async Task<CompanyResponse> CreateAsync(CreateCompanyRequest request)
    {
        var company = new Company
        {
            Name = request.Name,
            CountryCode = request.CountryCode,
            Status = "active"
        };
        var created = await _companies.CreateAsync(company);
        return Map(created);
    }

    public async Task<CompanyResponse> UpdateAsync(Guid id, UpdateCompanyRequest request)
    {
        var company = await _companies.GetByIdAsync(id)
            ?? throw new KeyNotFoundException("Company not found.");
        company.Name = request.Name;
        company.CountryCode = request.CountryCode;
        await _companies.UpdateAsync(company);
        return Map(company);
    }

    public async Task UpdateStatusAsync(Guid id, string status)
    {
        _ = await _companies.GetByIdAsync(id)
            ?? throw new KeyNotFoundException("Company not found.");

        await _companies.UpdateStatusAsync(id, status);

        if (status == "suspended")
        {
            await _users.SuspendByCompanyIdAsync(id);
            await _refreshTokens.RevokeAllByCompanyIdAsync(id, "company_suspended");

            await _auditLog.LogAsync(new AuthAuditLog
            {
                UserId = null,
                EventType = AuditEventType.CompanySuspended,
                Details = JsonSerializer.Serialize(new { company_id = id, status })
            });
        }
        else if (status == "inactive")
        {
            var users = await _users.GetByCompanyIdAsync(id);
            foreach (var user in users)
            {
                await _userAppRoles.SuspendAllByUserAsync(user.Id);
                await _users.UpdateStatusAsync(user.Id, status);
            }

            await _auditLog.LogAsync(new AuthAuditLog
            {
                UserId = null,
                EventType = AuditEventType.CompanySuspended,
                Details = JsonSerializer.Serialize(new { company_id = id, status })
            });
        }
    }

    private static CompanyResponse Map(Company c) => new()
    {
        Id = c.Id,
        Name = c.Name,
        CountryCode = c.CountryCode,
        Status = c.Status,
        CreatedAt = c.CreatedAt
    };
}
