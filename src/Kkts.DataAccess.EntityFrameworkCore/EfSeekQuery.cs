using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

namespace Kkts.DataAccess.EntityFrameworkCore;

internal static class EfSeekQuery<T>
    where T : class
{
    internal static void ValidateOrder(DbContext db, EntityReadPlan<T> read) =>
        _ = SortKeys(db, read, Expression.Parameter(typeof(T), "row"));

    static List<Expression> SortKeys(
        DbContext db,
        EntityReadPlan<T> read,
        ParameterExpression parameter
    )
    {
        var model =
            db.Model.FindEntityType(typeof(T))
            ?? throw new InvalidOperationException("Seek entity is not mapped.");
        var keys = new List<Expression>();
        foreach (var sort in read.Sort)
        {
            var property = model.FindProperty(sort.Property.Name);
            var type =
                Nullable.GetUnderlyingType(sort.Property.PropertyType)
                ?? sort.Property.PropertyType;
            if (
                property?.PropertyInfo != sort.Property
                || property.ClrType != sort.Property.PropertyType
                || property.GetValueConverter() != null
                || property.GetProviderClrType() != null
                || !(
                    type == typeof(string)
                    || type == typeof(byte)
                    || type == typeof(sbyte)
                    || type == typeof(short)
                    || type == typeof(int)
                    || type == typeof(long)
                    || type == typeof(decimal)
                    || type == typeof(DateTime)
                    || type.IsEnum
                )
            )
                throw new NotSupportedException(
                    $"Seek ordering for '{sort.Property.Name}' requires a supported mapped scalar without a custom converter."
                );
            keys.Add(Expression.Property(parameter, sort.Property));
        }
        return keys;
    }

    internal static IQueryable<T> Apply(DbContext db, IQueryable<T> query, EntitySeekPlan<T> plan)
    {
        var parameter = Expression.Parameter(typeof(T), "row");
        var keys = SortKeys(db, plan.Read, parameter);
        if (plan.Boundary != null)
        {
            Expression after = Expression.Constant(false);
            Expression prefix = Expression.Constant(true);
            for (var i = 0; i < keys.Count; i++)
            {
                var key = keys[i];
                var value = plan.Boundary[i];
                var nullable =
                    !key.Type.IsValueType || Nullable.GetUnderlyingType(key.Type) != null;
                var isNull = nullable
                    ? Expression.Equal(key, Expression.Constant(null, key.Type))
                    : (Expression)Expression.Constant(false);
                Expression comparison;
                Expression equal;
                if (value == null)
                {
                    equal = isNull;
                    comparison = plan.Read.Sort[i].Descending
                        ? Expression.Constant(false)
                        : Expression.Not(isNull);
                }
                else
                {
                    Expression constant = Expression.Constant(value, value.GetType());
                    if (constant.Type != key.Type)
                        constant = Expression.Convert(constant, key.Type);
                    equal = Expression.Equal(key, constant);
                    Expression left = key,
                        right = constant;
                    var scalarType = Nullable.GetUnderlyingType(key.Type) ?? key.Type;
                    if (scalarType == typeof(string))
                    {
                        left = Expression.Call(
                            typeof(string),
                            nameof(string.Compare),
                            Type.EmptyTypes,
                            key,
                            constant
                        );
                        right = Expression.Constant(0);
                    }
                    else if (scalarType.IsEnum)
                    {
                        var underlying = Enum.GetUnderlyingType(scalarType);
                        var converted =
                            Nullable.GetUnderlyingType(key.Type) == null
                                ? underlying
                                : typeof(Nullable<>).MakeGenericType(underlying);
                        left = Expression.Convert(key, converted);
                        right = Expression.Convert(constant, converted);
                    }
                    comparison = plan.Read.Sort[i].Descending
                        ? Expression.LessThan(left, right)
                        : Expression.GreaterThan(left, right);
                    comparison = Expression.AndAlso(Expression.Not(isNull), comparison);
                    if (plan.Read.Sort[i].Descending)
                        comparison = Expression.OrElse(isNull, comparison);
                }
                after = Expression.OrElse(after, Expression.AndAlso(prefix, comparison));
                prefix = Expression.AndAlso(prefix, equal);
            }
            query = query.Where(Expression.Lambda<Func<T, bool>>(after, parameter));
        }
        var ordered = false;
        for (var i = 0; i < keys.Count; i++)
        {
            var key = keys[i];
            if (!key.Type.IsValueType || Nullable.GetUnderlyingType(key.Type) != null)
            {
                var rank = Expression.Condition(
                    Expression.Equal(key, Expression.Constant(null, key.Type)),
                    Expression.Constant(0),
                    Expression.Constant(1)
                );
                query = Order(query, rank, parameter, plan.Read.Sort[i].Descending, ordered);
                ordered = true;
            }
            query = Order(query, key, parameter, plan.Read.Sort[i].Descending, ordered);
            ordered = true;
        }
        return query;
    }

    static IQueryable<T> Order(
        IQueryable<T> query,
        Expression key,
        ParameterExpression parameter,
        bool descending,
        bool ordered
    ) =>
        query.Provider.CreateQuery<T>(
            Expression.Call(
                typeof(Queryable),
                ordered
                    ? (descending ? "ThenByDescending" : "ThenBy")
                    : (descending ? "OrderByDescending" : "OrderBy"),
                [typeof(T), key.Type],
                query.Expression,
                Expression.Quote(Expression.Lambda(key, parameter))
            )
        );
}
