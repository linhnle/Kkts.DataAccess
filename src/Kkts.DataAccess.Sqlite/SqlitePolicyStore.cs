using Microsoft.Data.Sqlite;

namespace Kkts.DataAccess.Sqlite;

public sealed class PolicyConflictException()
    : Exception("Policy revision conflict. Reload before saving.");

public sealed record DraftSnapshot(
    string? Payload,
    long DraftRevision,
    long ActiveRevision,
    long? ActiveVersion
);

public sealed record AuditEvent(
    long Id,
    string Actor,
    string Scope,
    string Resource,
    string Time,
    string Operation,
    long DraftRevision,
    long ActiveRevision,
    long? Version,
    string Summary
);

public sealed class SqlitePolicyStore(string connectionString)
{
    public async Task InitializeAsync(CancellationToken cancellation = default)
    {
        await using var connection = await Open(cancellation);
        await Execute(
            connection,
            null,
            """
            CREATE TABLE IF NOT EXISTS PolicyMigrations(Version INTEGER PRIMARY KEY);
            CREATE TABLE IF NOT EXISTS PolicyHeads(
              Scope TEXT NOT NULL, Resource TEXT NOT NULL, DraftPayload TEXT,
              DraftRevision INTEGER NOT NULL DEFAULT 0, ActiveVersion INTEGER,
              ActiveRevision INTEGER NOT NULL DEFAULT 0, PRIMARY KEY(Scope,Resource));
            CREATE TABLE IF NOT EXISTS PolicyVersions(
              Id INTEGER PRIMARY KEY AUTOINCREMENT, Scope TEXT NOT NULL, Resource TEXT NOT NULL,
              SchemaVersion TEXT NOT NULL, Payload TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS PolicyAudit(
              Id INTEGER PRIMARY KEY AUTOINCREMENT, Actor TEXT NOT NULL, Scope TEXT NOT NULL,
              Resource TEXT NOT NULL, Time TEXT NOT NULL, Operation TEXT NOT NULL,
              DraftRevision INTEGER NOT NULL, ActiveRevision INTEGER NOT NULL, Version INTEGER, Summary TEXT NOT NULL);
            CREATE TRIGGER IF NOT EXISTS ImmutablePolicyUpdate BEFORE UPDATE ON PolicyVersions
              BEGIN SELECT RAISE(ABORT,'Published policies are immutable'); END;
            CREATE TRIGGER IF NOT EXISTS ImmutablePolicyDelete BEFORE DELETE ON PolicyVersions
              BEGIN SELECT RAISE(ABORT,'Published policies are immutable'); END;
            CREATE TRIGGER IF NOT EXISTS ImmutableAuditUpdate BEFORE UPDATE ON PolicyAudit
              BEGIN SELECT RAISE(ABORT,'Audit history is append only'); END;
            CREATE TRIGGER IF NOT EXISTS ImmutableAuditDelete BEFORE DELETE ON PolicyAudit
              BEGIN SELECT RAISE(ABORT,'Audit history is append only'); END;
            INSERT OR IGNORE INTO PolicyMigrations(Version) VALUES(1);
            """,
            cancellation
        );
    }

    public async Task<DraftSnapshot> ReadDraftAsync(
        AdministrationContext admin,
        string scope,
        string resource,
        CancellationToken cancellation = default
    )
    {
        admin.Demand(scope);
        await using var c = await Open(cancellation);
        return await ReadHead(c, null, scope, resource, cancellation);
    }

    public async Task<long> SaveDraftAsync(
        AdministrationContext admin,
        string scope,
        string resource,
        string payload,
        long expectedRevision,
        CancellationToken cancellation = default
    )
    {
        admin.Demand(scope);
        if (payload.Length > 1_048_576)
            throw new AuthorizationException("Draft payload exceeds limit.");
        await using var c = await Open(cancellation);
        using var tx = c.BeginTransaction();
        await Execute(
            c,
            tx,
            "INSERT OR IGNORE INTO PolicyHeads(Scope,Resource) VALUES($s,$r)",
            cancellation,
            ("$s", scope),
            ("$r", resource)
        );
        var head = await ReadHead(c, tx, scope, resource, cancellation);
        if (head.DraftRevision != expectedRevision)
            throw new PolicyConflictException();
        await Execute(
            c,
            tx,
            "UPDATE PolicyHeads SET DraftPayload=$p,DraftRevision=DraftRevision+1 WHERE Scope=$s AND Resource=$r",
            cancellation,
            ("$p", payload),
            ("$s", scope),
            ("$r", resource)
        );
        await Audit(
            c,
            tx,
            admin,
            scope,
            resource,
            "SaveDraft",
            head.DraftRevision + 1,
            head.ActiveRevision,
            head.ActiveVersion,
            "Draft updated",
            cancellation
        );
        await tx.CommitAsync(cancellation);
        return head.DraftRevision + 1;
    }

    public async Task<long> PublishAsync<T>(
        AdministrationContext admin,
        string scope,
        ResourceSchema<T> schema,
        ContextRegistry registry,
        long expectedDraft,
        long expectedActive,
        PolicyLimits? limits = null,
        Func<Task>? beforeCommit = null,
        CancellationToken cancellation = default
    )
        where T : class
    {
        admin.Demand(scope);
        await using var c = await Open(cancellation);
        using var tx = c.BeginTransaction();
        var head = await ReadHead(c, tx, scope, schema.Resource, cancellation);
        if (head.DraftRevision != expectedDraft || head.ActiveRevision != expectedActive)
            throw new PolicyConflictException();
        if (head.Payload == null)
            throw new AuthorizationException("No draft exists.");
        var policy = PolicyJson.Deserialize(head.Payload);
        new PolicyValidator<T>(schema, registry, limits).DemandValid(policy);
        await Execute(
            c,
            tx,
            "INSERT INTO PolicyVersions(Scope,Resource,SchemaVersion,Payload) VALUES($s,$r,$v,$p)",
            cancellation,
            ("$s", scope),
            ("$r", schema.Resource),
            ("$v", schema.Version),
            ("$p", PolicyJson.Serialize(policy))
        );
        using var idCommand = c.CreateCommand();
        idCommand.Transaction = tx;
        idCommand.CommandText = "SELECT last_insert_rowid()";
        var version = (long)(await idCommand.ExecuteScalarAsync(cancellation))!;
        await Execute(
            c,
            tx,
            "UPDATE PolicyHeads SET ActiveVersion=$v,ActiveRevision=ActiveRevision+1 WHERE Scope=$s AND Resource=$r",
            cancellation,
            ("$v", version),
            ("$s", scope),
            ("$r", schema.Resource)
        );
        await Audit(
            c,
            tx,
            admin,
            scope,
            schema.Resource,
            "Publish",
            head.DraftRevision,
            head.ActiveRevision + 1,
            version,
            "Published validated draft",
            cancellation
        );
        if (beforeCommit != null)
            await beforeCommit();
        await tx.CommitAsync(cancellation);
        return version;
    }

    public async Task RollbackAsync<T>(
        AdministrationContext admin,
        string scope,
        ResourceSchema<T> schema,
        ContextRegistry registry,
        long version,
        long expectedActive,
        PolicyLimits? limits = null,
        CancellationToken cancellation = default
    )
        where T : class
    {
        admin.Demand(scope);
        await using var c = await Open(cancellation);
        using var tx = c.BeginTransaction();
        var head = await ReadHead(c, tx, scope, schema.Resource, cancellation);
        if (head.ActiveRevision != expectedActive)
            throw new PolicyConflictException();
        var captured = await ReadVersion(c, tx, scope, schema.Resource, version, cancellation);
        if (captured.SchemaVersion != schema.Version)
            throw new AuthorizationException("Rollback schema is incompatible.");
        new PolicyValidator<T>(schema, registry, limits).DemandValid(
            PolicyJson.Deserialize(captured.Payload)
        );
        await Execute(
            c,
            tx,
            "UPDATE PolicyHeads SET ActiveVersion=$v,ActiveRevision=ActiveRevision+1 WHERE Scope=$s AND Resource=$r",
            cancellation,
            ("$v", version),
            ("$s", scope),
            ("$r", schema.Resource)
        );
        await Audit(
            c,
            tx,
            admin,
            scope,
            schema.Resource,
            "Rollback",
            head.DraftRevision,
            head.ActiveRevision + 1,
            version,
            "Selected compatible immutable version",
            cancellation
        );
        await tx.CommitAsync(cancellation);
    }

    public async Task<CapturedPolicy> CaptureAsync(
        string scope,
        string resource,
        CancellationToken cancellation = default
    )
    {
        await using var c = await Open(cancellation);
        using var tx = c.BeginTransaction(deferred: true);
        var head = await ReadHead(c, tx, scope, resource, cancellation);
        if (head.ActiveVersion == null)
            throw new AuthorizationException("No active policy.");
        var captured = await ReadVersion(
            c,
            tx,
            scope,
            resource,
            head.ActiveVersion.Value,
            cancellation
        );
        await tx.CommitAsync(cancellation);
        return captured;
    }

    public async Task<IReadOnlyList<AuditEvent>> HistoryAsync(
        AdministrationContext admin,
        string scope,
        string resource,
        CancellationToken cancellation = default
    )
    {
        admin.Demand(scope);
        await using var c = await Open(cancellation);
        using var cmd = c.CreateCommand();
        cmd.CommandText =
            "SELECT Id,Actor,Scope,Resource,Time,Operation,DraftRevision,ActiveRevision,Version,Summary FROM PolicyAudit WHERE Scope=$s AND Resource=$r ORDER BY Id";
        cmd.Parameters.AddWithValue("$s", scope);
        cmd.Parameters.AddWithValue("$r", resource);
        using var reader = await cmd.ExecuteReaderAsync(cancellation);
        var events = new List<AuditEvent>();
        while (await reader.ReadAsync(cancellation))
            events.Add(
                new(
                    reader.GetInt64(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    reader.GetString(5),
                    reader.GetInt64(6),
                    reader.GetInt64(7),
                    reader.IsDBNull(8) ? null : reader.GetInt64(8),
                    reader.GetString(9)
                )
            );
        return events.AsReadOnly();
    }

    async Task<SqliteConnection> Open(CancellationToken token)
    {
        var c = new SqliteConnection(connectionString);
        try
        {
            await c.OpenAsync(token);
            return c;
        }
        catch
        {
            await c.DisposeAsync();
            throw;
        }
    }

    static async Task<DraftSnapshot> ReadHead(
        SqliteConnection c,
        SqliteTransaction? tx,
        string scope,
        string resource,
        CancellationToken token
    )
    {
        using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText =
            "SELECT DraftPayload,DraftRevision,ActiveRevision,ActiveVersion FROM PolicyHeads WHERE Scope=$s AND Resource=$r";
        cmd.Parameters.AddWithValue("$s", scope);
        cmd.Parameters.AddWithValue("$r", resource);
        using var reader = await cmd.ExecuteReaderAsync(token);
        return await reader.ReadAsync(token)
            ? new(
                reader.IsDBNull(0) ? null : reader.GetString(0),
                reader.GetInt64(1),
                reader.GetInt64(2),
                reader.IsDBNull(3) ? null : reader.GetInt64(3)
            )
            : new(null, 0, 0, null);
    }

    static async Task<CapturedPolicy> ReadVersion(
        SqliteConnection c,
        SqliteTransaction? tx,
        string scope,
        string resource,
        long version,
        CancellationToken token
    )
    {
        using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText =
            "SELECT SchemaVersion,Payload FROM PolicyVersions WHERE Id=$v AND Scope=$s AND Resource=$r";
        cmd.Parameters.AddWithValue("$v", version);
        cmd.Parameters.AddWithValue("$s", scope);
        cmd.Parameters.AddWithValue("$r", resource);
        using var reader = await cmd.ExecuteReaderAsync(token);
        if (!await reader.ReadAsync(token))
            throw new AuthorizationException("Unknown policy version in this scope/resource.");
        return new(scope, resource, reader.GetString(0), version, reader.GetString(1));
    }

    static async Task Audit(
        SqliteConnection c,
        SqliteTransaction tx,
        AdministrationContext admin,
        string scope,
        string resource,
        string operation,
        long draft,
        long active,
        long? version,
        string summary,
        CancellationToken token
    )
    {
        await Execute(
            c,
            tx,
            "INSERT INTO PolicyAudit(Actor,Scope,Resource,Time,Operation,DraftRevision,ActiveRevision,Version,Summary) VALUES($a,$s,$r,$t,$o,$d,$h,$v,$m)",
            token,
            ("$a", admin.Actor),
            ("$s", scope),
            ("$r", resource),
            ("$t", DateTimeOffset.UtcNow.ToString("O")),
            ("$o", operation),
            ("$d", draft),
            ("$h", active),
            ("$v", version),
            ("$m", summary)
        );
    }

    static async Task<int> Execute(
        SqliteConnection c,
        SqliteTransaction? tx,
        string sql,
        CancellationToken token,
        params (string Name, object? Value)[] parameters
    )
    {
        using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        foreach (var p in parameters)
            cmd.Parameters.AddWithValue(p.Name, p.Value ?? DBNull.Value);
        return await cmd.ExecuteNonQueryAsync(token);
    }
}
