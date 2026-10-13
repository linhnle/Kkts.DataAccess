using System.Linq.Expressions;
using Kkts.Expressions;
using Xunit;

namespace Kkts.DataAccess.Tests;

public class ExpressionsCompatibilityTests
{
    public sealed class Row
    {
        public int Id { get; set; }
        public decimal Amount { get; set; }
        public string? Name { get; set; }
    }

    [Fact]
    public void Registered_selectors_typed_values_analysis_and_ordering_use_pinned_apis()
    {
        var parameter = Expression.Parameter(typeof(Row), "row");
        var selector = Expression.Lambda<Func<Row, decimal>>(
            Expression.Property(parameter, typeof(Row).GetProperty(nameof(Row.Amount))!),
            parameter
        );
        var context = new ExpressionQueryContext(
            new QuerySchema<Row>().Field("amount", selector).Field("name", r => r.Name).Build(),
            new QueryPolicy()
        );
        var variables = new ExpressionVariableSchema([
            ExpressionVariableDefinition.FromType("bound", typeof(decimal)),
        ]);
        var resolver = new VariableResolver();
        Assert.True(resolver.TryAdd("bound", 123456789.123456789m));
        var tree = FilterNode.Condition("amount", "=", FilterValue.Variable("bound"));
        Assert.True(context.ValidateFilterTree(tree, variables).Succeeded);
        var formatted = context.FormatFilterTree(tree, variables);
        Assert.True(formatted.Succeeded);
        Assert.True(context.AnalyzeExpression(formatted.Result, variables).IsSemanticallyValid);
        Assert.False(context.AnalyzeExpression("amount = 'bad'").IsSemanticallyValid);
        var predicate = context.BuildPredicate<Row>(tree, resolver).Compile();
        Assert.True(predicate(new Row { Amount = 123456789.123456789m }));
        Assert.False(predicate(new Row { Amount = 123456789.123456788m }));
        var rows = new[]
        {
            new Row { Id = 1, Name = "B" },
            new Row { Id = 3, Name = "A" },
            new Row { Id = 2, Name = "A" },
        };
        var ordered = rows.AsQueryable()
            .OrderBy(
                new[]
                {
                    new OrderByInfo { Property = "Name" },
                    new OrderByInfo { Property = "Id", Descending = true },
                }
            );
        Assert.Equal(new[] { 3, 2, 1 }, ordered.Select(r => r.Id));
    }
}
