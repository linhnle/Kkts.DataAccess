using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using EmployeeApp;
using Kkts.DataAccess;
using Kkts.DataAccess.Sqlite;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder
    .Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.LoginPath = "/login";
        o.Events.OnRedirectToLogin = c =>
        {
            c.Response.StatusCode = 401;
            return Task.CompletedTask;
        };
        o.Events.OnRedirectToAccessDenied = c =>
        {
            c.Response.StatusCode = 403;
            return Task.CompletedTask;
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddAntiforgery();
var connection = builder.Configuration["Database"] ?? "Data Source=employee.db";
var provider = builder.Configuration["EntityProvider"] ?? "Sqlite";
if (provider is not ("Sqlite" or "SqlServer" or "MySql"))
    throw new InvalidOperationException("Unknown EntityProvider. Use Sqlite, SqlServer or MySql.");
var entityConnection =
    builder.Configuration["EntityDatabase"]
    ?? (
        provider == "Sqlite"
            ? connection
            : throw new InvalidOperationException(
                "EntityDatabase is required for the selected provider."
            )
    );
var policyConnection = builder.Configuration["PolicyDatabase"] ?? connection;
var optionsBuilder = new DbContextOptionsBuilder<EmployeeDatabase>();
string providerIdentity;
switch (provider)
{
    case "Sqlite":
        optionsBuilder.UseSqlite(entityConnection);
        providerIdentity = "Microsoft.EntityFrameworkCore.Sqlite";
        break;
    case "SqlServer":
        optionsBuilder.UseSqlServer(entityConnection);
        providerIdentity = "Microsoft.EntityFrameworkCore.SqlServer";
        break;
    case "MySql":
        optionsBuilder.UseMySQL(entityConnection);
        providerIdentity = "MySql.EntityFrameworkCore";
        break;
    default:
        throw new InvalidOperationException(
            "Unknown EntityProvider. Use Sqlite, SqlServer or MySql."
        );
}
var options = optionsBuilder.Options;
var entityAdapter = Kkts.DataAccess.EntityFrameworkCore.EfEntityReadAdapter<Employee>.Register(
    () => new EmployeeDatabase(options),
    providerIdentity
);
var store = new SqlitePolicyStore(policyConnection);
var compiler = new PolicyCompiler<Employee>(
    EmployeeConfiguration.Schema,
    EmployeeConfiguration.Registry,
    EmployeeConfiguration.Mandatory
);
var app = builder.Build();
app.Use(
    async (http, next) =>
    {
        try
        {
            await next();
        }
        catch (Exception ex)
            when (ex
                    is AuthorizationException
                        or PolicyConflictException
                        or JsonException
                        or AntiforgeryValidationException
                        or BadHttpRequestException
            )
        {
            http.Response.StatusCode =
                ex is AuthorizationException ? 403
                : ex is PolicyConflictException ? 409
                : 400;
            await http.Response.WriteAsJsonAsync(new { error = ex.Message });
        }
    }
);
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
await using (var db = new EmployeeDatabase(options))
{
    await db.Database.EnsureCreatedAsync();
    if (!await db.Employees.AnyAsync())
    {
        db.AddRange(EmployeeConfiguration.Fixtures(1).Concat(EmployeeConfiguration.Fixtures(2)));
        await db.SaveChangesAsync();
    }
}
await store.InitializeAsync();
foreach (var tenant in new[] { 1, 2 })
{
    var admin = new AdministrationContext("seed", "tenant-" + tenant, true);
    if ((await store.ReadDraftAsync(admin, admin.Scope, "Employee")).DraftRevision == 0)
        await store.SaveDraftAsync(
            admin,
            admin.Scope,
            "Employee",
            PolicyJson.Serialize(EmployeeConfiguration.InitialPolicy()),
            0
        );
}

IAuthorizedReadRepository<Employee> Reads(HttpContext http) =>
    new AuthorizedReadRepository<Employee>(
        EmployeeConfiguration.Schema,
        compiler,
        store,
        entityAdapter,
        () => EmployeeConfiguration.Context(http.User),
        EmployeeConfiguration.Scope(http.User)
    );
string Token(HttpContext http) =>
    HtmlEncoder.Default.Encode(
        http
            .RequestServices.GetRequiredService<IAntiforgery>()
            .GetAndStoreTokens(http)
            .RequestToken!
    );
Task Protect(HttpContext http) =>
    http.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(http);
long Revision(IFormCollection form, string name) =>
    long.TryParse(form[name], out var value) && value >= 0
        ? value
        : throw new BadHttpRequestException("Invalid revision.");
app.MapGet("/", () => Results.Redirect("/admin"));
app.MapGet(
    "/login",
    (HttpContext http) =>
    {
        if (!app.Environment.IsDevelopment())
            return Results.NotFound();
        return Results.Content(
            "<h1>Synthetic demo identity</h1><p>Local development fixtures only.</p><form method='post'><input type='hidden' name='__RequestVerificationToken' value='"
                + Token(http)
                + "'><select name='profile'>"
                + string.Join(
                    "",
                    from tenant in new[] { 1, 2 }
                    from role in new[] { "admin", "manager", "hr", "employee", "none" }
                    select $"<option value='{tenant}:{role}'>Tenant {tenant} {role}</option>"
                )
                + "</select><button>Sign in</button></form>",
            "text/html"
        );
    }
);
app.MapPost(
    "/login",
    async (HttpContext http) =>
    {
        if (!app.Environment.IsDevelopment())
            return Results.NotFound();
        await Protect(http);
        var form = await http.Request.ReadFormAsync();
        var parts = form["profile"].ToString().Split(':');
        if (
            parts.Length != 2
            || !new[] { "1", "2" }.Contains(parts[0])
            || !new[] { "admin", "manager", "hr", "employee", "none" }.Contains(parts[1])
        )
            return Results.BadRequest();
        var tenant = int.Parse(parts[0]);
        var claims = new[]
        {
            new Claim(ClaimTypes.Name, $"demo-{tenant}-{parts[1]}"),
            new Claim(ClaimTypes.NameIdentifier, (tenant * 100 + 1).ToString()),
            new Claim("tenant", parts[0]),
            new Claim("department", "10"),
            new Claim(ClaimTypes.Role, parts[1]),
        };
        await http.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(
                new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)
            )
        );
        return Results.Redirect(parts[1] == "admin" ? "/admin" : "/employees");
    }
);
app.MapGet(
        "/employees",
        (Func<HttpContext, Task<IResult>>)(
            async (HttpContext http) =>
                Results.Json(await Reads(http).ListAsync(new(), http.RequestAborted))
        )
    )
    .RequireAuthorization();
app.MapPost(
        "/employees/list",
        async (HttpContext http, ReadRequest request) =>
        {
            await Protect(http);
            return await Reads(http).ListAsync(request, http.RequestAborted);
        }
    )
    .RequireAuthorization();
app.MapPost(
        "/employees/count",
        async (HttpContext http, ReadRequest request) =>
        {
            await Protect(http);
            return await Reads(http).CountAsync(request, http.RequestAborted);
        }
    )
    .RequireAuthorization();
app.MapGet(
        "/employees/{id:int}",
        async (HttpContext http, int id) =>
        {
            var detail = await Reads(http)
                .DetailAsync(JsonSerializer.SerializeToElement(id), new(), http.RequestAborted);
            return detail.Record == null ? Results.NotFound() : Results.Json(detail);
        }
    )
    .RequireAuthorization();
app.MapPost(
        "/employees/export",
        async (HttpContext http, ReadRequest request) =>
        {
            await Protect(http);
            http.Response.ContentType = "application/x-ndjson";
            await foreach (var batch in Reads(http).ExportAsync(request, http.RequestAborted))
            foreach (var row in batch.Records)
                await http.Response.WriteAsync(row.GetRawText() + "\n", http.RequestAborted);
        }
    )
    .RequireAuthorization();
app.MapGet(
        "/admin",
        async (HttpContext http) =>
        {
            var admin = EmployeeConfiguration.Admin(http.User);
            admin.Demand(admin.Scope);
            var draft = await store.ReadDraftAsync(
                admin,
                admin.Scope,
                "Employee",
                http.RequestAborted
            );
            var history = await store.HistoryAsync(
                admin,
                admin.Scope,
                "Employee",
                http.RequestAborted
            );
            return Results.Content(
                Designer.Render(
                    EmployeeConfiguration.Schema.Metadata(admin, admin.Scope),
                    draft,
                    history,
                    Token(http)
                ),
                "text/html"
            );
        }
    )
    .RequireAuthorization();
app.MapPost(
        "/admin/save",
        async (HttpContext http) =>
        {
            await Protect(http);
            var admin = EmployeeConfiguration.Admin(http.User);
            admin.Demand(admin.Scope);
            var form = await http.Request.ReadFormAsync();
            var payload = form["payload"].ToString();
            var policy = PolicyJson.Deserialize(payload);
            var errors = new PolicyValidator<Employee>(
                EmployeeConfiguration.Schema,
                EmployeeConfiguration.Registry
            ).Validate(policy);
            if (errors.Count != 0)
                return Results.BadRequest(new { diagnostics = errors });
            await store.SaveDraftAsync(
                admin,
                admin.Scope,
                "Employee",
                payload,
                Revision(form, "draftRevision"),
                http.RequestAborted
            );
            return Results.Redirect("/admin");
        }
    )
    .RequireAuthorization();
app.MapPost(
        "/admin/publish",
        async (HttpContext http) =>
        {
            await Protect(http);
            var admin = EmployeeConfiguration.Admin(http.User);
            var form = await http.Request.ReadFormAsync();
            await store.PublishAsync(
                admin,
                admin.Scope,
                EmployeeConfiguration.Schema,
                EmployeeConfiguration.Registry,
                Revision(form, "draftRevision"),
                Revision(form, "activeRevision"),
                cancellation: http.RequestAborted
            );
            return Results.Redirect("/admin");
        }
    )
    .RequireAuthorization();
app.MapPost(
        "/admin/rollback",
        async (HttpContext http) =>
        {
            await Protect(http);
            var admin = EmployeeConfiguration.Admin(http.User);
            var form = await http.Request.ReadFormAsync();
            await store.RollbackAsync(
                admin,
                admin.Scope,
                EmployeeConfiguration.Schema,
                EmployeeConfiguration.Registry,
                Revision(form, "version"),
                Revision(form, "activeRevision"),
                cancellation: http.RequestAborted
            );
            return Results.Redirect("/admin");
        }
    )
    .RequireAuthorization();
app.MapPost(
        "/admin/preview",
        async (HttpContext http) =>
        {
            await Protect(http);
            var admin = EmployeeConfiguration.Admin(http.User);
            admin.Demand(admin.Scope);
            var form = await http.Request.ReadFormAsync();
            var role = form["sampleRole"].ToString();
            if (!new[] { "manager", "hr", "employee", "none" }.Contains(role))
                return Results.BadRequest();
            var tenant = (int)EmployeeConfiguration.Context(http.User).Values["tenant"]!;
            var sample = new TrustedContext(
                [role],
                [],
                new Dictionary<string, object?>
                {
                    { "tenant", tenant },
                    { "user.id", tenant * 100 + 1 },
                    { "user.departmentId", 10 },
                }
            );
            var policy = PolicyJson.Deserialize(form["payload"].ToString());
            var captured = new CapturedPolicy(
                admin.Scope,
                "Employee",
                "1",
                long.MaxValue,
                PolicyJson.Serialize(policy)
            );
            // A fresh preview compiler avoids sharing synthetic content with published version caches.
            var previewCompiler = new PolicyCompiler<Employee>(
                EmployeeConfiguration.Schema,
                EmployeeConfiguration.Registry,
                EmployeeConfiguration.Mandatory
            );
            try
            {
                var bound = previewCompiler.Bind(captured, sample, admin.Scope);
                var predicate = bound.Predicate.Compile();
                return Results.Json(
                    new
                    {
                        label = "Synthetic fixtures only",
                        mandatory = "TenantId equals synthetic tenant",
                        bound.ApplicableRules,
                        fields = bound.Fields,
                        records = EmployeeConfiguration
                            .Fixtures(tenant)
                            .Concat(EmployeeConfiguration.Fixtures(tenant == 1 ? 2 : 1))
                            .Select(e => new
                            {
                                fixture = e.Id,
                                allowed = predicate(e),
                                matchedRules = policy
                                    .Rules.Where(r =>
                                        bound.ApplicableRules.Contains(r.Id)
                                        && previewCompiler
                                            .Predicate(r.Condition, sample)
                                            .Compile()(e)
                                    )
                                    .Select(r => new { r.Id, r.Effect }),
                            }),
                    }
                );
            }
            catch (AuthorizationException ex)
            {
                return Results.Json(
                    new
                    {
                        label = "Synthetic fixtures only",
                        denied = true,
                        error = ex.Message,
                    }
                );
            }
        }
    )
    .RequireAuthorization();
app.Run();

public partial class Program { }
