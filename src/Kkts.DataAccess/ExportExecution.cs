namespace Kkts.DataAccess;

internal sealed class ExportExecution : IDisposable
{
    readonly ExportReadConfiguration configuration;
    readonly CancellationToken caller;
    readonly long started;
    readonly CancellationTokenSource? deadline;
    readonly CancellationTokenSource? linked;
    public CancellationToken Token { get; }

    public ExportExecution(ExportReadConfiguration configuration, CancellationToken caller)
    {
        this.configuration = configuration;
        this.caller = caller;
        started = configuration.TimeProvider.GetTimestamp();
        if (configuration.MaxDuration is { } duration)
        {
            // System timers truncate fractional milliseconds; never schedule before the bound.
            var due = TimeSpan.FromMilliseconds(Math.Ceiling(duration.TotalMilliseconds));
            deadline = new(due, configuration.TimeProvider);
            linked = CancellationTokenSource.CreateLinkedTokenSource(caller, deadline.Token);
        }
        Token = linked?.Token ?? caller;
    }

    public void Check()
    {
        caller.ThrowIfCancellationRequested();
        if (
            configuration.MaxDuration is { } duration
            && (
                deadline!.IsCancellationRequested
                || configuration.TimeProvider.GetElapsedTime(started) >= duration
            )
        )
            throw new ExportDurationExceededException();
    }

    public async Task<TResult> RunAsync<TResult>(Func<CancellationToken, Task<TResult>> operation)
    {
        Check();
        try
        {
            var result = await operation(Token);
            Check();
            return result;
        }
        catch (OperationCanceledException ex)
            when (ex.CancellationToken == Token
                && (caller.IsCancellationRequested || deadline?.IsCancellationRequested == true)
            )
        {
            Check();
            throw;
        }
    }

    public Task RunAsync(Func<CancellationToken, Task> operation) =>
        RunAsync(async token =>
        {
            await operation(token);
            return true;
        });

    public void Dispose()
    {
        linked?.Dispose();
        deadline?.Dispose();
    }
}
