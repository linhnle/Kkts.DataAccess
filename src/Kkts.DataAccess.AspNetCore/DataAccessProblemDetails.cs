using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Kkts.DataAccess.AspNetCore;

public static class DataAccessProblemDetailsExtensions
{
    public static IServiceCollection AddDataAccessProblemDetails(this IServiceCollection services)
    {
        services.AddProblemDetails();
        services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = true);
        return services;
    }

    public static IApplicationBuilder UseDataAccessProblemDetails(this IApplicationBuilder app) =>
        app.UseMiddleware<DataAccessProblemDetailsMiddleware>();
}

public sealed class DataAccessProblemDetailsMiddleware(
    RequestDelegate next,
    ILogger<DataAccessProblemDetailsMiddleware> logger
)
{
    public async Task InvokeAsync(HttpContext http)
    {
        try
        {
            await next(http);
        }
        catch (OperationCanceledException) when (http.RequestAborted.IsCancellationRequested)
        {
            http.Abort();
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Data access request failed: {Failure}",
                exception.GetType().Name
            );
            if (http.Response.HasStarted)
            {
                http.Abort();
                return;
            }

            var (status, title, code) = exception switch
            {
                DataAccessException error => error.Category switch
                {
                    DataAccessErrorCategory.InvalidRequest => (400, "Invalid request", error.Code),
                    DataAccessErrorCategory.AccessDenied => (403, "Access denied", error.Code),
                    DataAccessErrorCategory.PolicyMissing => (
                        503,
                        "Policy unavailable",
                        error.Code
                    ),
                    DataAccessErrorCategory.SchemaIncompatible => (
                        409,
                        "Schema conflict",
                        error.Code
                    ),
                    DataAccessErrorCategory.RevisionConflict => (
                        409,
                        "Revision conflict",
                        error.Code
                    ),
                    DataAccessErrorCategory.ProviderUnsupported => (
                        422,
                        "Unsupported operation",
                        error.Code
                    ),
                    DataAccessErrorCategory.InvalidContinuation => (
                        400,
                        "Invalid continuation",
                        error.Code
                    ),
                    DataAccessErrorCategory.ExportPolicyChanged => (
                        403,
                        "Export policy changed",
                        error.Code
                    ),
                    DataAccessErrorCategory.ExportDurationExceeded => (
                        504,
                        "Export duration exceeded",
                        error.Code
                    ),
                    _ => (500, "Internal server error", (DataAccessErrorCode?)null),
                },
                JsonException or BadHttpRequestException or AntiforgeryValidationException => (
                    400,
                    "Invalid request",
                    (DataAccessErrorCode?)DataAccessErrorCode.InvalidRequest
                ),
                _ => (500, "Internal server error", (DataAccessErrorCode?)null),
            };
            var problem = new ProblemDetails
            {
                Status = status,
                Title = title,
                Type = "about:blank",
            };
            problem.Extensions["code"] = code is { } known
                ? JsonSerializer.SerializeToElement(known)
                : JsonSerializer.SerializeToElement("internal_error");
            problem.Extensions["traceId"] =
                Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N");
            if (
                exception is DataAccessException
                {
                    Category: DataAccessErrorCategory.InvalidRequest
                } invalid
            )
                problem.Extensions["diagnostics"] = invalid
                    .Diagnostics.Select(diagnostic => new Diagnostic(
                        diagnostic.Code,
                        diagnostic.Path,
                        "The input is invalid."
                    ))
                    .ToArray();

            http.Response.Clear();
            http.Response.StatusCode = status;
            await http.Response.WriteAsJsonAsync(
                problem,
                options: (JsonSerializerOptions?)null,
                contentType: "application/problem+json",
                cancellationToken: http.RequestAborted
            );
        }
    }
}
