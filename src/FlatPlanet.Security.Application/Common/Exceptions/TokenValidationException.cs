namespace FlatPlanet.Security.Application.Common.Exceptions;

public class TokenValidationException : ApplicationException
{
    public TokenValidationException(string message) : base(message) { }
    public TokenValidationException(string message, Exception inner) : base(message, inner) { }
}
