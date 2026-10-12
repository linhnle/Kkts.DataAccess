using System.Net;
using System.Text;
using System.Text.Json;
using Kkts.DataAccess.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Kkts.DataAccess.Tests;

public class StructuredHttpErrorTests
{
    [Theory]
    [InlineData(DataAccessErrorCode.InvalidRequest, 400, "invalid_request")]
    [InlineData(DataAccessErrorCode.AccessDenied, 403, "access_denied")]
    [InlineData(DataAccessErrorCode.PolicyMissing, 503, "policy_missing")]
    [InlineData(DataAccessErrorCode.SchemaIncompatible, 409, "schema_incompatible")]
    [InlineData(DataAccessErrorCode.RevisionConflict, 409, "revision_conflict")]
    [InlineData(DataAccessErrorCode.ProviderUnsupported, 422, "provider_unsupported")]
    [InlineData(DataAccessErrorCode.InvalidContinuation, 400, "invalid_continuation")]
    [InlineData(DataAccessErrorCode.ExportPolicyChanged, 403, "export_policy_changed")]
    [InlineData(DataAccessErrorCode.ExportDurationExceeded, 504, "export_duration_exceeded")]
    public async Task Known_errors_have_safe_problem_details(
        DataAccessErrorCode code,
        int status,
        string serializedCode
    )
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        var middleware = new DataAccessProblemDetailsMiddleware(
            _ =>
                throw new DataAccessException(
                    code,
                    "SQL policy token context record sentinel",
                    [
                        new(
                            DataAccessErrorCode.InvalidLiteral,
                            "/filter/literal",
                            "literal sentinel"
                        ),
                    ],
                    new InvalidOperationException("inner sentinel")
                ),
            NullLogger<DataAccessProblemDetailsMiddleware>.Instance
        );
        await middleware.InvokeAsync(context);
        Assert.Equal(status, context.Response.StatusCode);
        Assert.Equal("application/problem+json", context.Response.ContentType);
        context.Response.Body.Position = 0;
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();
        Assert.DoesNotContain("sentinel", body);
        using var json = JsonDocument.Parse(body);
        Assert.Equal(status, json.RootElement.GetProperty("status").GetInt32());
        Assert.Equal(serializedCode, json.RootElement.GetProperty("code").GetString());
        Assert.Equal(32, json.RootElement.GetProperty("traceId").GetString()!.Length);
        if (code == DataAccessErrorCode.InvalidRequest)
        {
            var diagnostic = json.RootElement.GetProperty("diagnostics")[0];
            Assert.Equal("invalid_literal", diagnostic.GetProperty("code").GetString());
            Assert.Equal("/filter/literal", diagnostic.GetProperty("path").GetString());
        }
        else
            Assert.False(json.RootElement.TryGetProperty("diagnostics", out _));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Unexpected_infrastructure_and_configuration_errors_remain_generic(
        bool configuration
    )
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        Exception cause = configuration
            ? new ArgumentException("configuration sentinel")
            : new InvalidOperationException("database SQL credentials sentinel");
        var middleware = new DataAccessProblemDetailsMiddleware(
            _ => throw cause,
            NullLogger<DataAccessProblemDetailsMiddleware>.Instance
        );
        await middleware.InvokeAsync(context);
        Assert.Equal(500, context.Response.StatusCode);
        context.Response.Body.Position = 0;
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();
        Assert.Contains("internal_error", body);
        Assert.DoesNotContain("sentinel", body);
        Assert.DoesNotContain("diagnostics", body);
    }

    [Fact]
    public async Task Caller_cancellation_is_not_translated_into_a_problem_body()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var context = new DefaultHttpContext { RequestAborted = cancellation.Token };
        context.Response.Body = new MemoryStream();
        var cause = new OperationCanceledException(cancellation.Token);
        var middleware = new DataAccessProblemDetailsMiddleware(
            _ => throw cause,
            NullLogger<DataAccessProblemDetailsMiddleware>.Instance
        );
        Assert.Same(
            cause,
            await Assert.ThrowsAsync<OperationCanceledException>(() =>
                middleware.InvokeAsync(context)
            )
        );
        Assert.Equal(0, context.Response.Body.Length);
    }

    [Theory]
    [InlineData("""{"unknown":"raw sentinel"}""")]
    [InlineData("""{"filter":"raw sentinel"}""")]
    [InlineData("""{"filter":raw sentinel}""")]
    public async Task Malformed_transport_is_normalized_without_raw_JSON(string body)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Services.AddDataAccessProblemDetails();
        await using var app = builder.Build();
        app.UseDataAccessProblemDetails();
        app.MapPost("/count", (CountReadRequest request) => Results.Ok(request));
        await app.StartAsync();
        using var client = new HttpClient();
        try
        {
            using var response = await client.PostAsync(
                app.Urls.Single() + "/count",
                new StringContent(body, Encoding.UTF8, "application/json")
            );
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal(
                "application/problem+json",
                response.Content.Headers.ContentType!.MediaType
            );
            var result = await response.Content.ReadAsStringAsync();
            Assert.Contains("invalid_request", result);
            Assert.DoesNotContain("sentinel", result);
        }
        finally
        {
            await app.StopAsync();
        }
    }
}
