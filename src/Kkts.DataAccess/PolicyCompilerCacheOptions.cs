namespace Kkts.DataAccess;

public sealed record PolicyCompilerCacheOptions
{
    public int MaxEntries { get; }

    public PolicyCompilerCacheOptions(int MaxEntries = 128)
    {
        if (MaxEntries < 1)
            throw new ArgumentOutOfRangeException(nameof(MaxEntries));
        this.MaxEntries = MaxEntries;
    }
}
