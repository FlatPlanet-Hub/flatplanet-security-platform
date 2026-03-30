namespace FlatPlanet.Security.Application.Interfaces.Services;

public interface ISmsSender
{
    Task SendAsync(string to, string body);
}
