using System.Net;
using FlatPlanet.Security.Application.Common.Exceptions;
using Npgsql;

namespace FlatPlanet.Security.API.Middleware;

/// <summary>
/// Maps exceptions to HTTP status codes and messages.
/// Add new exception types here — ExceptionHandlingMiddleware never needs to change.
/// </summary>
internal static class ExceptionResponseMapper
{
    internal static (HttpStatusCode status, string message) Map(Exception ex) => ex switch
    {
        TooManyRequestsException e                  => ((HttpStatusCode)429, e.Message),
        AccountLockedException e                    => ((HttpStatusCode)423, e.Message),
        ServiceUnavailableException e               => ((HttpStatusCode)503, e.Message),
        ForbiddenException e                        => (HttpStatusCode.Forbidden, e.Message),
        UnauthorizedAccessException e               => (HttpStatusCode.Unauthorized, e.Message),
        ArgumentException e                         => (HttpStatusCode.BadRequest, e.Message),
        KeyNotFoundException                        => (HttpStatusCode.NotFound, "Resource not found."),
        InvalidOperationException e                 => (HttpStatusCode.Conflict, e.Message),
        PostgresException { SqlState: "23505" }     => (HttpStatusCode.Conflict, "A record with that value already exists."),
        NpgsqlException e when e.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase)
                                                    => ((HttpStatusCode)503, "Database is temporarily unavailable. Please retry."),
        OperationCanceledException                  => ((HttpStatusCode)503, "Request timed out. Please retry."),
        _                                           => (HttpStatusCode.InternalServerError, "An unexpected error occurred.")
    };
}
