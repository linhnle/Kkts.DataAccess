using System.Data;
using Kkts.DataAccess.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Kkts.DataAccess.SqlServer;

public static class SqlServerEntityReads
{
    public static IEntityReadAdapter<T> Create<T>(Func<DbContext> database)
        where T : class =>
        EfEntityReadAdapter<T>.Register(
            database,
            "Microsoft.EntityFrameworkCore.SqlServer",
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
            throw new NotSupportedException(
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
            throw new NotSupportedException(
                "SQL Server snapshots require an ordinary mapped local table."
            );
        return new(await connection.BeginTransactionAsync(IsolationLevel.Snapshot, cancellation));
    }
}
