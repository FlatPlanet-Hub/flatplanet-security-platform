using FlatPlanet.Security.Application.DTOs.Admin;
using FlatPlanet.Security.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FlatPlanet.Security.API.Controllers;

[ApiController]
[Route("api/v1/companies/{companyId:guid}/members")]
[Authorize(Policy = "PlatformOwner")]
public class BusinessMembershipController : ApiController
{
    private readonly IBusinessMembershipService _memberships;

    public BusinessMembershipController(IBusinessMembershipService memberships)
        => _memberships = memberships;

    [HttpGet]
    public async Task<IActionResult> GetMembers(Guid companyId)
    {
        var result = await _memberships.GetMembersAsync(companyId);
        return OkData(result);
    }

    [HttpPost]
    public async Task<IActionResult> AddMember(Guid companyId, [FromBody] AddMemberRequest request)
    {
        await _memberships.AddMemberAsync(companyId, request);
        return OkMessage("Member added.");
    }

    [HttpDelete("{userId:guid}")]
    public async Task<IActionResult> RemoveMember(Guid companyId, Guid userId)
    {
        await _memberships.RemoveMemberAsync(companyId, userId);
        return OkMessage("Member removed.");
    }
}
