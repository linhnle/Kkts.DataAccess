using System.Text.Json;
using Xunit;

namespace Kkts.DataAccess.Tests;

public class SchemaTests
{
    public class Employee
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public decimal? Salary { get; set; }
        public Employee? Manager { get; set; }
    }

    static ResourceField Id() =>
        ResourceSchema<Employee>.Field("Id", e => e.Id, [FieldOperation.Select]);

    [Fact]
    public void Invalid_bindings_and_duplicate_names_are_rejected()
    {
        Assert.Throws<ArgumentException>(() =>
            new ResourceSchema<Employee>("Employee", "1", [Id(), Id()], "Id")
        );
        Assert.Throws<ArgumentException>(() =>
            ResourceSchema<Employee>.Field("Manager", e => e.Manager, [FieldOperation.Select])
        );
        Assert.Throws<ArgumentException>(() =>
            ResourceSchema<Employee>.Field("ManagerId", e => e.Manager!.Id, [FieldOperation.Select])
        );
        Assert.Throws<ArgumentException>(() =>
            ResourceSchema<Employee>.Field("Id", e => e.Id + 1, [FieldOperation.Select])
        );
        Assert.Throws<ArgumentException>(() =>
            ResourceSchema<Employee>.Field("Id", e => e.Id, [FieldOperation.Select], ["contains"])
        );
    }

    [Fact]
    public void Metadata_requires_administration_in_the_host_scope()
    {
        var schema = new ResourceSchema<Employee>("Employee", "1", [Id()], "Id");
        Assert.Throws<AuthorizationException>(() =>
            schema.Metadata(new("actor", "tenant", false), "tenant")
        );
        Assert.Throws<AuthorizationException>(() =>
            schema.Metadata(new("actor", "other", true), "tenant")
        );
        var metadata = schema.Metadata(new("actor", "tenant", true), "tenant");
        Assert.Single(metadata.Fields);
        Assert.Equal("Id", metadata.Fields[0].Name);
        Assert.Throws<AuthorizationException>(() => schema.Resolve("Manager.Id"));
    }

    [Theory]
    [InlineData("1.5")]
    [InlineData("2147483648")]
    [InlineData("\"1\"")]
    [InlineData("null")]
    public void Integer_conversion_rejects_lossy_or_incompatible_literals(string json)
    {
        Assert.Throws<AuthorizationException>(() =>
            LiteralConverter.Convert(JsonDocument.Parse(json).RootElement, typeof(int))
        );
    }

    [Fact]
    public void Conversion_rejects_decimal_rounding_and_binary_precision_loss()
    {
        foreach (var json in new[] { "0.12345678901234567890123456789123", "1e-50" })
            Assert.Throws<AuthorizationException>(() =>
                LiteralConverter.Convert(JsonDocument.Parse(json).RootElement, typeof(decimal))
            );
        Assert.Throws<AuthorizationException>(() =>
            LiteralConverter.Convert(JsonDocument.Parse("0.1").RootElement, typeof(double))
        );
        Assert.Equal(
            1.5,
            LiteralConverter.Convert(JsonDocument.Parse("1.5").RootElement, typeof(double))
        );
    }

    [Fact]
    public void Nullable_numeric_and_string_values_are_typed()
    {
        Assert.Null(LiteralConverter.Convert(JsonDocument.Parse("null").RootElement, typeof(int?)));
        Assert.Equal(
            123,
            LiteralConverter.Convert(JsonDocument.Parse("123").RootElement, typeof(int?))
        );
        Assert.Equal(
            "Alice",
            LiteralConverter.Convert(JsonDocument.Parse("\"Alice\"").RootElement, typeof(string))
        );
    }
}
