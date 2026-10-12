using Kkts.DataAccess.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Kkts.DataAccess.Sqlite;

public static class SqliteEntityReads
{
    public static IEntityReadAdapter<T> Create<T>(Func<DbContext> database)
        where T : class =>
        EfEntityReadAdapter<T>.Register(database, "Microsoft.EntityFrameworkCore.Sqlite");
}
