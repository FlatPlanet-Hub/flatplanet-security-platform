using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

namespace FlatPlanet.Security.API.Authorization;

/// <summary>
/// Asserts that every action method gated by AdminAccess also carries [RequireScope].
///
/// Background: AdminAccess now admits any service_token role so that narrow-scoped
/// per-service tokens can reach admin controllers. The per-endpoint scope check
/// ([RequireScope]) is the real gate for service tokens. If an action has AdminAccess
/// but no RequireScope, any service token — regardless of its scopes — can reach it.
///
/// Call Verify() once at startup (after builder.Build(), before app.Run()).
/// It throws InvalidOperationException listing all violations if any are found,
/// which aborts boot and prevents a misconfigured app from serving traffic.
/// </summary>
public static class AdminAccessScopeInvariant
{
    public static void Verify(Assembly controllerAssembly) =>
        Verify(controllerAssembly.GetTypes()
            .Where(t => !t.IsAbstract && typeof(ControllerBase).IsAssignableFrom(t)));

    public static void Verify(IEnumerable<Type> controllerTypes)
    {
        var violations = new List<string>();

        controllerTypes = controllerTypes
            .Where(t => !t.IsAbstract && typeof(ControllerBase).IsAssignableFrom(t));

        foreach (var type in controllerTypes)
        {
            var classHasAdminAccess = HasAdminAccessPolicy(type.GetCustomAttributes<AuthorizeAttribute>());

            var actionMethods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => m.GetCustomAttributes<HttpMethodAttribute>().Any() ||
                            m.GetCustomAttributes<RouteAttribute>().Any());

            foreach (var method in actionMethods)
            {
                // [AllowAnonymous] overrides every [Authorize] on the action — the
                // scope gate is irrelevant because the action never runs auth.
                if (method.GetCustomAttribute<AllowAnonymousAttribute>() is not null)
                    continue;

                var methodHasAdminAccess = HasAdminAccessPolicy(method.GetCustomAttributes<AuthorizeAttribute>());
                var actionIsAdminAccess = classHasAdminAccess || methodHasAdminAccess;

                if (!actionIsAdminAccess)
                    continue;

                var hasRequireScope = method.GetCustomAttribute<RequireScopeAttribute>() is not null ||
                                      type.GetCustomAttribute<RequireScopeAttribute>() is not null;

                if (!hasRequireScope)
                    violations.Add($"{type.Name}.{method.Name}");
            }
        }

        if (violations.Count > 0)
        {
            throw new InvalidOperationException(
                $"AdminAccess invariant violated — the following actions are gated by AdminAccess " +
                $"but have no [RequireScope] attribute. Any service_token can reach them unchecked. " +
                $"Add [RequireScope(\"...\")]: {string.Join(", ", violations)}");
        }
    }

    private static bool HasAdminAccessPolicy(IEnumerable<AuthorizeAttribute> attrs) =>
        attrs.Any(a => string.Equals(a.Policy, "AdminAccess", StringComparison.Ordinal));
}
