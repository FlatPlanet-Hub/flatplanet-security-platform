namespace FlatPlanet.Security.Application.Interfaces.Services;

public interface IEmailService
{
    Task SendPasswordResetEmailAsync(string toEmail, string resetLink);
    Task SendMfaOtpEmailAsync(string toEmail, string otpCode, int expiryMinutes);
}
