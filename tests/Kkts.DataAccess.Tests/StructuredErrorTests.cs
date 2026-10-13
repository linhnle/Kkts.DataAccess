using System.Text.Json;
using Xunit;

namespace Kkts.DataAccess.Tests;

public class StructuredErrorTests
{
    [Fact]
    public void Catalog_categories_and_codes_are_distinct_and_serializable()
    {
        foreach (var category in Enum.GetValues<DataAccessErrorCategory>())
        {
            var code = Enum.Parse<DataAccessErrorCode>(category.ToString());
            var error = new DataAccessException(code, "Non-contractual text.");
            Assert.Equal(category, error.Category);
            Assert.Equal(
                code,
                JsonSerializer.Deserialize<DataAccessErrorCode>(
                    JsonSerializer.Serialize(error.Code)
                )
            );
            Assert.DoesNotContain(category.ToString(), JsonSerializer.Serialize(code));
        }
        foreach (var code in Enum.GetValues<DataAccessErrorCode>())
            Assert.Equal(
                code,
                JsonSerializer.Deserialize<DataAccessErrorCode>(JsonSerializer.Serialize(code))
            );
    }

    [Fact]
    public void Diagnostics_are_owned_immutable_and_bounded_and_causes_are_preserved()
    {
        var diagnostic = new Diagnostic(
            DataAccessErrorCode.InvalidLiteral,
            "/filter/literal",
            "Invalid literal."
        );
        var source = Enumerable.Repeat(diagnostic, 300).ToArray();
        var cause = new InvalidOperationException("internal sentinel");
        var error = new InvalidRequestException(diagnostics: source, innerException: cause);
        source[0] = diagnostic with { Path = "/changed" };
        Assert.Equal(DataAccessException.MaxDiagnostics, error.Diagnostics.Count);
        Assert.Equal("/filter/literal", error.Diagnostics[0].Path);
        Assert.Same(cause, error.InnerException);
        Assert.Throws<NotSupportedException>(() =>
            ((IList<Diagnostic>)error.Diagnostics).Add(diagnostic)
        );
        var json = JsonSerializer.Serialize(error.Diagnostics, PolicyJson.Options);
        Assert.Contains("\"code\":\"invalid_literal\"", json);
        Assert.DoesNotContain("sentinel", json);
    }

    [Fact]
    public void Json_pointer_preserves_root_groups_indices_and_escapes_segments()
    {
        Assert.Equal("", JsonPointer.Append(""));
        Assert.Equal(
            "/filter/all/1/not/operator",
            JsonPointer.Append("/filter", "all", "1", "not", "operator")
        );
        Assert.Equal("/select/a~1b~0c", JsonPointer.Append("", "select", "a/b~c"));
        Assert.Equal("/sort/0/field", JsonPointer.Append("", "sort", "0", "field"));
        Assert.Equal("/key", JsonPointer.Append("", "key"));
        Assert.Throws<ArgumentException>(() => JsonPointer.Append("$", "field"));
    }

    [Fact]
    public void Validation_locates_actual_nested_input_without_literal_disclosure()
    {
        var validator = new PolicyValidator<CompilerTests.Employee>(
            CompilerTests.Schema,
            CompilerTests.Registry
        );
        var condition = new Condition
        {
            All =
            [
                CompilerTests.Leaf("Id", "eq", 1),
                new() { Any = [new() { Not = CompilerTests.Leaf("Id", "sentinel", 1) }] },
            ],
        };
        var error = Assert.Throws<InvalidRequestException>(() =>
            validator.DemandCondition(condition, "/filter")
        );
        Assert.Equal(DataAccessErrorCode.InvalidOperator, error.Diagnostics[0].Code);
        Assert.Equal("/filter/all/1/any/0/not/operator", error.Diagnostics[0].Path);
        Assert.DoesNotContain("sentinel", JsonSerializer.Serialize(error.Diagnostics));
        var invalidLiteral = Assert.Throws<InvalidRequestException>(() =>
            validator.DemandCondition(CompilerTests.Leaf("Id", "eq", "private sentinel"), "/filter")
        );
        Assert.Equal(DataAccessErrorCode.InvalidLiteral, invalidLiteral.Diagnostics[0].Code);
        Assert.Equal("/filter/literal", invalidLiteral.Diagnostics[0].Path);
        Assert.DoesNotContain(
            "private sentinel",
            JsonSerializer.Serialize(invalidLiteral.Diagnostics)
        );
    }

    [Fact]
    public void Unsupported_field_grants_are_detected_structurally()
    {
        var error = Assert.Throws<InvalidRequestException>(() =>
            PolicyJson.Deserialize(
                """{"resource":"Employee","rules":[{"fields":{"Select":["secret"]}}]}"""
            )
        );
        Assert.Equal(DataAccessErrorCode.ConditionalFieldsUnsupported, error.Diagnostics[0].Code);
        Assert.Equal("/rules/0/fields", error.Diagnostics[0].Path);
        Assert.DoesNotContain("secret", JsonSerializer.Serialize(error.Diagnostics));
    }

    [Fact]
    public void Code_catalog_documentation_contains_every_serialized_code()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "Kkts.DataAccess.slnx")))
            root = root.Parent;
        Assert.NotNull(root);
        var documentation = File.ReadAllText(Path.Combine(root.FullName, "docs/errors.md"));
        foreach (var code in Enum.GetValues<DataAccessErrorCode>())
            Assert.Contains(JsonSerializer.Serialize(code).Trim('"'), documentation);
    }

    [Fact]
    public void Pinned_dependency_codes_are_preserved_and_unknown_codes_are_bounded()
    {
        var expressions = new ExpressionsAdapter<CompilerTests.Employee>(
            CompilerTests.Schema,
            CompilerTests.Registry,
            new()
        );
        var diagnostic = Assert.Single(
            expressions.ValidateLeaf(CompilerTests.Leaf("Id", "contains", 1), "/filter", false)
        );
        Assert.Equal(DataAccessErrorCode.OperatorNotApplicable, diagnostic.Code);
        Assert.Equal("\"operator-not-applicable\"", JsonSerializer.Serialize(diagnostic.Code));
        Assert.Equal("/filter/operator", diagnostic.Path);
        var fallback = ExpressionsAdapter<CompilerTests.Employee>.DependencyDiagnostic(
            "unknown-dependency-code",
            "/filter",
            new()
        );
        Assert.Equal(DataAccessErrorCode.InvalidCondition, fallback.Code);
    }

    [Fact]
    public void Validator_diagnostics_are_bounded_and_captured_policy_paths_are_not_disclosed()
    {
        var validator = new PolicyValidator<CompilerTests.Employee>(
            CompilerTests.Schema,
            CompilerTests.Registry
        );
        var policy = CompilerTests.Definition(
            Enumerable
                .Range(0, 100)
                .Select(index =>
                    CompilerTests.Rule(
                        index.ToString(),
                        Effect.Allow,
                        new Condition { All = Enumerable.Repeat(new Condition(), 100).ToArray() }
                    )
                )
                .ToArray()
        );
        Assert.Equal(DataAccessException.MaxDiagnostics, validator.Validate(policy).Count);
        var error = Assert.Throws<SchemaIncompatibleException>(() =>
            CompilerTests
                .Compiler()
                .Bind(
                    CompilerTests.Capture(
                        CompilerTests.Definition(
                            CompilerTests.Rule(
                                "private rule",
                                Effect.Allow,
                                CompilerTests.Leaf("private field", "eq", "private literal")
                            )
                        )
                    ),
                    CompilerTests.Context(),
                    "scope"
                )
        );
        Assert.Empty(error.Diagnostics);
        Assert.IsType<InvalidRequestException>(error.InnerException);
        Assert.DoesNotContain("private", error.Message);
    }
}
