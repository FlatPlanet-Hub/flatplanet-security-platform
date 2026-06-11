using System.Reflection;
using FlatPlanet.Security.API.Authorization;
using FlatPlanet.Security.Application.DTOs.ServiceTokens;
using FlatPlanet.Security.Application.Interfaces.Repositories;
using FlatPlanet.Security.Application.Services;
using FlatPlanet.Security.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Moq;

namespace FlatPlanet.Security.Tests;

public class AdminAccessScopeInvariantTests
{
    [Fact]
    public void Verify_PassesCleanAssembly_WhenAllAdminActionsHaveRequireScope()
    {
        // The real API assembly: every AdminAccess action should already have [RequireScope].
        // If this test fails, a controller was added without [RequireScope] — that's the bug.
        var assembly = typeof(FlatPlanet.Security.API.Controllers.AppController).Assembly;

        var ex = Record.Exception(() => AdminAccessScopeInvariant.Verify(assembly));

        Assert.Null(ex);
    }

    [Fact]
    public void Verify_Throws_WhenAdminAccessActionMissingRequireScope()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            AdminAccessScopeInvariant.Verify(new[] { typeof(FakeViolatingController) }));

        Assert.Contains("FakeViolatingController", ex.Message);
        Assert.Contains("BadAction", ex.Message);
    }

    [Fact]
    public void Verify_Passes_WhenAdminAccessActionHasRequireScope()
    {
        var ex = Record.Exception(() =>
            AdminAccessScopeInvariant.Verify(new[] { typeof(FakeCompliantController) }));

        Assert.Null(ex);
    }
}

// AdminAccess at class level, action has no [RequireScope] — invariant violation.
[ApiController]
[Route("fake/violating")]
[Authorize(Policy = "AdminAccess")]
internal sealed class FakeViolatingController : ControllerBase
{
    [HttpGet]
    public IActionResult BadAction() => Ok();
}

// AdminAccess at class level, action has [RequireScope] — compliant.
[ApiController]
[Route("fake/compliant")]
[Authorize(Policy = "AdminAccess")]
internal sealed class FakeCompliantController : ControllerBase
{
    [HttpGet]
    [RequireScope("users:read")]
    public IActionResult GoodAction() => Ok();
}
