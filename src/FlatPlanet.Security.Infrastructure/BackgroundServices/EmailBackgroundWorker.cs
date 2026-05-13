using FlatPlanet.Security.Application.Interfaces.Services;
using FlatPlanet.Security.Infrastructure.Email;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FlatPlanet.Security.Infrastructure.BackgroundServices;

public class EmailBackgroundWorker : BackgroundService
{
    private readonly EmailBackgroundQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<EmailBackgroundWorker> _logger;

    public EmailBackgroundWorker(
        EmailBackgroundQueue queue,
        IServiceScopeFactory scopeFactory,
        ILogger<EmailBackgroundWorker> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Email background worker started.");
        try
        {
            await foreach (var job in _queue.Reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var email = scope.ServiceProvider.GetRequiredService<IEmailService>();
                    await email.SendMfaOtpEmailAsync(job.ToEmail, job.Otp, job.ExpiryMinutes);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to send OTP email to {Email}", job.ToEmail);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown — not an error
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex,
                "Email background worker crashed unexpectedly. MFA OTP email delivery is disabled until the service restarts.");
            throw; // re-throw so the host knows the service failed
        }
        finally
        {
            _logger.LogInformation("Email background worker stopped.");
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _queue.CompleteAdding(); // signal no more items
        // Give up to 5 seconds to drain remaining jobs
        using var drainCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(drainCts.Token, cancellationToken);
        try
        {
            await DrainAsync(linked.Token);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Email background worker shutdown: some queued OTP emails were not sent.");
        }
        await base.StopAsync(cancellationToken);
    }

    private async Task DrainAsync(CancellationToken ct)
    {
        await foreach (var job in _queue.Reader.ReadAllAsync(ct))
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var email = scope.ServiceProvider.GetRequiredService<IEmailService>();
                await email.SendMfaOtpEmailAsync(job.ToEmail, job.Otp, job.ExpiryMinutes);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send OTP email to {Email} during shutdown drain", job.ToEmail);
            }
        }
    }
}
