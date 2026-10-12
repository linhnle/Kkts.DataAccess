using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace Kkts.DataAccess.Tests;

[Trait("Provider", "SqlServer")]
public class SqlServerReadTests : ReadTests
{
    readonly string databaseName = "kda_" + Guid.NewGuid().ToString("N");
    protected override string ProviderName => "Microsoft.EntityFrameworkCore.SqlServer";
    protected override DbContextOptions<Database> Options =>
        new DbContextOptionsBuilder<Database>()
            .UseSqlServer(Connection() + ";Database=" + databaseName)
            .Options;

    static string Connection() =>
        Environment.GetEnvironmentVariable("KDA_SQLSERVER_TEST")
        ?? throw new InvalidOperationException(
            "SQL Server acceptance not run: set KDA_SQLSERVER_TEST to an isolated test server connection."
        );

    static EntityReadPlan<CompilerTests.Employee> Plan() =>
        new(row => row.Id > 0, [new(typeof(CompilerTests.Employee).GetProperty("Id")!, false)]);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Snapshot_requires_fully_enabled_option_and_RCSI_is_insufficient(bool rcsi)
    {
        await using (var setup = new Database(Options))
        {
            await setup.Database.ExecuteSqlRawAsync(
                "ALTER DATABASE CURRENT SET ALLOW_SNAPSHOT_ISOLATION OFF"
            );
            if (rcsi)
                await setup.Database.ExecuteSqlRawAsync(
                    "ALTER DATABASE CURRENT SET READ_COMMITTED_SNAPSHOT ON WITH ROLLBACK IMMEDIATE"
                );
        }
        Database? context = null;
        var adapter = SqlServer.SqlServerEntityReads.Create<CompilerTests.Employee>(() =>
            context = new Database(Options)
        );
        await using (var session = await adapter.OpenAsync())
        {
            await Assert.ThrowsAsync<NotSupportedException>(() =>
                ((IEntitySnapshotReadSession<CompilerTests.Employee>)session).BeginSnapshotAsync(
                    Plan(),
                    CancellationToken.None
                )
            );
            using var command = context!.Database.GetDbConnection().CreateCommand();
            command.CommandText =
                "SELECT snapshot_isolation_state FROM sys.databases WHERE database_id=DB_ID()";
            Assert.Equal(0, Convert.ToInt32(await command.ExecuteScalarAsync()));
        }
        Assert.True(context!.Disposed);
    }

    [Fact]
    public async Task Transitional_snapshot_option_is_rejected_without_downgrading()
    {
        await using var holder = new Database(Options);
        await holder.Database.ExecuteSqlRawAsync(
            "ALTER DATABASE CURRENT SET ALLOW_SNAPSHOT_ISOLATION ON"
        );
        await using var held = await holder.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.Snapshot
        );
        await holder.Set<CompilerTests.Employee>().CountAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var disable = Task.Run(async () =>
        {
            await using var setup = new Database(Options);
            await setup.Database.ExecuteSqlRawAsync(
                "ALTER DATABASE CURRENT SET ALLOW_SNAPSHOT_ISOLATION OFF",
                timeout.Token
            );
        });
        var observed = false;
        try
        {
            await using var observer = new Database(Options);
            await observer.Database.OpenConnectionAsync(timeout.Token);
            using var command = observer.Database.GetDbConnection().CreateCommand();
            command.CommandText =
                "SELECT snapshot_isolation_state FROM sys.databases WHERE database_id=DB_ID()";
            for (var i = 0; i < 100; i++)
            {
                if (Convert.ToInt32(await command.ExecuteScalarAsync(timeout.Token)) == 2)
                {
                    observed = true;
                    break;
                }
                await Task.Delay(20, timeout.Token);
            }
            Assert.True(
                observed,
                "Expected IN_TRANSITION_TO_OFF while the snapshot remains active."
            );
            var adapter = SqlServer.SqlServerEntityReads.Create<CompilerTests.Employee>(() =>
                new Database(Options)
            );
            await using var session = await adapter.OpenAsync();
            await Assert.ThrowsAsync<NotSupportedException>(() =>
                ((IEntitySnapshotReadSession<CompilerTests.Employee>)session).BeginSnapshotAsync(
                    Plan(),
                    timeout.Token
                )
            );
        }
        finally
        {
            await held.RollbackAsync(CancellationToken.None);
            await disable;
        }
    }
}

[Trait("Provider", "MySql")]
public class MySqlReadTests : ReadTests
{
    readonly string databaseName = "kda_" + Guid.NewGuid().ToString("N");
    protected override string ProviderName => "MySql.EntityFrameworkCore";
    protected override DbContextOptions<Database> Options =>
        new DbContextOptionsBuilder<Database>()
            .UseMySQL(Connection() + ";Database=" + databaseName)
            .Options;

    static string Connection() =>
        Environment.GetEnvironmentVariable("KDA_MYSQL_TEST")
        ?? throw new InvalidOperationException(
            "MySQL acceptance not run: set KDA_MYSQL_TEST to an isolated test server connection."
        );

    [Fact]
    public async Task Snapshot_rejects_nontransactional_table_without_changing_engine()
    {
        await using (var setup = new Database(Options))
        {
            var table = setup.Model.FindEntityType(typeof(CompilerTests.Employee))!.GetTableName()!;
            var identifier = setup.GetService<ISqlGenerationHelper>().DelimitIdentifier(table);
            var statement = $"ALTER TABLE {identifier} ENGINE=MyISAM";
            await setup.Database.ExecuteSqlRawAsync(statement);
        }
        Database? context = null;
        var adapter = MySql.MySqlEntityReads.Create<CompilerTests.Employee>(() =>
            context = new Database(Options)
        );
        await using (var session = await adapter.OpenAsync())
        {
            var plan = new EntityReadPlan<CompilerTests.Employee>(
                row => row.Id > 0,
                [new(typeof(CompilerTests.Employee).GetProperty("Id")!, false)]
            );
            await Assert.ThrowsAsync<NotSupportedException>(() =>
                ((IEntitySnapshotReadSession<CompilerTests.Employee>)session).BeginSnapshotAsync(
                    plan,
                    CancellationToken.None
                )
            );
            using var command = context!.Database.GetDbConnection().CreateCommand();
            command.CommandText =
                "SELECT ENGINE FROM information_schema.TABLES WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME=@table";
            var parameter = command.CreateParameter();
            parameter.ParameterName = "@table";
            parameter.Value = context
                .Model.FindEntityType(typeof(CompilerTests.Employee))!
                .GetTableName()!;
            command.Parameters.Add(parameter);
            Assert.Equal("MyISAM", await command.ExecuteScalarAsync());
        }
        Assert.True(context!.Disposed);
    }

    sealed class ReadCommitted : Microsoft.EntityFrameworkCore.Diagnostics.DbConnectionInterceptor
    {
        public override async Task ConnectionOpenedAsync(
            System.Data.Common.DbConnection connection,
            Microsoft.EntityFrameworkCore.Diagnostics.ConnectionEndEventData eventData,
            CancellationToken cancellationToken = default
        )
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SET SESSION TRANSACTION ISOLATION LEVEL READ COMMITTED";
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    [Fact]
    public async Task Snapshot_sets_repeatable_read_independently_of_session_default()
    {
        var options = new DbContextOptionsBuilder<Database>(Options)
            .AddInterceptors(new ReadCommitted())
            .Options;
        Database? context = null;
        var adapter = MySql.MySqlEntityReads.Create<CompilerTests.Employee>(() =>
            context = new Database(options)
        );
        await using var session = await adapter.OpenAsync();
        var plan = new EntityReadPlan<CompilerTests.Employee>(
            row => row.Id > 0,
            [new(typeof(CompilerTests.Employee).GetProperty("Id")!, false)]
        );
        await ((IEntitySnapshotReadSession<CompilerTests.Employee>)session).BeginSnapshotAsync(
            plan,
            CancellationToken.None
        );
        Assert.Equal(
            System.Data.IsolationLevel.RepeatableRead,
            context!.Database.CurrentTransaction!.GetDbTransaction().IsolationLevel
        );
        using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction!.GetDbTransaction();
        command.CommandText = "SELECT @@transaction_isolation";
        Assert.Equal("READ-COMMITTED", await command.ExecuteScalarAsync());
        var before = await session.CountAsync(plan, CancellationToken.None);
        await using (var writer = new Database(Options))
        {
            writer.Remove(
                await writer.Set<CompilerTests.Employee>().SingleAsync(row => row.Id == 1)
            );
            await writer.SaveChangesAsync();
        }
        Assert.Equal(before, await session.CountAsync(plan, CancellationToken.None));
    }
}
