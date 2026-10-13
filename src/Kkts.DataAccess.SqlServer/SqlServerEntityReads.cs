using System.Data;
using Kkts.DataAccess.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Kkts.DataAccess.SqlServer;

public static class SqlServerEntityReads
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
            "Microsoft.EntityFrameworkCore.SqlServer",
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
            "Microsoft.EntityFrameworkCore.SqlServer",
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
            "Microsoft.EntityFrameworkCore.SqlServer",
            operations,
            BeginSnapshot<T>
        );

    static async Task<EfSnapshotTransaction> BeginSnapshot<T>(
        DbContext db,
        CancellationToken cancellation
    )
        where T : class
    {
        var connection = db.Database.GetDbConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT snapshot_isolation_state FROM sys.databases WHERE database_id=DB_ID()";
        if (Convert.ToInt32(await command.ExecuteScalarAsync(cancellation)) != 1)
            throw new ProviderUnsupportedException(
                "SQL Server snapshots require ALLOW_SNAPSHOT_ISOLATION fully enabled."
            );
        var entity = db.Model.FindEntityType(typeof(T))!;
        command.CommandText = """
            SELECT COUNT(*) FROM sys.tables
            WHERE name=@table AND schema_id=SCHEMA_ID(COALESCE(@schema,SCHEMA_NAME()))
              AND is_memory_optimized=0 AND temporal_type=0
              AND object_id NOT IN (SELECT object_id FROM sys.external_tables)
            """;
        var table = command.CreateParameter();
        table.ParameterName = "@table";
        table.Value = entity.GetTableName()!;
        command.Parameters.Add(table);
        var schema = command.CreateParameter();
        schema.ParameterName = "@schema";
        schema.Value = (object?)entity.GetSchema() ?? DBNull.Value;
        command.Parameters.Add(schema);
        if (Convert.ToInt32(await command.ExecuteScalarAsync(cancellation)) != 1)
            throw new ProviderUnsupportedException(
                "SQL Server snapshots require an ordinary mapped local table."
            );
        return new(await connection.BeginTransactionAsync(IsolationLevel.Snapshot, cancellation));
    }
}
