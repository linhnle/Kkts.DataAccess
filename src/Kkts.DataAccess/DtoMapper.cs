using System.Text.Json;

namespace Kkts.DataAccess;

public static class DtoMapper
{
    internal static readonly DtoShapeCache Cache = new();

    public static IReadOnlyList<JsonElement> MapValues<T>(
        ResourceSchema<T> schema,
        IReadOnlyList<ProjectedReadRow> rows,
        IReadOnlyList<string> selection
    )
        where T : class =>
        ReadDiagnostics.Stage("map", () => MapValuesCore(schema, rows, selection));

    static IReadOnlyList<JsonElement> MapValuesCore<T>(
        ResourceSchema<T> schema,
        IReadOnlyList<ProjectedReadRow> rows,
        IReadOnlyList<string> selection
    )
        where T : class
    {
        var properties = selection.Select(name => schema.Resolve(name).Property).ToArray();
        foreach (var row in rows)
        {
            ArgumentNullException.ThrowIfNull(row);
            ProjectedReadRow.ValidateValues(row.OutputValues, properties);
        }
        if (selection.Count == 0)
            return Array.AsReadOnly(
                rows.Select(_ =>
                        JsonSerializer.SerializeToElement(new Dictionary<string, object>())
                    )
                    .ToArray()
            );
        var shape = Cache.GetOrBuild(DtoShapeKey.Create(schema, selection));
        return Array.AsReadOnly(rows.Select(row => shape.MapValues(row.OutputValues)).ToArray());
    }

    public static IReadOnlyList<JsonElement> Map<T>(
        ResourceSchema<T> schema,
        IReadOnlyList<T> entities,
        IReadOnlyList<string> selection
    )
        where T : class => ReadDiagnostics.Stage("map", () => MapCore(schema, entities, selection));

    static IReadOnlyList<JsonElement> MapCore<T>(
        ResourceSchema<T> schema,
        IReadOnlyList<T> entities,
        IReadOnlyList<string> selection
    )
        where T : class
    {
        if (selection.Count == 0)
            return Array.AsReadOnly(
                entities
                    .Select(_ =>
                        JsonSerializer.SerializeToElement(new Dictionary<string, object>())
                    )
                    .ToArray()
            );
        var shape = Cache.GetOrBuild(DtoShapeKey.Create(schema, selection));
        return Array.AsReadOnly(entities.Select(entity => shape.Map(entity)).ToArray());
    }
}
