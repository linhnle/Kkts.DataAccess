using Kkts.DataAccess;

namespace EmployeeApp.Middleware;

public sealed class ExportLifecycleMiddleware(
    RequestDelegate next,
    ILogger<ExportLifecycleMiddleware> logger
)
{
    public async Task InvokeAsync(HttpContext http)
    {
        try
        {
            await next(http);
        }
        catch (Exception ex)
            when (ex is ExportPolicyChangedException or ExportDurationExceededException)
        {
            logger.LogWarning(ex, "Export lifecycle terminated: {Failure}", ex.GetType().Name);
            if (http.Response.HasStarted)
            {
                http.Abort();
                return;
            }
            http.Response.StatusCode =
                ex is ExportPolicyChangedException
                    ? StatusCodes.Status409Conflict
                    : StatusCodes.Status408RequestTimeout;
            await http.Response.WriteAsJsonAsync(new { error = ex.Message }, http.RequestAborted);
        }
    }
}
