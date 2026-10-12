namespace Kkts.DataAccess;

public enum ExportPolicyConsistency
{
    CapturedPolicy,
    AbortOnPolicyChange,
}

public enum ExportDataConsistency
{
    BestEffort,
    Snapshot,
}

public sealed class ExportReadConfiguration
{
    public ExportPolicyConsistency PolicyConsistency { get; }
    public ExportDataConsistency DataConsistency { get; }
    public long? MaxRows { get; }
    public TimeSpan? MaxDuration { get; }
    public TimeProvider TimeProvider { get; }

    public ExportReadConfiguration(
        ExportPolicyConsistency policyConsistency = ExportPolicyConsistency.CapturedPolicy,
        ExportDataConsistency dataConsistency = ExportDataConsistency.BestEffort,
        long? maxRows = null,
        TimeSpan? maxDuration = null,
        TimeProvider? timeProvider = null
    )
    {
        if (!Enum.IsDefined(policyConsistency))
            throw new ArgumentOutOfRangeException(nameof(policyConsistency));
        if (!Enum.IsDefined(dataConsistency))
            throw new ArgumentOutOfRangeException(nameof(dataConsistency));
        if (maxRows is <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxRows));
        if (
            maxDuration is { } duration
            && (duration <= TimeSpan.Zero || duration.TotalMilliseconds > uint.MaxValue - 1)
        )
            throw new ArgumentOutOfRangeException(nameof(maxDuration));
        PolicyConsistency = policyConsistency;
        DataConsistency = dataConsistency;
        MaxRows = maxRows;
        MaxDuration = maxDuration;
        TimeProvider = timeProvider ?? TimeProvider.System;
    }
}

public sealed class ExportPolicyChangedException()
    : Exception("Export stopped because the active policy changed.");

public sealed class ExportDurationExceededException()
    : TimeoutException("Export exceeded its maximum elapsed duration.");
