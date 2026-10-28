# ASP.NET Core Idempotency Key

`LiteBus.Extensions.AspNetCore` provides `WithIdempotencyKey`, an endpoint filter that reads the `Idempotency-Key` request header and keys the command the endpoint sends by it, so a client retry with the same key is answered from the idempotency store without running the handler again. The filter validates the header, the endpoint carries the key into mediation settings, a guard hands it to the idempotency shortcut as a `SuppliedIdempotencyKey`, and a result helper maps refusals to HTTP status codes.

For declaring keys and how the shortcut and completion handler settle them, see [In-Process Idempotency](../concepts/idempotency.md).

## Register

Reference `LiteBus.Extensions.AspNetCore` and `LiteBus.Commands`. Register the guard on the command module, enable idempotency with an `IIdempotencyStore`, add the filter to the endpoint, and send through `TrySendAsync`:

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

## Options

`WithIdempotencyKey` accepts an optional configuration action over `IdempotencyKeyFilterOptions`. `HeaderName` names the request header and defaults to `Idempotency-Key`. `RequireHeader` defaults to `true`: a missing or blank header short-circuits the endpoint with a `400 Bad Request` problem response without running it, and setting it to `false` lets the endpoint proceed without a key. `MaxKeyLength` defaults to 255 characters: a longer key short-circuits the endpoint with a `400 Bad Request` problem response. `TenantResolver` reads the tenant the key is scoped to from the current request and defaults to `null`, which leaves keys unscoped.

```csharp
group.WithIdempotencyKey(options =>
{
    options.HeaderName = "X-Request-Key";
    options.RequireHeader = false;
    options.MaxKeyLength = 128;
    options.TenantResolver = context => context.User.FindFirst("tenant_id")?.Value;
});
```

A multi-tenant endpoint sets `TenantResolver`. Without a tenant scope, two tenants sending one key string for one command share a store row, and one tenant's repeat is answered with the other's stored result whenever the fingerprints match or either side carries none. A blank or missing tenant resolves to no tenant scope, so single-tenant endpoints leave the resolver unset.

Option errors fail at startup: a blank header name and a non-positive length limit throw `ArgumentException` and `ArgumentOutOfRangeException` from `WithIdempotencyKey`, not on the first request.

## How The Key Reaches The Pipeline

The idempotency shortcut reads a context-supplied key from the execution-context data store, which only a pipeline stage can write. The integration bridges that gap in three steps: the filter stores the validated header value in `HttpContext.Items` for the duration of the endpoint invocation, `WithIdempotencyKey` on `CommandMediationSettings` copies it into the settings items the endpoint passes to the mediator, and `IdempotencyKeyGuard` runs in the guard stage, before the shortcut stage, and stores it as a `SuppliedIdempotencyKey`. A tenant the filter resolved travels the same two hops into the trace tenant entry, so the stored key is prefixed by the calling tenant.

The guard always allows and leaves a key another stage already supplied alone. A mediation that carries no key passes through untouched. The guard applies to every command indirectly, and indirect guards run before command-specific direct guards, so a command-specific guard that derives its own key runs after this one and keeps precedence only when it overwrites unconditionally with `Data.Set`: a guard that sets only when no key is present yet silently loses to the header.

## Refusal Mapping

`ToHttpResult` maps the `TrySendAsync` outcome to a response in one call. A mediation that ran or was answered from the store runs the `onSuccess` callback. A key another delivery is still applying answers `409 Conflict` so the client retries, and a key already used for a different request answers `422 Unprocessable Content` because retrying the same request cannot help. Any other denial answers `403 Forbidden` and a validation failure answers `400 Bad Request`, both carrying the reason the pipeline reported. Problem responses use the same shape as the management endpoints: a stable `urn:litebus:` type, the request path as instance, and the request trace identifier as `traceId`.

## Limitations

The filter keys commands, not queries or events: only a command declaring `IdempotencyDeclaration.KeyedByContext()` reads the header, and the guard is registered on the command module. Sending such a command without the filter, or without registering the guard, ends in `IdempotencyConfigurationException` from the shortcut, because no stage supplied the key it requires. A command declared with `KeyedByContext` that replays results should also set a fingerprint, or any repeat of a predictable key is answered with the stored result. The guard compares nothing by itself; duplicate detection is the store's claim, so two hosts need a shared `IIdempotencyStore` exactly as with any other idempotent command.

## Related Docs

- [In-Process Idempotency](../concepts/idempotency.md)
- [PostgreSQL Idempotency Store](commands-postgresql-idempotency-store.md)
