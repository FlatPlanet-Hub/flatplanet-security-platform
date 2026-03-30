using FlatPlanet.Security.Application.Interfaces.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FlatPlanet.Security.Infrastructure.BackgroundServices;

public class AuditLogCleanupService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AuditLogCleanupService> _logger;

    public AuditLogCleanupService(IServiceScopeFactory scopeFactory, ILogger<AuditLogCleanupService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var config = scope.ServiceProvider.GetRequiredService<ISecurityConfigRepository>();
                var audit  = scope.ServiceProvider.GetRequiredService<IAdminAuditLogRepository>();
                var mfa    = scope.ServiceProvider.GetRequiredService<IMfaChallengeRepository>();

                var raw           = await config.GetValueAsync("audit_log_retention_days");
                var retentionDays = int.TryParse(raw, out var days) ? days : 1095;

                await audit.DeleteExpiredAsync(retentionDays);
                await mfa.DeleteExpiredAsync();
                _logger.LogInformation("Cleanup complete. Audit retention: {Days} days.", retentionDays);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Admin audit log cleanup failed.");
            }

            await Task.Delay(TimeSpan.FromHours(24), ct);
        }
    }
}
