using Kkts.DataAccess.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Kkts.DataAccess.SqlServer;

public static class SqlServerEntityReads
{
    public static IEntityReadAdapter<T> Create<T>(Func<DbContext> database)
        where T : class =>
        EfEntityReadAdapter<T>.Register(database, "Microsoft.EntityFrameworkCore.SqlServer");
}
