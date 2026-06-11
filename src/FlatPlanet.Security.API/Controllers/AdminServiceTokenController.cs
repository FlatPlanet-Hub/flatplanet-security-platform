using FlatPlanet.Security.Application.DTOs.ServiceTokens;
using FlatPlanet.Security.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FlatPlanet.Security.API.Controllers;

/// <summary>
/// Admin endpoints for managing per-service tokens. PlatformOwner only.
///
/// Lifecycle:
///   POST   /api/v1/admin/service-tokens                              — mint (plaintext shown once)
///   GET    /api/v1/admin/service-tokens                              — list
///   GET    /api/v1/admin/service-tokens/{id}                         — get one
///   PUT    /api/v1/admin/service-tokens/{id}/scopes                  — change scopes
///   DELETE /api/v1/admin/service-tokens/{id}                         — revoke
///   POST   /api/v1/admin/service-tokens/{id}/flush-cache             — invalidate validator cache
/// </summary>
[ApiController]
[Route("api/v1/admin/service-tokens")]
[Authorize(Policy = "PlatformOwner")]
public sealed class AdminServiceTokenController : ApiController
{
    private readonly IServiceTokenService _service;

    public AdminServiceTokenController(IServiceTokenService service) => _service = service;

    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Mint([FromBody] MintServiceTokenRequest request)
    {
        try
        {
            var actingUserId = GetUserId();
            var response = await _service.MintAsync(request, actingUserId);
            return Created201(response);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { success = false, message = ex.Message });
        }
    }

    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> List()
    {
        var items = await _service.ListAsync();
        return OkData(items);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id)
    {
        var item = await _service.GetByIdAsync(id);
        if (item is null)
            return NotFound(new { success = false, message = "Service token not found." });
        return OkData(item);
    }

    [HttpPut("{id:guid}/scopes")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateScopes(Guid id, [FromBody] UpdateScopesRequest request)
    {
        try
        {
            await _service.UpdateScopesAsync(id, request.Scopes ?? [], GetUserId());
            return OkMessage("Scopes updated.");
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { success = false, message = "Service token not found." });
        }
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Revoke(Guid id)
    {
        try
        {
            await _service.RevokeAsync(id, GetUserId());
            return OkMessage("Service token revoked.");
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { success = false, message = "Service token not found." });
        }
    }

    [HttpPost("{id:guid}/flush-cache")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> FlushCache(Guid id)
    {
        await _service.FlushCacheAsync(id);
        return OkMessage("Validator cache flushed for this token.");
    }

    /// <summary>
    /// Returns any existing tokens whose stored scopes contain values not in the
    /// canonical ServiceTokenScopes list. Useful for auditing tokens minted before
    /// the scope allow-list was enforced (Phase 1/2 legacy tokens).
    /// </summary>
    [HttpGet("scope-audit")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> ScopeAudit()
    {
        var findings = await _service.AuditUnknownScopesAsync();
        return OkData(findings);
    }
}
