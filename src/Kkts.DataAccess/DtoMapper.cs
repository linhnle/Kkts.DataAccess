using System.Text.Json;
using Kkts.Dynamic;

namespace Kkts.DataAccess;

public static class DtoMapper
{
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
        using var assembly = new DynamicAssembly();
        var shape = assembly.DeclareClass(
            "AuthorizedDto",
            typeof(T),
            selection.Select(name =>
            {
                var field = schema.Resolve(name);
                return new PropertyBinding
                {
                    EntityProperty = field.Property.Name,
                    DtoProperty = field.Name,
                    Mode = BindingMode.OneWayToDto,
                };
            })
        );
        assembly.Build();
        return Array.AsReadOnly(
            entities
                .Select(entity =>
                {
                    var dto = shape.CreateDto(entity);
                    return JsonSerializer.SerializeToElement(dto, dto.GetType());
                })
                .ToArray()
        );
    }
}
