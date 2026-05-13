namespace FlatPlanet.Security.Application.Interfaces.Services;

public interface IEmailBackgroundQueue
{
    void EnqueueOtp(string toEmail, string otp, int expiryMinutes, Guid userId);
    void CompleteAdding();
}
