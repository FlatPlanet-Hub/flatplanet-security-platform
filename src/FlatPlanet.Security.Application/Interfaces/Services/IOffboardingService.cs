namespace FlatPlanet.Security.Application.Interfaces.Services;

public interface IOffboardingService
{
    Task OffboardAsync(Guid userId, Guid requestedBy);
}
