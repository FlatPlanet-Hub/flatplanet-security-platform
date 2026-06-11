using FlatPlanet.Security.API.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

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

    [Fact]
    public void Verify_IgnoresAllowAnonymousAction_EvenOnAdminAccessController()
    {
        var ex = Record.Exception(() =>
            AdminAccessScopeInvariant.Verify(new[] { typeof(FakeAllowAnonymousController) }));

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

// AdminAccess at class level, action has [AllowAnonymous] and no [RequireScope].
// [AllowAnonymous] overrides [Authorize] at runtime, so the action never needs a scope.
// The invariant must NOT flag this as a violation.
[ApiController]
[Route("fake/anonymous")]
[Authorize(Policy = "AdminAccess")]
internal sealed class FakeAllowAnonymousController : ControllerBase
{
    [HttpGet]
    [AllowAnonymous]
    public IActionResult PublicAction() => Ok();
}
