using Kkts.DataAccess.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Kkts.DataAccess.Sqlite;

public static class SqliteEntityReads
{
    public static EfEntityReadAdapter<T> Create<T, TContext>(
        ResourceSchema<T> schema,
        IDbContextFactory<TContext> database,
        ReadOperations operations = ReadOperations.All
    )
        where T : class
        where TContext : DbContext =>
        EfEntityReadAdapter<T>.Create(
            schema,
            database,
            "Microsoft.EntityFrameworkCore.Sqlite",
            operations,
            BeginSnapshot<T>
        );

    public static Task<EfEntityReadAdapter<T>> CreateAsync<T, TContext>(
        ResourceSchema<T> schema,
        IDbContextFactory<TContext> database,
        ReadOperations operations = ReadOperations.All,
        CancellationToken cancellation = default
    )
        where T : class
        where TContext : DbContext =>
        EfEntityReadAdapter<T>.RegisterAsync(
            schema,
            database,
            "Microsoft.EntityFrameworkCore.Sqlite",
            operations,
            BeginSnapshot<T>,
            cancellation
        );

    public static EfEntityReadAdapter<T> Create<T>(
        ResourceSchema<T> schema,
        Func<DbContext> database,
        ReadOperations operations = ReadOperations.All
    )
        where T : class =>
        EfEntityReadAdapter<T>.Register(
            schema,
            database,
            "Microsoft.EntityFrameworkCore.Sqlite",
            operations,
            BeginSnapshot<T>
        );

    internal static async Task<EfSnapshotTransaction> BeginSnapshot<T>(
        DbContext db,
        CancellationToken cancellation
    )
        where T : class
    {
        if (
            db.Database.GetDbConnection() is not SqliteConnection connection
            || new SqliteConnectionStringBuilder(connection.ConnectionString).Cache
                != SqliteCacheMode.Private
        )
            throw new ProviderUnsupportedException(
                "SQLite snapshots require an explicit private-cache connection."
            );
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode";
        if (
            !string.Equals(
                (string?)await command.ExecuteScalarAsync(cancellation),
                "wal",
                StringComparison.OrdinalIgnoreCase
            )
        )
            throw new ProviderUnsupportedException(
                "SQLite snapshots require WAL provisioned by the host."
            );
        command.CommandText = "PRAGMA read_uncommitted";
        if (Convert.ToInt64(await command.ExecuteScalarAsync(cancellation)) != 0)
            throw new ProviderUnsupportedException("SQLite snapshots do not support dirty reads.");
        var entity = db.Model.FindEntityType(typeof(T))!;
        command.CommandText =
            "SELECT sql FROM main.sqlite_schema WHERE type='table' AND name=$table";
        command.Parameters.AddWithValue("$table", entity.GetTableName());
        var definition = await command.ExecuteScalarAsync(cancellation) as string;
        if (
            entity.GetSchema() != null
            || definition == null
            || !definition
                .TrimStart()
                .StartsWith("CREATE TABLE", StringComparison.OrdinalIgnoreCase)
        )
            throw new ProviderUnsupportedException(
                "SQLite snapshots require an ordinary mapped main-database table."
            );
        cancellation.ThrowIfCancellationRequested();
        return new(connection.BeginTransaction(deferred: true));
    }
}
