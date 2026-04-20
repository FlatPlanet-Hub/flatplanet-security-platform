using FlatPlanet.Security.Application.DTOs.Auth;
using FlatPlanet.Security.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FlatPlanet.Security.API.Controllers;

[ApiController]
[Route("api/v1/admin/users")]
[Authorize(Policy = "AdminAccess")]
public class AdminUserController : ApiController
{
    private readonly IAuthService _auth;

    public AdminUserController(IAuthService auth) => _auth = auth;

    [HttpPost("{userId}/force-reset-password")]
    public async Task<IActionResult> ForceResetPassword(Guid userId, [FromBody] AdminForceResetPasswordRequest request)
    {
        await _auth.AdminForceResetPasswordAsync(userId, request.AppSlug, GetUserId());
        return OkData(new { message = "Password reset email sent." });
    }
}
