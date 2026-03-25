using System.Text.Json;
using FlatPlanet.Security.Application.DTOs.Admin;
using FlatPlanet.Security.Application.DTOs.Audit;
using FlatPlanet.Security.Application.DTOs.Compliance;
using FlatPlanet.Security.Application.Interfaces.Repositories;
using FlatPlanet.Security.Application.Interfaces.Services;
using FlatPlanet.Security.Domain.Entities;
using FlatPlanet.Security.Domain.Enums;

namespace FlatPlanet.Security.Application.Services;

public class ComplianceService : IComplianceService
{
    private readonly IUserRepository _users;
    private readonly IUserAppRoleRepository _userAppRoles;
    private readonly IRoleRepository _roles;
    private readonly IAppRepository _apps;
    private readonly ISessionRepository _sessions;
    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly IAuditLogRepository _auditLog;

    public ComplianceService(
        IUserRepository users,
        IUserAppRoleRepository userAppRoles,
        IRoleRepository roles,
        IAppRepository apps,
        ISessionRepository sessions,
        IRefreshTokenRepository refreshTokens,
        IAuditLogRepository auditLog)
    {
        _users = users;
        _userAppRoles = userAppRoles;
        _roles = roles;
        _apps = apps;
        _sessions = sessions;
        _refreshTokens = refreshTokens;
        _auditLog = auditLog;
    }

    public async Task<ComplianceExportResponse> ExportUserDataAsync(Guid userId)
    {
        var user = await _users.GetByIdAsync(userId)
            ?? throw new KeyNotFoundException("User not found.");

        var userRoles = (await _userAppRoles.GetActiveByUserAsync(userId)).ToList();
        var appRoles = new List<UserAccessResponse>();
        foreach (var ur in userRoles)
        {
            var role = await _roles.GetByIdAsync(ur.RoleId);
            var app = await _apps.GetByIdAsync(ur.AppId);
            if (role == null || app == null) continue;
            appRoles.Add(new UserAccessResponse
            {
                Id = ur.Id,
                UserId = ur.UserId,
                UserEmail = user.Email,
                UserFullName = user.FullName,
                RoleId = ur.RoleId,
                RoleName = role.Name,
                Status = ur.Status,
                ExpiresAt = ur.ExpiresAt
            });
        }

        var sessions = (await _sessions.GetAllByUserIdAsync(userId))
            .Select(s => new SessionDto
            {
                Id = s.Id,
                IpAddress = s.IpAddress,
                UserAgent = s.UserAgent,
                IsActive = s.IsActive,
                StartedAt = s.StartedAt,
                LastActiveAt = s.LastActiveAt
            });

        var auditEvents = (await _auditLog.GetByUserIdAsync(userId))
            .Select(a => new AuditLogResponse
            {
                Id = a.Id,
                UserId = a.UserId,
                AppId = a.AppId,
                EventType = a.EventType,
                IpAddress = a.IpAddress,
                UserAgent = a.UserAgent,
                Details = a.Details,
                CreatedAt = a.CreatedAt
            });

        return new ComplianceExportResponse
        {
            User = new UserResponse
            {
                Id = user.Id,
                CompanyId = user.CompanyId,
                Email = user.Email,
                FullName = user.FullName,
                RoleTitle = user.RoleTitle,
                Status = user.Status,
                CreatedAt = user.CreatedAt,
                LastSeenAt = user.LastSeenAt
            },
            AppRoles = appRoles,
            Sessions = sessions,
            AuditEvents = auditEvents
        };
    }

    public async Task AnonymizeUserAsync(Guid userId, Guid requestedBy)
    {
        var user = await _users.GetByIdAsync(userId)
            ?? throw new KeyNotFoundException("User not found.");

        user.Email = $"anonymized_{userId:N}@deleted.invalid";
        user.FullName = "Anonymized User";
        user.RoleTitle = null;
        await _users.UpdateAsync(user);

        await _users.UpdateStatusAsync(userId, "inactive");
        await _sessions.EndAllActiveSessionsByUserAsync(userId, "anonymized");
        await _refreshTokens.RevokeAllByUserAsync(userId, "anonymized");

        await _auditLog.LogAsync(new AuthAuditLog
        {
            UserId = userId,
            EventType = AuditEventType.UserAnonymized,
            Details = JsonSerializer.Serialize(new { requested_by = requestedBy })
        });
    }
}
