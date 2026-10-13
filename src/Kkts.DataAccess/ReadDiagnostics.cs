using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Runtime.CompilerServices;

namespace Kkts.DataAccess;

public static class ReadDiagnostics
{
    public const string Name = "Kkts.DataAccess";
    public const string Version = "1.0.0";
    internal static readonly ActivitySource Source = new(Name, Version);
    internal static readonly Meter Meter = new(Name, Version);
    static readonly Histogram<double> OperationDuration = Meter.CreateHistogram<double>(
        "kda.operation.duration",
        "s"
    );
    static readonly Histogram<double> StageDuration = Meter.CreateHistogram<double>(
        "kda.stage.duration",
        "s"
    );
    static readonly Counter<long> Operations = Meter.CreateCounter<long>(
        "kda.operations",
        "{operation}"
    );
    static readonly Counter<long> Exports = Meter.CreateCounter<long>("kda.exports", "{export}");
    static readonly Counter<long> Rows = Meter.CreateCounter<long>("kda.rows.returned", "{row}");
    static readonly Counter<long> CacheLookups = Meter.CreateCounter<long>(
        "kda.cache.lookups",
        "{lookup}"
    );
    static readonly Counter<long> CacheBuilds = Meter.CreateCounter<long>(
        "kda.cache.builds",
        "{build}"
    );
    static readonly Counter<long> CacheEvictions = Meter.CreateCounter<long>(
        "kda.cache.evictions",
        "{eviction}"
    );
    static readonly AsyncLocal<Observation?> Current = new();

    internal static string Provider(string? provider) =>
        provider switch
        {
            "sqlite" or "Microsoft.EntityFrameworkCore.Sqlite" => "sqlite",
            "sqlserver" or "Microsoft.EntityFrameworkCore.SqlServer" => "sqlserver",
            "mysql" or "MySql.EntityFrameworkCore" => "mysql",
            _ => "custom",
        };

    internal static string Code(Exception? error) =>
        error switch
        {
            null => "none",
            OperationCanceledException => "canceled",
            DataAccessException known => known.Code switch
            {
                DataAccessErrorCode.InvalidRequest => "invalid_request",
                DataAccessErrorCode.AccessDenied => "access_denied",
                DataAccessErrorCode.PolicyMissing => "policy_missing",
                DataAccessErrorCode.SchemaIncompatible => "schema_incompatible",
                DataAccessErrorCode.RevisionConflict => "revision_conflict",
                DataAccessErrorCode.ProviderUnsupported => "provider_unsupported",
                DataAccessErrorCode.InvalidContinuation => "invalid_continuation",
                DataAccessErrorCode.ExportPolicyChanged => "export_policy_changed",
                DataAccessErrorCode.ExportDurationExceeded => "export_duration_exceeded",
                DataAccessErrorCode.InvalidCondition => "invalid_condition",
                DataAccessErrorCode.InvalidLiteral => "invalid_literal",
                DataAccessErrorCode.UnknownField => "unknown_field",
                DataAccessErrorCode.InvalidOperator => "invalid_operator",
                DataAccessErrorCode.InvalidContextReference => "invalid_context_reference",
                DataAccessErrorCode.InvalidSubject => "invalid_subject",
                DataAccessErrorCode.InvalidEntry => "invalid_entry",
                DataAccessErrorCode.ComplexityExceeded => "complexity_exceeded",
                DataAccessErrorCode.ConditionalFieldsUnsupported =>
                    "conditional_fields_unsupported",
                DataAccessErrorCode.UnknownProperty => "unknown-property",
                DataAccessErrorCode.OperatorNotApplicable => "operator-not-applicable",
                DataAccessErrorCode.IncompatibleOperand => "incompatible-operand",
                _ => "unknown",
            },
            _ => "infrastructure",
        };

    static string Outcome(Exception error) =>
        error switch
        {
            ExportPolicyChangedException => "policy_changed",
            ExportDurationExceededException => "duration_exceeded",
            OperationCanceledException => "canceled",
            _ => "failed",
        };

    sealed class Observation
    {
        readonly long started = Stopwatch.GetTimestamp();
        internal readonly string Operation;
        internal readonly string Provider;
        internal readonly Activity? Activity;
        internal string Completion = "exhausted";

        internal Observation(string operation, string provider)
        {
            Operation = operation;
            Provider = provider;
            var previous = System.Diagnostics.Activity.Current;
            Activity = Source.StartActivity("kda.read." + operation);
            System.Diagnostics.Activity.Current = previous;
            Activity?.SetTag("operation", operation);
            Activity?.SetTag("provider", provider);
        }

        internal TagList Tags(string outcome, Exception? error = null) =>
            new()
            {
                { "operation", Operation },
                { "provider", Provider },
                { "outcome", outcome },
                { "code", Code(error) },
            };

        internal void Returned(long count)
        {
            if (Rows.Enabled)
                Rows.Add(count, Tags("success"));
        }

        internal void Finish(string outcome, Exception? error)
        {
            Activity?.SetTag("outcome", outcome);
            Activity?.SetTag("code", Code(error));
            if (error != null)
                Activity?.SetStatus(ActivityStatusCode.Error);
            Activity?.Dispose();
            if (Operations.Enabled || OperationDuration.Enabled || Exports.Enabled)
            {
                var tags = Tags(outcome, error);
                Operations.Add(1, tags);
                OperationDuration.Record(Stopwatch.GetElapsedTime(started).TotalSeconds, tags);
                if (Operation == "export")
                    Exports.Add(1, tags);
            }
        }
    }

    internal static async Task<TResult> Read<TResult>(
        string operation,
        string provider,
        Func<Task<TResult>> action,
        Func<TResult, int>? rowCount = null
    )
    {
        var observation = new Observation(operation, provider);
        var previous = Current.Value;
        Current.Value = observation;
        Exception? error = null;
        try
        {
            var result = await action();
            if (rowCount != null)
                observation.Returned(rowCount(result));
            return result;
        }
        catch (Exception ex)
        {
            error = ex;
            throw;
        }
        finally
        {
            Current.Value = previous;
            observation.Finish(error == null ? "success" : Outcome(error), error);
        }
    }

    internal static async IAsyncEnumerable<ExportBatch> Export(
        Func<CancellationToken, IAsyncEnumerable<ExportBatch>> action,
        Func<string> provider,
        [EnumeratorCancellation] CancellationToken cancellation
    )
    {
        var observation = new Observation("export", provider());
        IAsyncEnumerator<ExportBatch>? iterator = null;
        Exception? error = null;
        var outcome = "disposed_early";
        try
        {
            iterator = action(cancellation).GetAsyncEnumerator(cancellation);
            while (true)
            {
                bool advanced;
                var previous = Current.Value;
                Current.Value = observation;
                try
                {
                    advanced = await iterator.MoveNextAsync();
                }
                catch (Exception ex)
                {
                    error = ex;
                    outcome = Outcome(ex);
                    throw;
                }
                finally
                {
                    Current.Value = previous;
                }
                if (!advanced)
                {
                    outcome = observation.Completion;
                    break;
                }
                observation.Returned(iterator.Current.Records.Count);
                yield return iterator.Current;
            }
        }
        finally
        {
            try
            {
                if (iterator != null)
                    await iterator.DisposeAsync();
            }
            catch (Exception ex)
            {
                error = ex;
                outcome = Outcome(ex);
                throw;
            }
            finally
            {
                observation.Finish(outcome, error);
            }
        }
    }

    internal static void ExportCompleted(string outcome)
    {
        if (Current.Value is { } observation)
            observation.Completion = outcome;
    }

    internal static async Task<TResult> StageAsync<TResult>(
        string stage,
        Func<Task<TResult>> action
    )
    {
        using var observation = new StageObservation(stage);
        try
        {
            return await action();
        }
        catch (Exception ex)
        {
            observation.Error = ex;
            throw;
        }
    }

    internal static TResult Stage<TResult>(string stage, Func<TResult> action)
    {
        using var observation = new StageObservation(stage);
        try
        {
            return action();
        }
        catch (Exception ex)
        {
            observation.Error = ex;
            throw;
        }
    }

    sealed class StageObservation : IDisposable
    {
        readonly string stage;
        readonly Observation? parent = Current.Value;
        readonly long started = Stopwatch.GetTimestamp();
        readonly Activity? activity;
        internal Exception? Error;

        internal StageObservation(string stage)
        {
            this.stage = stage;
            activity = Source.StartActivity(
                stage switch
                {
                    "capture" => "kda.policy.capture",
                    "bind" => "kda.policy.bind",
                    "execute" => "kda.entity.execute",
                    _ => "kda.dto.map",
                },
                ActivityKind.Internal,
                parent?.Activity?.Context ?? Activity.Current?.Context ?? default
            );
            activity?.SetTag("stage", stage);
            activity?.SetTag("operation", parent?.Operation ?? "standalone");
            activity?.SetTag("provider", parent?.Provider ?? "custom");
        }

        public void Dispose()
        {
            var outcome = Error == null ? "success" : Outcome(Error);
            activity?.SetTag("outcome", outcome);
            activity?.SetTag("code", Code(Error));
            if (Error != null)
                activity?.SetStatus(ActivityStatusCode.Error);
            activity?.Dispose();
            if (StageDuration.Enabled)
            {
                var tags =
                    parent?.Tags(outcome, Error)
                    ?? new TagList
                    {
                        { "operation", "standalone" },
                        { "provider", "custom" },
                        { "outcome", outcome },
                        { "code", Code(Error) },
                    };
                tags.Add("stage", stage);
                StageDuration.Record(Stopwatch.GetElapsedTime(started).TotalSeconds, tags);
            }
        }
    }

    internal static void Cache(string kind, bool hit, bool built, bool evicted, Exception? error)
    {
        if (!CacheLookups.Enabled && !CacheBuilds.Enabled && !CacheEvictions.Enabled)
            return;
        var tags = new TagList { { "cache", kind }, { "outcome", hit ? "hit" : "miss" } };
        CacheLookups.Add(1, tags);
        if (built)
        {
            tags = new()
            {
                { "cache", kind },
                { "outcome", error == null ? "success" : "failed" },
                { "code", Code(error) },
            };
            CacheBuilds.Add(1, tags);
        }
        if (evicted)
            CacheEvictions.Add(1, new TagList { { "cache", kind }, { "outcome", "evicted" } });
    }
}
