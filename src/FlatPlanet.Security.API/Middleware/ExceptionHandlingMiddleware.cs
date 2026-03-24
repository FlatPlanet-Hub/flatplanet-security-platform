using System.Net;
using System.Text.Json;
using FlatPlanet.Security.Application.Common.Exceptions;

namespace FlatPlanet.Security.API.Middleware;

public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception for {Method} {Path}", context.Request.Method, context.Request.Path);
            await WriteErrorResponseAsync(context, ex);
        }
    }

    private static async Task WriteErrorResponseAsync(HttpContext context, Exception ex)
    {
        var (statusCode, message) = ex switch
        {
            TooManyRequestsException e => ((HttpStatusCode)429, e.Message),
            AccountLockedException e => ((HttpStatusCode)423, e.Message),
            ForbiddenException e => (HttpStatusCode.Forbidden, e.Message),
            UnauthorizedAccessException e => (HttpStatusCode.Unauthorized, e.Message),
            ArgumentException e => (HttpStatusCode.BadRequest, e.Message),
            KeyNotFoundException => (HttpStatusCode.NotFound, "Resource not found."),
            InvalidOperationException e => (HttpStatusCode.Conflict, e.Message),
            _ => (HttpStatusCode.InternalServerError, "An unexpected error occurred.")
        };

        context.Response.StatusCode = (int)statusCode;
        context.Response.ContentType = "application/json";

        var body = JsonSerializer.Serialize(new
        {
            success = false,
            message
        });

        await context.Response.WriteAsync(body);
    }
}
