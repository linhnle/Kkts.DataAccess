namespace Kkts.DataAccess.Testing;

public sealed class OwnedFixtureResources : IAsyncDisposable
{
    readonly List<IAsyncDisposable> resources = [];
    bool disposed;

    public T Own<T>(T resource)
        where T : IAsyncDisposable
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(resource);
        if (resources.Any(owned => ReferenceEquals(owned, resource)))
            throw new ArgumentException("A resource is already owned.", nameof(resource));
        resources.Add(resource);
        return resource;
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed)
            return;
        disposed = true;
        var errors = new List<Exception>();
        for (var index = resources.Count - 1; index >= 0; index--)
        {
            try
            {
                await resources[index].DisposeAsync();
            }
            catch (Exception error)
            {
                errors.Add(error);
            }
        }
        resources.Clear();
        if (errors.Count != 0)
            throw new AggregateException("Owned fixture teardown failed.", errors);
    }
}
