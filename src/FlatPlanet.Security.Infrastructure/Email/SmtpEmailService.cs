using FlatPlanet.Security.Application.Common.Options;
using FlatPlanet.Security.Application.Interfaces.Services;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using Polly;
using Polly.Retry;

namespace FlatPlanet.Security.Infrastructure.Email;

public class SmtpEmailService : IEmailService
{
    private readonly SmtpOptions _options;
    private readonly ResiliencePipeline _pipeline;

    public SmtpEmailService(IOptions<SmtpOptions> options)
    {
        _options = options.Value;

        _pipeline = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 3,
                Delay = TimeSpan.FromSeconds(2),
                BackoffType = DelayBackoffType.Exponential,
                ShouldHandle = new PredicateBuilder().Handle<Exception>()
            })
            .AddTimeout(TimeSpan.FromSeconds(30))
            .Build();
    }

    public async Task SendPasswordResetEmailAsync(string toEmail, string resetLink)
    {
        var message = BuildPasswordResetMessage(toEmail, resetLink);
        await SendWithRetryAsync(message);
    }

    public async Task SendMfaOtpEmailAsync(string toEmail, string otpCode, int expiryMinutes)
    {
        var message = BuildMfaOtpMessage(toEmail, otpCode, expiryMinutes);
        await SendWithRetryAsync(message);
    }

    private MimeMessage BuildMfaOtpMessage(string toEmail, string otpCode, int expiryMinutes)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(_options.FromName, _options.FromEmail));
        message.To.Add(MailboxAddress.Parse(toEmail));
        message.Subject = "Your FlatPlanet login code";

        message.Body = new TextPart("plain")
        {
            Text = $"""
                Your FlatPlanet login verification code is:

                {otpCode}

                This code expires in {expiryMinutes} minutes.

                If you did not attempt to log in, please ignore this email and consider changing your password.
                """
        };

        return message;
    }

    private MimeMessage BuildPasswordResetMessage(string toEmail, string resetLink)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(_options.FromName, _options.FromEmail));
        message.To.Add(MailboxAddress.Parse(toEmail));
        message.Subject = "Reset your FlatPlanet password";

        message.Body = new TextPart("plain")
        {
            Text = $"""
                You requested a password reset. Click the link below to reset your password.
                This link expires in 15 minutes.

                {resetLink}

                If you did not request this, please ignore this email.
                """
        };

        return message;
    }

    private async Task SendWithRetryAsync(MimeMessage message)
    {
        await _pipeline.ExecuteAsync(async ct =>
        {
            using var client = new SmtpClient();
            await client.ConnectAsync(_options.Host, _options.Port, SecureSocketOptions.StartTls, ct);
            await client.AuthenticateAsync(_options.Username, _options.Password, ct);
            await client.SendAsync(message, cancellationToken: ct);
            await client.DisconnectAsync(true, ct);
        });
    }
}
