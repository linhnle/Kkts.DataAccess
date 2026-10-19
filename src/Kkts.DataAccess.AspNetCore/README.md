# Kkts.DataAccess.AspNetCore

ASP.NET Core Problem Details translation for authorization and read errors.

## When to use

Use this optional package in HTTP hosts to translate Kkts.DataAccess failures
into bounded, safe `application/problem+json` responses.
It does not create read endpoints, configure repositories, authenticate clients,
or enforce policies merely by being installed.
Non-HTTP applications can handle core's typed exceptions without this package.

## Requirements and installation

Targets .NET 10 and requires the `Microsoft.AspNetCore.App` shared framework.
NuGet installs core transitively. Use an ASP.NET Core host, normally with the
`Microsoft.NET.Sdk.Web` SDK.

```sh
dotnet add package Kkts.DataAccess.AspNetCore
```

## Quick start

In an ASP.NET Core application's `Program.cs`:

```csharp
using Kkts.DataAccess.AspNetCore;
using Microsoft.AspNetCore.Builder;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddDataAccessProblemDetails();
var app = builder.Build();
app.UseDataAccessProblemDetails();
app.Run();
```

Register your application services before `Build` and map your own authenticated
endpoints after the middleware registration and before `Run`. The minimal host
above intentionally has no endpoints or database configuration.

## Behavior and responsibilities

- Registration enables strict minimal-API bad-request exceptions. Middleware
  handles malformed transport input and library failures without serializing
  internal exception messages, SQL, tokens, or record values into responses.
- Responses include `type`, `title`, `status`, `code`, and an opaque `traceId`;
  invalid requests also include bounded safe diagnostics with JSON Pointer paths.
- Invalid requests/continuations map to 400, access denial/policy changes to 403,
  revision/schema conflicts to 409, unsupported providers to 422, missing policy
  to 503, and export duration expiry to 504. Unexpected exceptions map to
  500/`internal_error`.
- Middleware logs failures internally. Hosts own access to logs, authentication,
  endpoint authorization, and administrator-only policy operations.
- Caller cancellation does not invent a response body. After response start,
  failures abort the connection rather than append Problem Details to a stream.
  Downloaded prefixes are not retractable; interrupted exports are incomplete.
- Startup configuration errors still stop startup; they are not HTTP input errors.

## Related packages and documentation

- [Error catalog and response contract](https://github.com/linhnle/Kkts.DataAccess/blob/main/docs/errors.md).
- [Core](https://github.com/linhnle/Kkts.DataAccess/blob/main/src/Kkts.DataAccess/README.md)
  and [optional DI integration](https://github.com/linhnle/Kkts.DataAccess/blob/main/src/Kkts.DataAccess.DependencyInjection/README.md).
- [Export lifecycle](https://github.com/linhnle/Kkts.DataAccess/blob/main/docs/reads.md)
  and [working HTTP host](https://github.com/linhnle/Kkts.DataAccess/tree/main/examples/EmployeeApp).
