using System.Text.Json;

namespace Kkts.DataAccess;

public static class DtoMapper
{
    internal static readonly DtoShapeCache Cache = new();

    public static IReadOnlyList<JsonElement> Map<T>(
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
