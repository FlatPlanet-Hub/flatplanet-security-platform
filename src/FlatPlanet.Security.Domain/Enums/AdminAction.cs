namespace FlatPlanet.Security.Domain.Enums;

public static class AdminAction
{
    public const string UserCreate     = "user.create";
    public const string UserUpdate     = "user.update";
    public const string UserSuspend    = "user.suspend";
    public const string UserDeactivate = "user.deactivate";

    public const string RoleGrant  = "role.grant";
    public const string RoleRevoke = "role.revoke";

    public const string AppRegister   = "app.register";
    public const string AppUpdate     = "app.update";
    public const string AppDeactivate = "app.deactivate";
}
