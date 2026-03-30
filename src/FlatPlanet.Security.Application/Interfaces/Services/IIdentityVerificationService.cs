namespace FlatPlanet.Security.Application.Interfaces.Services;

public interface IIdentityVerificationService
{
    Task SyncStatusAsync(Guid userId);
}
