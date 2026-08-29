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
        // If the response has already begun (e.g. a file/stream download that faulted mid-copy),
        // setting StatusCode/ContentType throws a second exception that escapes to Kestrel and
        // aborts the connection. Log and bail — we can't write an error body over a committed response.
        if (ctx.Response.HasStarted)
        {
            _logger.LogError(ex, "Unhandled exception after the response has started; cannot write an error response");
            return;
        }

        var (status, problem) = ex switch
        {
            // The per-field errors go in the standard problem+json "errors" extension, which is where
            // the client already looks for them. Serialising the dictionary into Detail meant users
            // were shown raw C# — literally {"Input.PolicyNumber":["'Policy Number' must not be
            // empty."]} — while the client's own field-error reader always came back empty.
            ValidationException v => ((int)HttpStatusCode.BadRequest, BuildValidationProblem(v)),
            // Never echo ex.Message here — it embeds the record key/GUID. Show only the entity type
            // (the full message with the key is still captured in the server log below).
            NotFoundException nf => ((int)HttpStatusCode.NotFound, new ProblemDetails
            {
                Title = "Resource not found",
                Status = (int)HttpStatusCode.NotFound,
                Detail = $"The requested {nf.Entity.ToLowerInvariant()} was not found."
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

    /// <summary>
    /// A human sentence in Detail, and the machine-readable field errors in the standard "errors"
    /// extension so the form can highlight the offending inputs.
    /// </summary>
    private static ProblemDetails BuildValidationProblem(ValidationException v)
    {
        var byField = v.Errors;

        var problem = new ProblemDetails
        {
            Title = "Validation failed",
            Status = (int)HttpStatusCode.BadRequest,
            // One clear sentence when there is a single problem — which is the common case — rather
            // than making the reader parse a structure. Falls back to a count for the rest.
            Detail = byField.Count == 1 && byField.First().Value.Length == 1
                ? byField.First().Value[0]
                : "Some of the details you entered need attention.",
        };
        problem.Extensions["errors"] = byField;
        return problem;
    }
}
