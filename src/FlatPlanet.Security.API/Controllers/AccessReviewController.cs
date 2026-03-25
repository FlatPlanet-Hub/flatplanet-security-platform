using FlatPlanet.Security.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FlatPlanet.Security.API.Controllers;

[ApiController]
[Route("api/v1/access-review")]
[Authorize(Policy = "AdminAccess")]
public class AccessReviewController : ControllerBase
{
    private readonly IAccessReviewService _accessReview;

    public AccessReviewController(IAccessReviewService accessReview) => _accessReview = accessReview;

    [HttpGet]
    public async Task<IActionResult> GetAccessReview(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] Guid? companyId = null,
        [FromQuery] Guid? appId = null)
    {
        var result = await _accessReview.GetAccessReviewAsync(page, pageSize, companyId, appId);
        return Ok(new { success = true, data = result });
    }
}
