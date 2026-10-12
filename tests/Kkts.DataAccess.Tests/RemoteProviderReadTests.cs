using Microsoft.EntityFrameworkCore;
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
}
