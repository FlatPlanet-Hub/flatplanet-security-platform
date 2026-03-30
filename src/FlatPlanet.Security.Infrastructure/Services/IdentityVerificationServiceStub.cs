using FlatPlanet.Security.Application.Interfaces.Services;

namespace FlatPlanet.Security.Infrastructure.Services;

public class IdentityVerificationServiceStub : IIdentityVerificationService
{
    public Task SyncStatusAsync(Guid userId) => Task.CompletedTask;
}
