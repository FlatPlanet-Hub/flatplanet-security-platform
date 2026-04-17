using FlatPlanet.Security.Application.DTOs.Identity;

namespace FlatPlanet.Security.Application.Interfaces.Services;

public interface IIdentityVerificationService
{
    Task SyncStatusAsync(Guid userId, bool mfaTotpEnrolled);
    Task<IdentityVerificationStatusDto> GetStatusAsync(Guid userId);
}
