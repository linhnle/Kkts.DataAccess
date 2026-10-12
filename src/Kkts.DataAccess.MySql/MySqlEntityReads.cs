using System.Data;
using Kkts.DataAccess.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using MySql.Data.MySqlClient;

namespace Kkts.DataAccess.MySql;

public static class MySqlEntityReads
{
    public static IEntityReadAdapter<T> Create<T>(Func<DbContext> database)
        where T : class =>
        EfEntityReadAdapter<T>.Register(database, "MySql.EntityFrameworkCore", BeginSnapshot<T>);

    static async Task<EfSnapshotTransaction> BeginSnapshot<T>(
        DbContext db,
        CancellationToken cancellation
    )
        where T : class
    {
        var connection = db.Database.GetDbConnection();
        var entity = db.Model.FindEntityType(typeof(T))!;
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT ENGINE FROM information_schema.TABLES
            WHERE TABLE_SCHEMA=@schema AND TABLE_NAME=@table AND TABLE_TYPE='BASE TABLE'
            """;
        var table = command.CreateParameter();
        table.ParameterName = "@table";
        table.Value = entity.GetTableName()!;
        command.Parameters.Add(table);
        var schema = command.CreateParameter();
        schema.ParameterName = "@schema";
        schema.Value = entity.GetSchema() ?? connection.Database;
        command.Parameters.Add(schema);
        if (
            !string.Equals(
                (string?)await command.ExecuteScalarAsync(cancellation),
                "InnoDB",
                StringComparison.OrdinalIgnoreCase
            )
        )
            throw new NotSupportedException(
                "MySQL snapshots require a verified InnoDB mapped table."
            );
        var transaction = await connection.BeginTransactionAsync(
            IsolationLevel.RepeatableRead,
            cancellation
        );
        var marker = "kda_export_" + Guid.NewGuid().ToString("N");
        try
        {
            using var initialize = connection.CreateCommand();
            initialize.Transaction = transaction;
            initialize.CommandText = $"SAVEPOINT {marker}";
            await initialize.ExecuteNonQueryAsync(cancellation);
            return new(
                transaction,
                async token =>
                {
                    using var verify = connection.CreateCommand();
                    verify.Transaction = transaction;
                    verify.CommandText = $"RELEASE SAVEPOINT {marker}";
                    try
                    {
                        await verify.ExecuteNonQueryAsync(token);
                    }
                    catch (MySqlException ex) when (ex.Number == 1305)
                    {
                        throw new InvalidOperationException(
                            "Export snapshot transaction was lost.",
                            ex
                        );
                    }
                    verify.CommandText = $"SAVEPOINT {marker}";
                    await verify.ExecuteNonQueryAsync(token);
                }
            );
        }
        catch
        {
            await transaction.DisposeAsync();
            throw;
        }
    }
}
