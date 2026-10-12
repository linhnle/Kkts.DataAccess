using System.Security.Claims;
using System.Text.Json;
using Kkts.DataAccess;
using Microsoft.EntityFrameworkCore;

namespace EmployeeApp;

public class Employee
{
    public int Id { get; set; }
    public int TenantId { get; set; }
    public int DepartmentId { get; set; }
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public string Status { get; set; } = "Active";
    public decimal Salary { get; set; }
}

public class EmployeeDatabase(DbContextOptions<EmployeeDatabase> options) : DbContext(options)
{
    public DbSet<Employee> Employees => Set<Employee>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<Employee>().Property(e => e.Id).ValueGeneratedNever();
        model.Entity<Employee>().Property(e => e.Salary).HasPrecision(18, 2);
    }
}

public static class EmployeeConfiguration
{
    public static readonly ResourceSchema<Employee> Schema = new(
        "Employee",
        "1",
        [
            ResourceSchema<Employee>.Field("Id", e => e.Id, [FieldOperation.Select]),
            ResourceSchema<Employee>.Field("TenantId", e => e.TenantId, []),
            ResourceSchema<Employee>.Field(
                "DepartmentId",
                e => e.DepartmentId,
                [FieldOperation.Filter]
            ),
            ResourceSchema<Employee>.Field(
                "Name",
                e => e.Name,
                [FieldOperation.Select, FieldOperation.Sort]
            ),
            ResourceSchema<Employee>.Field("Email", e => e.Email, [FieldOperation.Select]),
            ResourceSchema<Employee>.Field("Status", e => e.Status, [FieldOperation.Filter]),
            ResourceSchema<Employee>.Field("Salary", e => e.Salary, []),
        ],
        "Id"
    );
    public static readonly ContextRegistry Registry = new(
        new Dictionary<string, Type>
        {
            { "tenant", typeof(int) },
            { "user.id", typeof(int) },
            { "user.departmentId", typeof(int) },
        }
    );
    public static readonly Condition Mandatory = new()
    {
        Field = "TenantId",
        Operator = "eq",
        Context = "tenant",
    };

    public static PolicyDefinition InitialPolicy() =>
        new(
            "Employee",
            "1",
            [
                new(
                    "manager-active-department",
                    "Employee",
                    "Read",
                    Effect.Allow,
                    new(["manager"], []),
                    new()
                    {
                        All =
                        [
                            new()
                            {
                                Field = "DepartmentId",
                                Operator = "eq",
                                Context = "user.departmentId",
                            },
                            new()
                            {
                                Field = "Status",
                                Operator = "eq",
                                Literal = JsonSerializer.SerializeToElement("Active"),
                            },
                        ],
                    }
                ),
                new(
                    "hr-active",
                    "Employee",
                    "Read",
                    Effect.Allow,
                    new(["hr"], []),
                    new()
                    {
                        Field = "Status",
                        Operator = "eq",
                        Literal = JsonSerializer.SerializeToElement("Active"),
                    }
                ),
                new(
                    "employee-self",
                    "Employee",
                    "Read",
                    Effect.Allow,
                    new(["employee"], []),
                    new()
                    {
                        Field = "Id",
                        Operator = "eq",
                        Context = "user.id",
                    }
                ),
            ],
            [
                new(
                    "public-fields",
                    "Employee",
                    "Read",
                    Effect.Allow,
                    new(["manager", "hr", "employee"], []),
                    new()
                    {
                        { FieldOperation.Select, ["Id", "Name", "Email"] },
                        { FieldOperation.Filter, ["DepartmentId", "Status"] },
                        { FieldOperation.Sort, ["Name"] },
                    }
                ),
            ]
        );

    public static TrustedContext Context(ClaimsPrincipal user)
    {
        if (user.Identity?.IsAuthenticated != true)
            throw new AuthorizationException("Authentication required.");
        return new(
            user.FindAll(ClaimTypes.Role).Select(c => c.Value),
            [],
            new Dictionary<string, object?>
            {
                { "tenant", int.Parse(user.FindFirstValue("tenant")!) },
                { "user.id", int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!) },
                { "user.departmentId", int.Parse(user.FindFirstValue("department")!) },
            }
        );
    }

    public static string Scope(ClaimsPrincipal user) => "tenant-" + Context(user).Values["tenant"];

    public static AdministrationContext Admin(ClaimsPrincipal user) =>
        new(user.Identity?.Name ?? "", Scope(user), user.IsInRole("admin"));

    public static IEnumerable<Employee> Fixtures(int tenant) =>
        [
            new()
            {
                Id = tenant * 100 + 1,
                TenantId = tenant,
                DepartmentId = 10,
                Name = "Alice",
                Email = "alice@example.test",
                Salary = 100,
            },
            new()
            {
                Id = tenant * 100 + 2,
                TenantId = tenant,
                DepartmentId = 10,
                Name = "Bob",
                Email = "bob@example.test",
                Salary = 200,
            },
            new()
            {
                Id = tenant * 100 + 3,
                TenantId = tenant,
                DepartmentId = 20,
                Name = "Carol",
                Email = "carol@example.test",
                Salary = 300,
            },
            new()
            {
                Id = tenant * 100 + 4,
                TenantId = tenant,
                DepartmentId = 10,
                Name = "Inactive",
                Email = "inactive@example.test",
                Status = "Inactive",
                Salary = 400,
            },
        ];
}
