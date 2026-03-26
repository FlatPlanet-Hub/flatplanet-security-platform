using FlatPlanet.Security.Application.DTOs.Admin;
using FlatPlanet.Security.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FlatPlanet.Security.API.Controllers;

[ApiController]
[Route("api/v1/companies")]
[Authorize(Policy = "PlatformOwner")]
public class CompanyController : ApiController
{
    private readonly ICompanyService _companies;

    public CompanyController(ICompanyService companies) => _companies = companies;

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var result = await _companies.GetAllAsync();
        return OkData(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var result = await _companies.GetByIdAsync(id);
        return OkData(result);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateCompanyRequest request)
    {
        var result = await _companies.CreateAsync(request);
        return CreatedData(nameof(GetById), new { id = result.Id }, result);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateCompanyRequest request)
    {
        var result = await _companies.UpdateAsync(id, request);
        return OkData(result);
    }

    [HttpPut("{id:guid}/status")]
    public async Task<IActionResult> UpdateStatus(Guid id, [FromBody] UpdateCompanyStatusRequest request)
    {
        await _companies.UpdateStatusAsync(id, request.Status);
        return OkMessage("Status updated.");
    }
}
