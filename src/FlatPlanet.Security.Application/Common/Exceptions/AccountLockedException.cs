namespace FlatPlanet.Security.Application.Common.Exceptions;

public class AccountLockedException : Exception
{
    public AccountLockedException(string message) : base(message) { }
}
