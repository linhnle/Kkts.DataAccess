using EmployeeApp;
using Xunit;

namespace Kkts.DataAccess.Tests;

public class ExamplePolicyTests
{
    [Fact]
    public void Documented_policy_compiles_for_the_expected_employee_matrix()
    {
        var file = Path.GetFullPath(
            "../../../../../docs/employee-policy.json",
            AppContext.BaseDirectory
        );
        var documented = PolicyJson.Deserialize(File.ReadAllText(file));
        Assert.Equal(
            PolicyJson.Serialize(EmployeeConfiguration.InitialPolicy()),
            PolicyJson.Serialize(documented)
        );
        var compiler = new PolicyCompiler<Employee>(
            EmployeeConfiguration.Schema,
            EmployeeConfiguration.Registry,
            EmployeeConfiguration.Mandatory
        );
        foreach (var (role, expected) in new[] { ("manager", 2), ("hr", 3), ("employee", 1) })
        {
            var trusted = new TrustedContext(
                [role],
                [],
                new Dictionary<string, object?>
                {
                    { "tenant", 1 },
                    { "user.id", 101 },
                    { "user.departmentId", 10 },
                }
            );
            var bound = compiler.Bind(
                new("tenant-1", "Employee", "1", 1, PolicyJson.Serialize(documented)),
                trusted,
                "tenant-1"
            );
            Assert.Equal(
                expected,
                EmployeeConfiguration
                    .Fixtures(1)
                    .Concat(EmployeeConfiguration.Fixtures(2))
                    .Count(bound.Predicate.Compile())
            );
            Assert.Equal(
                new[] { "Email", "Id", "Name" },
                bound.Fields[FieldOperation.Select].Order()
            );
        }
    }
}
