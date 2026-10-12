using Kkts.DataAccess;

namespace CustomReadAdapter;

// Extension example: provide your database session implementation to this factory.
// The session must execute the supplied authorized plans in its database provider.
public sealed class CustomAdapter<T>(Func<CancellationToken, ValueTask<IEntityReadSession<T>>> open)
    : IEntityReadAdapter<T>
    where T : class
{
    public ValueTask<IEntityReadSession<T>> OpenAsync(CancellationToken cancellation = default) =>
        open(cancellation);
}
