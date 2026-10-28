# Idempotency-Key Endpoint Filter

## Header

- **ID**: `hosting.idempotency-key-endpoint-filter`
- **Name**: Idempotency-Key endpoint filter
- **Maturity**: GA
- **Summary**: Keys the command an ASP.NET Core endpoint sends by the `Idempotency-Key` request header, so a client retry is answered from the idempotency store without running the handler again.

## What It Does

`WithIdempotencyKey` is an endpoint filter from `LiteBus.Extensions.AspNetCore` that reads the configured request header before the endpoint runs. A missing required header and an over-long key short-circuit the endpoint with a `400 Bad Request` problem response; otherwise the validated key is stored in `HttpContext.Items` for the duration of the invocation, the endpoint carries it into mediation settings, a guard hands it to the idempotency shortcut as a `SuppliedIdempotencyKey`, and a result helper maps refusals to HTTP status codes.

```csharp
services.AddLiteBus(builder =>
{
    builder.AddMessaging(_ => { });
    builder.AddCommands(commands =>
    {
        commands.RegisterFromAssembly(typeof(PlaceOrder).Assembly);
        commands.Register<IdempotencyKeyGuard>();
        commands.EnableIdempotency();
    });
});

endpoints.MapPost("/orders", async (HttpContext context, ICommandMediator mediator, PlaceOrder command, CancellationToken cancellationToken) =>
{
    var settings = new CommandMediationSettings().WithIdempotencyKey(context);
    var result = await mediator.TrySendAsync(command, settings, cancellationToken).ConfigureAwait(false);
    return result.ToHttpResult(context, () => Results.Ok());
}).WithIdempotencyKey();
```

The command must declare `IdempotencyDeclaration.KeyedByContext()`, because the key arrives with the HTTP request rather than on the command. `WithIdempotencyKey` also extends route group builders, so one call covers every endpoint in a group. For a result-producing command, pass the value through: `result.ToHttpResult(context, value => Results.Ok(value))`, which replays the recorded result to a repeat.

## Public Surface

### Registration

- `TBuilder WithIdempotencyKey<TBuilder>(this TBuilder builder, Action<IdempotencyKeyFilterOptions>? configure = null) where TBuilder : IEndpointConventionBuilder`

### Configuration

- `IdempotencyKeyFilterOptions.HeaderName` (default `Idempotency-Key`)
- `IdempotencyKeyFilterOptions.RequireHeader` (default `true`): a missing or blank header short-circuits with `400 Bad Request`; `false` lets the endpoint proceed without a key
- `IdempotencyKeyFilterOptions.MaxKeyLength` (default 255): a longer key short-circuits with `400 Bad Request`
- `IdempotencyKeyFilterOptions.TenantResolver` (default `null`): reads the tenant the key is scoped to from the current request; a blank or missing tenant resolves to no tenant scope

### Pipeline Bridge

- `CommandMediationSettings.WithIdempotencyKey(HttpContext)` copies the validated header value from `HttpContext.Items` into the settings items the endpoint passes to the mediator; without the filter entry the settings pass through unchanged
- `IdempotencyKeyGuard` (`ICommandGuard<ICommand>`) runs in the guard stage, before the shortcut stage, and stores the carried key as a `SuppliedIdempotencyKey`

### Refusal Mapping

- `IResult ToHttpResult(this MediationResult result, HttpContext httpContext, Func<IResult> onSuccess)`
- `IResult ToHttpResult<TMessageResult>(this MediationResult<TMessageResult> result, HttpContext httpContext, Func<TMessageResult?, IResult> onSuccess)`

A mediation that ran or was answered from the store runs the `onSuccess` callback. A key another delivery is still applying answers `409 Conflict`, and a key already used for a different request answers `422 Unprocessable Content`. Any other denial answers `403 Forbidden` and a validation failure answers `400 Bad Request`, both carrying the reason the pipeline reported. Problem responses use the same shape as the management endpoints: a stable `urn:litebus:` type, the request path as instance, and the request trace identifier as `traceId`.

## Packages

- `LiteBus.Extensions.AspNetCore`

## Requires

- `hosting.add-lite-bus-microsoft-di` or `hosting.add-lite-bus-autofac`
- A command declaring `IdempotencyDeclaration.KeyedByContext()`
- `IdempotencyKeyGuard` registered on the command module
- Idempotency enabled with an `IIdempotencyStore`

## Invariants

- A blank header counts as missing; when several values arrive under one header name, the first value wins and the rest are ignored.
- Option errors fail at startup: a blank header name throws `ArgumentException` and a non-positive length limit throws `ArgumentOutOfRangeException` from `WithIdempotencyKey`, not on the first request.
- The guard always allows: a mediation that carries no key passes through untouched, and a key another stage already supplied is left alone. The guard is indirect and runs first, so a command-specific direct guard keeps precedence only when it overwrites unconditionally with `Data.Set`.
- A resolved tenant travels with the key into the mediation and prefixes the stored key, so two tenants sending one key string for one command do not share a store row.
- Without the guard the key never reaches the execution context, and the shortcut reports the missing key as a configuration error.

## Non-Goals

- Keying queries or events; only a command declaring `KeyedByContext` reads the header.
- Duplicate detection without a shared `IIdempotencyStore`; the guard compares nothing by itself.

## Observability

The filter emits no telemetry. Refusal mapping is visible through the HTTP problem responses it returns.

## Test Coverage

### Covered Use Cases

#### `IdempotencyKeyFilterTests.Send_WithSameKeyTwice_RunsHandlerOnce`

- **Use case**: client retry with the same key is answered from the store
- **Test kind**: Unit
- **Description**: sends one command twice through the filter with the same header value
- **Behavior**: executes the endpoint pipeline with an idempotency store
- **Expected outcome**: the handler runs once and the repeat is answered without running it
- **Remarks**: `tests/LiteBus.Extensions.UnitTests/AspNetCore/IdempotencyKeyFilterTests.cs`

#### `IdempotencyKeyFilterTests.Send_WithoutHeader_ReturnsBadRequest`

- **Use case**: missing required header short-circuits the endpoint
- **Test kind**: Unit
- **Description**: invokes the endpoint without the header and the default options
- **Behavior**: runs the filter
- **Expected outcome**: `400 Bad Request` without running the endpoint
- **Remarks**: `tests/LiteBus.Extensions.UnitTests/AspNetCore/IdempotencyKeyFilterTests.cs`

#### `IdempotencyKeyFilterTests.Send_WithTooLongKey_ReturnsBadRequest`

- **Use case**: over-long key short-circuits the endpoint
- **Test kind**: Unit
- **Description**: invokes the endpoint with a key longer than `MaxKeyLength`
- **Behavior**: runs the filter
- **Expected outcome**: `400 Bad Request` without running the endpoint
- **Remarks**: `tests/LiteBus.Extensions.UnitTests/AspNetCore/IdempotencyKeyFilterTests.cs`

#### `IdempotencyKeyFilterTests.Send_WhenClaimInFlight_ReturnsConflict`

- **Use case**: key another delivery is still applying answers retryable conflict
- **Test kind**: Unit
- **Description**: sends while a first delivery holds the claim
- **Behavior**: maps the mediation result through `ToHttpResult`
- **Expected outcome**: `409 Conflict` so the client retries
- **Remarks**: `tests/LiteBus.Extensions.UnitTests/AspNetCore/IdempotencyKeyFilterTests.cs`

#### `IdempotencyKeyFilterTests.Send_WhenFingerprintMismatches_ReturnsUnprocessableContent`

- **Use case**: key reused for a different request answers unprocessable
- **Test kind**: Unit
- **Description**: sends a different request under an already-claimed key
- **Behavior**: maps the mediation result through `ToHttpResult`
- **Expected outcome**: `422 Unprocessable Content`, because retrying the same request cannot help
- **Remarks**: `tests/LiteBus.Extensions.UnitTests/AspNetCore/IdempotencyKeyFilterTests.cs`

### Untested Use Cases

| Gap | Priority | Notes |
| --- | --- | --- |
| Route-group registration covering several endpoints with one call | Low | The filter targets `IEndpointConventionBuilder`, which both route and group builders implement. |

### Out-of-Scope Use Cases

- Client-side retry policy for `409 Conflict` responses.

## Deep Docs

- [ASP.NET Core Idempotency Key](../../integrations/aspnetcore-idempotency-key.md)
- [In-Process Idempotency](../../concepts/idempotency.md)
