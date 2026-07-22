using CRM.Application.Common.Exceptions;
using CRM.Domain.Common;
using Microsoft.AspNetCore.Mvc;
using System.Net;
using System.Text.Json;

namespace CRM.Api.Middleware;

public class ExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionMiddleware> _logger;

    public ExceptionMiddleware(RequestDelegate next, ILogger<ExceptionMiddleware> logger)
    {
        _next = Guard.AgainstNull(next);
        _logger = Guard.AgainstNull(logger);
    }

    public async Task Invoke(HttpContext ctx)
    {
        Guard.AgainstNull(ctx);
        try
        {
            await _next(ctx);
        }
        catch (Exception ex)
        {
            await HandleAsync(ctx, ex);
        }
    }

    private async Task HandleAsync(HttpContext ctx, Exception ex)
    {
        var (status, problem) = ex switch
        {
            ValidationException v => ((int)HttpStatusCode.BadRequest, new ProblemDetails
            {
                Title = "Validation failed",
                Status = (int)HttpStatusCode.BadRequest,
                Detail = JsonSerializer.Serialize(v.Errors)
            }),
            NotFoundException => ((int)HttpStatusCode.NotFound, new ProblemDetails
            {
                Title = "Resource not found",
                Status = (int)HttpStatusCode.NotFound,
                Detail = ex.Message
            }),
            ForbiddenAccessException => ((int)HttpStatusCode.Forbidden, new ProblemDetails
            {
                Title = "Forbidden",
                Status = (int)HttpStatusCode.Forbidden,
                Detail = ex.Message
            }),
            ConflictException => ((int)HttpStatusCode.Conflict, new ProblemDetails
            {
                Title = "Conflict",
                Status = (int)HttpStatusCode.Conflict,
                Detail = ex.Message
            }),
            TooManyRequestsException tmr => (StatusCodes.Status429TooManyRequests, new ProblemDetails
            {
                Title = "Too many requests",
                Status = StatusCodes.Status429TooManyRequests,
                Detail = tmr.Message
            }),
            _ => ((int)HttpStatusCode.InternalServerError, new ProblemDetails
            {
                Title = "Server error",
                Status = (int)HttpStatusCode.InternalServerError,
                Detail = "An unexpected error occurred."
            })
        };

        if (status >= 500) _logger.LogError(ex, "Unhandled exception");
        else _logger.LogWarning(ex, "Handled exception");

        if (ex is TooManyRequestsException tmr2 && tmr2.RetryAfter is { } ra)
            ctx.Response.Headers.RetryAfter = ((int)Math.Ceiling(ra.TotalSeconds)).ToString();

        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = "application/problem+json";
        await ctx.Response.WriteAsync(JsonSerializer.Serialize(problem));
    }
}
