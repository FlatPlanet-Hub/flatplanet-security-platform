using FlatPlanet.Security.Application.Interfaces.Services;

namespace FlatPlanet.Security.Infrastructure.ExternalServices;

public class ConsoleSmsSender : ISmsSender
{
    public Task SendAsync(string to, string body)
    {
        Console.WriteLine($"[SMS to {to}]: {body}");
        return Task.CompletedTask;
    }
}
