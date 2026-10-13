namespace Kkts.DataAccess;

public sealed record PolicyActivationStamp
{
    public long Version { get; }
    public long ActiveRevision { get; }

    public PolicyActivationStamp(long version, long activeRevision)
    {
        if (version <= 0)
            throw new ArgumentOutOfRangeException(nameof(version));
        if (activeRevision <= 0)
            throw new ArgumentOutOfRangeException(nameof(activeRevision));
        Version = version;
        ActiveRevision = activeRevision;
    }
}

public sealed record CapturedPolicyActivation
{
    public CapturedPolicy Policy { get; }
    public PolicyActivationStamp Stamp { get; }

    public CapturedPolicyActivation(CapturedPolicy policy, PolicyActivationStamp stamp)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(stamp);
        if (policy.Version != stamp.Version)
            throw new ArgumentException("Activation stamp does not match captured policy.");
        Policy = policy;
        Stamp = stamp;
    }
}

public interface IActivationAwarePolicySource : IActivePolicySource
{
    Task<CapturedPolicyActivation> CaptureActivationAsync(
        string scope,
        string resource,
        CancellationToken cancellation = default
    );

    Task<PolicyActivationStamp> ReadActivationAsync(
        string scope,
        string resource,
        CancellationToken cancellation = default
    );
}
