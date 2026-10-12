using Kkts.DataAccess;

namespace CustomReadAdapter;

// Extension example: provide your database session implementation to this factory.
// Execute complete authorized plans in the provider using validated host output/continuation bindings.
public sealed class CustomAdapter<T>(Func<CancellationToken, ValueTask<IEntityReadSession<T>>> open)
    : IEntityReadAdapter<T>
    where T : class
{
    public static CustomAdapter<T> ForSeek(
        Func<CancellationToken, ValueTask<IEntitySeekReadSession<T>>> open
    ) => new(async cancellation => await open(cancellation));

    public static CustomAdapter<T> ForProjectedSeek(
        Func<CancellationToken, ValueTask<IEntityProjectedSeekReadSession<T>>> open
    ) => new(async cancellation => await open(cancellation));

    public ValueTask<IEntityReadSession<T>> OpenAsync(CancellationToken cancellation = default) =>
        open(cancellation);
}
