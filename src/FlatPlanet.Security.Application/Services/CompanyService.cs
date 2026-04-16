using System.Text.Json;
using FlatPlanet.Security.Application.DTOs.Admin;
using FlatPlanet.Security.Application.Interfaces;
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
    private readonly ISessionRepository _sessions;
    private readonly IAuditLogRepository _auditLog;
    private readonly IDbConnectionFactory _db;

    public CompanyService(
        ICompanyRepository companies,
        IUserRepository users,
        IUserAppRoleRepository userAppRoles,
        IRefreshTokenRepository refreshTokens,
        ISessionRepository sessions,
        IAuditLogRepository auditLog,
        IDbConnectionFactory db)
    {
        _companies = companies;
        _users = users;
        _userAppRoles = userAppRoles;
        _refreshTokens = refreshTokens;
        _sessions = sessions;
        _auditLog = auditLog;
        _db = db;
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
            Status = "active",
            Code = request.Code
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
        company.Code = request.Code;
        await _companies.UpdateAsync(company);
        return Map(company);
    }

    public async Task UpdateStatusAsync(Guid id, string status)
    {
        _ = await _companies.GetByIdAsync(id)
            ?? throw new KeyNotFoundException("Company not found.");

        if (status == "suspended")
        {
            using var conn = await _db.CreateConnectionAsync();
            using var tx = conn.BeginTransaction();
            try
            {
                await _companies.UpdateStatusAsync(id, status, conn, tx);
                await _users.SuspendByCompanyIdAsync(id, conn, tx);
                await _refreshTokens.RevokeAllByCompanyIdAsync(id, "company_suspended", conn, tx);
                await _sessions.EndAllActiveSessionsByCompanyIdAsync(id, "company_suspended", conn, tx);

                await _auditLog.LogAsync(new AuthAuditLog
                {
                    UserId = null,
                    EventType = AuditEventType.CompanySuspended,
                    Details = JsonSerializer.Serialize(new { company_id = id, status })
                }, conn, tx);

                tx.Commit();
            }
            catch
            {
                tx.Rollback();
                throw;
            }
        }
        else if (status == "inactive")
        {
            using var conn = await _db.CreateConnectionAsync();
            using var tx = conn.BeginTransaction();
            try
            {
                await _companies.UpdateStatusAsync(id, status, conn, tx);
                await _users.DeactivateAllByCompanyIdAsync(id, conn, tx);
                await _userAppRoles.SuspendAllByCompanyIdAsync(id, conn, tx);
                await _refreshTokens.RevokeAllByCompanyIdAsync(id, "company_deactivated", conn, tx);
                await _sessions.EndAllActiveSessionsByCompanyIdAsync(id, "company_deactivated", conn, tx);

                await _auditLog.LogAsync(new AuthAuditLog
                {
                    UserId = null,
                    EventType = AuditEventType.CompanyDeactivated,
                    Details = JsonSerializer.Serialize(new { company_id = id, status })
                }, conn, tx);

                tx.Commit();
            }
            catch
            {
                tx.Rollback();
                throw;
            }
        }
        else
        {
            await _companies.UpdateStatusAsync(id, status);
        }
    }

    private static CompanyResponse Map(Company c) => new()
    {
        Id = c.Id,
        Name = c.Name,
        CountryCode = c.CountryCode,
        Status = c.Status,
        Code = c.Code,
        CreatedAt = c.CreatedAt
    };
}
