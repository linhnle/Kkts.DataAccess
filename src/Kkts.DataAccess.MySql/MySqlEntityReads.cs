using Kkts.DataAccess.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Kkts.DataAccess.MySql;

public static class MySqlEntityReads
{
    public static IEntityReadAdapter<T> Create<T>(Func<DbContext> database)
        where T : class => EfEntityReadAdapter<T>.Register(database, "MySql.EntityFrameworkCore");
}
