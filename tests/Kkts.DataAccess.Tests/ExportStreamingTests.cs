using System.Net;
using EmployeeApp.Middleware;
using Kkts.DataAccess;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Kkts.DataAccess.Tests;

public class ExportStreamingTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Lifecycle_errors_are_explicit_before_start_and_abort_partial_streams(
        bool duration,
        bool started
    )
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        await using var app = builder.Build();
        app.UseMiddleware<ExportLifecycleMiddleware>();
        var fail = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        app.MapPost(
            "/export",
            async (HttpContext http) =>
            {
                http.Response.ContentType = "application/x-ndjson";
                if (started)
                {
                    await http.Response.WriteAsync(
                        "{\"Name\":\"authorized prefix\"}\n",
                        http.RequestAborted
                    );
                    await http.Response.Body.FlushAsync(http.RequestAborted);
                    await fail.Task.WaitAsync(http.RequestAborted);
                }
                if (duration)
                    throw new ExportDurationExceededException();
                throw new ExportPolicyChangedException();
            }
        );
        await app.StartAsync();
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                app.Urls.Single() + "/export"
            );
            using var response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead
            );
            if (!started)
            {
                Assert.Equal(
                    duration ? HttpStatusCode.RequestTimeout : HttpStatusCode.Conflict,
                    response.StatusCode
                );
                Assert.Equal("application/json", response.Content.Headers.ContentType!.MediaType);
                var body = await response.Content.ReadAsStringAsync();
                Assert.Contains(
                    duration ? "maximum elapsed duration" : "active policy changed",
                    body
                );
                Assert.DoesNotContain("authorized prefix", body);
            }
            else
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.Equal(
                    "application/x-ndjson",
                    response.Content.Headers.ContentType!.MediaType
                );
                using var reader = new StreamReader(await response.Content.ReadAsStreamAsync());
                Assert.Equal("{\"Name\":\"authorized prefix\"}", await reader.ReadLineAsync());
                fail.SetResult();
                var error = await Record.ExceptionAsync(async () => await reader.ReadToEndAsync());
                Assert.True(
                    error is IOException or HttpRequestException,
                    "A partial NDJSON response must terminate abnormally, not append an error row or complete successfully."
                );
            }
        }
        finally
        {
            fail.TrySetResult();
            await app.StopAsync();
        }
    }
}
