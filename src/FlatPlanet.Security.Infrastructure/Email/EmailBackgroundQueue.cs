using System.Threading.Channels;
using FlatPlanet.Security.Application.Interfaces.Services;
using Microsoft.Extensions.Logging;

namespace FlatPlanet.Security.Infrastructure.Email;

public record OtpEmailJob(string ToEmail, string Otp, int ExpiryMinutes, Guid UserId);

public sealed class EmailBackgroundQueue : IEmailBackgroundQueue, IAsyncDisposable
{
    private readonly Channel<OtpEmailJob> _channel = Channel.CreateBounded<OtpEmailJob>(new BoundedChannelOptions(1000)
    {
        FullMode = BoundedChannelFullMode.DropOldest,
        SingleReader = true,
        SingleWriter = false
    });
    private readonly ILogger<EmailBackgroundQueue> _logger;

    public EmailBackgroundQueue(ILogger<EmailBackgroundQueue> logger)
    {
        _logger = logger;
    }

    public ChannelReader<OtpEmailJob> Reader => _channel.Reader;

    public void EnqueueOtp(string toEmail, string otp, int expiryMinutes, Guid userId)
    {
        if (!_channel.Writer.TryWrite(new OtpEmailJob(toEmail, otp, expiryMinutes, userId)))
            _logger.LogWarning("OTP email queue full — dropped OTP email for user {UserId}", userId);
    }

    public void CompleteAdding() => _channel.Writer.TryComplete();

    public ValueTask DisposeAsync()
    {
        _channel.Writer.TryComplete();
        return ValueTask.CompletedTask;
    }
}
