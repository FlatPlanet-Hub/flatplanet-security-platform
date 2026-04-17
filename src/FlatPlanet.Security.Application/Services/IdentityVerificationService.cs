using System.Text.Json;
using FlatPlanet.Security.Application.DTOs.Identity;
using FlatPlanet.Security.Application.Interfaces.Repositories;
using FlatPlanet.Security.Application.Interfaces.Services;
using FlatPlanet.Security.Domain.Entities;
using FlatPlanet.Security.Domain.Enums;
using Microsoft.Extensions.Caching.Memory;

namespace FlatPlanet.Security.Application.Services;

public class IdentityVerificationService : IIdentityVerificationService
{
    private readonly IIdentityVerificationRepository _repo;
    private readonly ISecurityConfigRepository _securityConfig;
    private readonly IAuditLogRepository _auditLog;
    private readonly IMemoryCache _cache;

    public IdentityVerificationService(
        IIdentityVerificationRepository repo,
        ISecurityConfigRepository securityConfig,
        IAuditLogRepository auditLog,
        IMemoryCache cache)
    {
        _repo = repo;
        _securityConfig = securityConfig;
        _auditLog = auditLog;
        _cache = cache;
    }

    public async Task SyncStatusAsync(Guid userId, bool mfaTotpEnrolled)
    {
        var requireVideo  = await GetRequireVideoAsync();
        var videoVerified = false;
        var fullyVerified = mfaTotpEnrolled && (!requireVideo || videoVerified);

        var existing = await _repo.GetByUserIdAsync(userId);
        var wasFullyVerified = existing?.FullyVerified ?? false;

        var now = DateTime.UtcNow;
        var record = new IdentityVerificationStatus
        {
            UserId        = userId,
            MfaVerified   = mfaTotpEnrolled,
            VideoVerified = videoVerified,
            FullyVerified = fullyVerified,
            VerifiedAt    = fullyVerified && !wasFullyVerified ? now : existing?.VerifiedAt,
            UpdatedAt     = now
        };

        await _repo.UpsertAsync(record);

        if (fullyVerified && !wasFullyVerified)
        {
            await _auditLog.LogAsync(new AuthAuditLog
            {
                UserId    = userId,
                EventType = AuditEventType.IdentityVerificationCompleted,
                Details   = JsonSerializer.Serialize(new { mfaTotpEnrolled, videoVerified })
            });
        }
    }

    public async Task<IdentityVerificationStatusDto> GetStatusAsync(Guid userId)
    {
        var existing = await _repo.GetByUserIdAsync(userId);
        if (existing is null)
            return new IdentityVerificationStatusDto();

        // Recompute fullyVerified from current config — do NOT trust stored DB value
        var requireVideo  = await GetRequireVideoAsync();
        var fullyVerified = existing.MfaVerified && (!requireVideo || existing.VideoVerified);

        return new IdentityVerificationStatusDto
        {
            MfaVerified   = existing.MfaVerified,
            VideoVerified = existing.VideoVerified,
            FullyVerified = fullyVerified,
            VerifiedAt    = existing.VerifiedAt
        };
    }

    private async Task<bool> GetRequireVideoAsync()
    {
        return await _cache.GetOrCreateAsync("require_video_verification", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);
            var raw = await _securityConfig.GetValueAsync("require_video_verification");
            return string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase);
        });
    }
}
