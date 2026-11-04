using System.Net;
using System.Text.Json;
using LazyBean.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using ValidationException = LazyBean.Application.Common.Exceptions.ValidationException;

namespace LazyBean.Api.Middleware;

// Single place that turns domain/application exceptions into consistent ProblemDetails
// JSON responses, so controllers stay free of try/catch noise.
public class ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            await HandleAsync(context, ex);
        }
    }

    private async Task HandleAsync(HttpContext context, Exception exception)
    {
        var (statusCode, title, extensions) = exception switch
        {
            ValidationException validationException => (
                HttpStatusCode.BadRequest,
                "Validation failed",
                new Dictionary<string, object?> { ["errors"] = validationException.Errors }),

            NotFoundException => (HttpStatusCode.NotFound, exception.Message, null),

            InvalidCredentialsException => (HttpStatusCode.Unauthorized, exception.Message, null),

            RunAlreadyInProgressException => (HttpStatusCode.Conflict, exception.Message, null),

            DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } } =>
                (HttpStatusCode.Conflict, "A record with the same key already exists.", null),

            _ => (HttpStatusCode.InternalServerError, "An unexpected error occurred.", null)
        };

        if (statusCode == HttpStatusCode.InternalServerError)
        {
            logger.LogError(exception, "Unhandled exception processing {Method} {Path}",
                context.Request.Method, context.Request.Path);
        }

        var correlationId = context.Items.TryGetValue(CorrelationIdMiddleware.HeaderName, out var id)
            ? id?.ToString()
            : context.TraceIdentifier;

        var problem = new Dictionary<string, object?>
        {
            ["status"] = (int)statusCode,
            ["title"] = title,
            ["traceId"] = context.TraceIdentifier,
            ["correlationId"] = correlationId
        };

        if (extensions is not null)
        {
            foreach (var (key, value) in extensions)
            {
                problem[key] = value;
            }
        }

        context.Response.ContentType = "application/problem+json";
        context.Response.StatusCode = (int)statusCode;
        await context.Response.WriteAsync(JsonSerializer.Serialize(problem));
    }
}
