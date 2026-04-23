using FlatPlanet.Security.Application.DTOs.Auth;

namespace FlatPlanet.Security.Application.Interfaces.Services;

public interface IProfileService
{
    Task<UserProfileResponse> GetProfileAsync(Guid userId, string? appSlug);
    Task<UpdateProfileResponse> UpdateProfileAsync(Guid userId, UpdateProfileRequest request, string? ipAddress);
}
