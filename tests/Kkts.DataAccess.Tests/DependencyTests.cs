using System.Linq.Expressions;
using System.Text.Json;
using Kkts.Dynamic;
using Kkts.Expressions;
using Xunit;

namespace Kkts.DataAccess.Tests;

public class DependencyTests
{
    [Fact]
    public void Official_packages_construct_predicates_and_serialize_only_bound_fields()
    {
        var predicate = new Filter
        {
            Property = "Id",
            Operator = "=",
            Value = "1",
        }.BuildPredicate<Employee>();
        Expression<Func<Employee, bool>> mandatory = e => e.TenantId == 10;
        var composed = mandatory.And(predicate);
        Assert.True(composed.Compile()(new Employee { Id = 1, TenantId = 10 }));
        Assert.False(composed.Compile()(new Employee { Id = 1, TenantId = 20 }));
        var assembly = new DynamicAssembly();
        var shape = assembly.DeclareClass(
            "EmployeeDto",
            typeof(Employee),
            new[]
            {
                new PropertyBinding
                {
                    EntityProperty = "Name",
                    DtoProperty = "Name",
                    Mode = BindingMode.OneWayToDto,
                },
            }
        );
        assembly.Build();
        var dto = Activator.CreateInstance(shape.GetBuiltType())!;
        Mapper.MapFromEntityToDto(
            dto,
            new Employee
            {
                Id = 1,
                Name = "Alice",
                Salary = 123,
            }
        );
        Assert.Equal("{\"Name\":\"Alice\"}", JsonSerializer.Serialize(dto, dto.GetType()));
    }

    [Fact]
    public void Typed_public_schema_and_nested_tree_restrict_entity_fields()
    {
        var schema = new QuerySchema<Employee>()
            .Field("employeeId", e => e.Id)
            .Field("displayName", e => e.Name)
            .Build();
        var context = new ExpressionQueryContext(schema, new QueryPolicy());
        var tree = FilterNode.And(
            new[]
            {
                FilterNode.Condition("employeeId", "=", FilterValue.Number("1")),
                FilterNode.Not(
                    FilterNode.Condition("displayName", "=", FilterValue.String("Excluded"))
                ),
            }
        );
        var decoded = FilterTreeJson.TryDeserialize(FilterTreeJson.Serialize(tree), context);
        Assert.True(decoded.Succeeded);
        var predicate = context.BuildPredicate<Employee>(decoded.Result).Compile();
        Assert.True(predicate(new Employee { Id = 1, Name = "Alice" }));
        Assert.False(predicate(new Employee { Id = 1, Name = "Excluded" }));
        Assert.False(context.ParsePredicate<Employee>("Salary = 123").Succeeded);
        Assert.False(context.ParsePredicate<Employee>("Id = 1").Succeeded);
    }

    [Fact]
    public void Typed_dto_shape_has_only_explicit_public_bindings()
    {
        using var assembly = new DynamicAssembly();
        var shape = assembly.DeclareClass<Employee>(
            "PublicEmployee",
            b => b.Map(e => e.Name, "DisplayName", BindingMode.OneWayToDto)
        );
        assembly.Build();
        var dto = shape.CreateDto(new Employee { Name = "Alice", Salary = 123 });
        Assert.Equal("{\"DisplayName\":\"Alice\"}", JsonSerializer.Serialize(dto, dto.GetType()));
        Assert.True(shape.GetMetadata().ResolveField("DisplayName").Success);
        Assert.False(shape.GetMetadata().ResolveField("Salary").Success);
    }

    public class Employee
    {
        public int Id { get; set; }
        public int TenantId { get; set; }
        public string Name { get; set; } = "";
        public decimal Salary { get; set; }
    }
}
