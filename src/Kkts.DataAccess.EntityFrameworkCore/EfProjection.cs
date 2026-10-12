using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;

namespace Kkts.DataAccess.EntityFrameworkCore;

internal sealed class EfProjection<T>
    where T : class
{
    readonly int[] output;
    readonly int[] continuation;
    readonly Expression<Func<T, object[]>> selector;
    readonly bool empty;

    internal EfProjection(DbContext db, EntityReadPlan<T> read, EntitySeekPlan<T>? seek = null)
    {
        var model =
            db.Model.FindEntityType(typeof(T))
            ?? throw new ProviderUnsupportedException("Projection entity is not mapped.");
        var slots = new List<PropertyInfo>();
        int Slot(PropertyInfo binding)
        {
            EfModelCapabilities<T>.Demand(EfModelCapabilities<T>.Mapping(db, binding));
            var index = slots.IndexOf(binding);
            if (index >= 0)
                return index;
            slots.Add(binding);
            return slots.Count - 1;
        }
        output = read.OutputBindings.Select(binding => Slot(binding.Property)).ToArray();
        continuation = (seek?.ContinuationBindings ?? []).Select(Slot).ToArray();
        var parameter = Expression.Parameter(typeof(T), "row");
        var values = slots
            .Select(property =>
                (Expression)
                    Expression.Convert(Expression.Property(parameter, property), typeof(object))
            )
            .ToArray();
        empty = values.Length == 0;
        selector = Expression.Lambda<Func<T, object[]>>(
            Expression.NewArrayInit(typeof(object), values),
            parameter
        );
    }

    internal async Task<IReadOnlyList<ProjectedReadRow>> ReadAsync(
        IQueryable<T> query,
        CancellationToken cancellation
    )
    {
        if (empty)
        {
            // Select a scalar marker, not a constant buffer that EF would retain in its query cache.
            var markers = await query.Select(_ => 1).ToListAsync(cancellation);
            return markers.Select(_ => new ProjectedReadRow([])).ToArray();
        }
        var rows = await query.Select(selector).ToListAsync(cancellation);
        return rows.Select(Row).ToArray();
    }

    internal async Task<ProjectedReadRow?> SingleAsync(
        IQueryable<T> query,
        CancellationToken cancellation
    )
    {
        if (empty)
        {
            var marker = await query.Select(_ => (int?)1).SingleOrDefaultAsync(cancellation);
            return marker == null ? null : new ProjectedReadRow([]);
        }
        var row = await query.Select(selector).SingleOrDefaultAsync(cancellation);
        return row == null ? null : Row(row);
    }

    ProjectedReadRow Row(object[] slots) =>
        new(output.Select(index => slots[index]), continuation.Select(index => slots[index]));
}
