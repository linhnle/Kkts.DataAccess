using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace Kkts.DataAccess.Tests;

[Trait("Provider", "MySql")]
public class MySqlFixtureLifecycleTests
{
    sealed class AsyncConnectionsOnly : DbConnectionInterceptor
    {
        public override InterceptionResult ConnectionOpening(
            DbConnection connection,
            ConnectionEventData eventData,
            InterceptionResult result
        ) =>
            throw new InvalidOperationException(
                "Fixture database connections must open asynchronously."
            );
    }

    sealed class Fixture : MySqlReadTests
    {
        protected override DbContextOptions<Database> Options =>
            new DbContextOptionsBuilder<Database>(base.Options)
                .AddInterceptors(new AsyncConnectionsOnly())
                .Options;
    }

    [Fact]
    public async Task Setup_and_cleanup_use_async_database_connections()
    {
        var fixture = new Fixture();
        try
        {
            await fixture.InitializeAsync();
        }
        finally
        {
            await fixture.DisposeAsync();
        }
    }
}
