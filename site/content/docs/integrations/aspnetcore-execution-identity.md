# ASP.NET Core Execution Identity

`LiteBus.Extensions.AspNetCore` provides `HttpContextExecutionIdentityProvider`, which supplies the LiteBus execution identity from the current HTTP request. The mediator reads it when a mediation carries no explicit identity, so guards, audit actor resolution, and tenancy checks observe the request caller without each endpoint copying the principal into mediation settings. The package also maps mediation refusals to RFC 9457 problem details through `IRefusalProblemCatalog`.

For the resolution order and what an identity carries, see [Execution Context](../concepts/execution-context.md). For declaring which messages require a tenant, see [Tenancy](../concepts/tenancy.md).

## Register

To resolve the identity from the current request:

1. Reference `LiteBus.Extensions.AspNetCore`.
2. Call `AddLiteBusExecutionIdentity`, optionally naming the tenant claim.
3. Send through `TrySendAsync` and answer with `ToHttpResult`:

```csharp
services.AddLiteBusExecutionIdentity(options =>
{
    options.TenantClaimTypes = ["tenant_id"];
});

services.AddLiteBus(builder =>
{
    builder.AddMessaging(messaging => messaging.UseTenancy());
    builder.AddCommands(commands =>
    {
        commands.RegisterFromAssembly(typeof(PlaceOrderCommand).Assembly);
    });
});

endpoints.MapPost("/orders", async (HttpContext context, ICommandMediator mediator, PlaceOrderCommand command, CancellationToken cancellationToken) =>
{
    var result = await mediator.TrySendAsync(command, cancellationToken: cancellationToken).ConfigureAwait(false);
    return result.ToHttpResult(context, () => Results.Ok());
});
```

`AddLiteBusExecutionIdentity` registers the HTTP context accessor, the configured `ExecutionIdentityOptions` as a singleton, and the provider as scoped, so each request resolves the identity of its own principal. For a result-producing command, pass the value through with `result.ToHttpResult(context, value => Results.Ok(value))`. A repeat answered from the idempotency store then receives the recorded result.

## How the Identity Is Read

The provider reads `HttpContext.User` as the calling principal and the tenant from the first claim type named by `ExecutionIdentityOptions.TenantClaimTypes` that carries a non-blank value, which defaults to a single `tenant_id` entry. When no HTTP context is available, the identity is `ExecutionIdentity.Anonymous`.

The principal is passed through as the request carries it, including an unauthenticated principal, so authorization policies observe the caller rather than a filtered copy. A missing tenant claim means no tenant: the identity reports `HasTenant` as false, and a message declaring a required tenant is denied with `tenancy.required`. Only the tenant travels onto durable envelopes. A principal is never persisted, so a reaction restored from an envelope runs in the tenant of the fact with no caller.

## Options

`AddLiteBusExecutionIdentity` accepts an optional configuration action over `ExecutionIdentityOptions`. `TenantClaimTypes` names the claim types that carry the tenant identifier, in the order they are tried, and defaults to a single `tenant_id` entry. The provider reads the first entry carrying a non-blank value.

```csharp
services.AddLiteBusExecutionIdentity(options =>
{
    options.TenantClaimTypes = ["workspace_id"];
});
```

The list is validated at registration: a null list, an empty list, or a blank entry throws from `AddLiteBusExecutionIdentity` rather than on the first request. An empty list would silently drop tenants present in claims, so it is a configuration fault, not an empty result.

## Dispatch Scope and Request-Scoped Services

The mediator resolves the provider from the dispatch scope, which is a sibling of the request scope rather than a child of it. A service the request scope holds, such as a principal holder the application fills during authentication, resolves as a fresh instance with no state inside the provider. An implementation that reads such a holder compiles and always observes the default, so every request silently resolves as `Anonymous`.

This registration bridges through `IHttpContextAccessor` instead: the provider reads the principal from `HttpContext.User` and the tenant from the first claim type named by `TenantClaimTypes` that carries a non-blank value, both of which stay available outside the request scope. An application that keeps the caller in its own request-scoped holder stamps the same values onto claims or the HTTP context so the provider reads them there, rather than reading the holder from the provider.

## Transient Principals on Sign-In Paths

The provider passes `HttpContext.User` through unfiltered, including a transient principal the request carries only during sign-in, such as the principal on an OpenID Connect callback path. A mediation started from such an endpoint would attribute the work to that transient caller, so those endpoints pass an explicit identity on the mediation settings instead:

```csharp
endpoints.MapPost("/sign-in/complete", async (HttpContext context, ICommandMediator mediator, CompleteSignInCommand command, CancellationToken cancellationToken) =>
{
    var settings = new CommandMediationSettings { Identity = ExecutionIdentity.Anonymous };
    var result = await mediator.TrySendAsync(command, settings, cancellationToken).ConfigureAwait(false);
    return result.ToHttpResult(context, () => Results.Ok());
});
```

Forgetting the explicit identity is silent: the mediation runs as the transient caller with no error.

## Tenants From More Than One Claim

A host that accepts a tenant from either of two claims, such as a session claim for cookie requests and a token claim for bearer requests, lists both claim types in the order they are tried. The provider reads the first entry carrying a non-blank value, so a blank first claim falls through to the next:

```csharp
services.AddLiteBusExecutionIdentity(options =>
{
    options.TenantClaimTypes = ["session_tenant", "tenant_id"];
});
```

A custom provider is needed only when the choice of tenant follows logic an ordered list cannot express. It still bridges through `IHttpContextAccessor` and claims, for the reason above.

## Refusal Mapping

`ToHttpResult` maps the `TrySendAsync` outcome to a response in one call. A mediation that ran or was answered from the store runs the `onSuccess` callback. A refusal resolves its code through an `IRefusalProblemCatalog` to a status code and title, and is written as an RFC 9457 problem carrying the reason the pipeline reported and a stable `urn:litebus:` type derived from the code, so one client handler reads every refusal the same way. Problem responses use the same shape as the management endpoints. They carry a stable `urn:litebus:` type, the request path as instance, and the request trace identifier as `traceId`.

The default catalog carries the refusal codes LiteBus reports:

| Code | Status | Title |
| --- | --- | --- |
| `declaration.missing` | 403 | Request denied |
| `authorization.not-signed-in` | 401 | Not signed in |
| `authorization.*` | 403 | Request denied |
| `tenancy.required` | 403 | Request denied |
| `validation.*` | 422 | Request validation failed |
| `concurrency.conflict` | 409 | Concurrency conflict |
| `idempotency.in-flight` | 409 | Idempotent request in flight |
| `idempotency.fingerprint-mismatch` | 422 | Idempotency key already used |
| anything else | 400 | Invalid request |

A code ending in `.*` is a prefix pattern covering every code under that prefix, so `authorization.*` covers `authorization.forbidden`. An exact mapping wins over a prefix pattern, and the longest prefix wins when several patterns cover one code. Codes without a mapping answer `400 Bad Request`. `tenancy.invalid`, reported when the mediation tenant is malformed under the registered validator, answers through that fallback as an invalid request, because the tenant value itself cannot be used.

An application contributes its own codes with `Map` and registers the catalog in request services, so `ToHttpResult` answers product-specific refusals with the status the product chooses:

```csharp
var catalog = RefusalProblemCatalog.CreateDefault();
catalog.Map("billing.insufficient-funds", StatusCodes.Status422UnprocessableEntity, "Insufficient funds");
services.AddSingleton<IRefusalProblemCatalog>(catalog);
```

`RefusalProblemCatalog.Default` is shared by the process. Map on a catalog built by `CreateDefault` and register that instance instead, so one host cannot change the answers of another. `ToHttpResult` resolves the catalog in order: the catalog the caller passes, the catalog registered in request services, and `Default` as the fallback. A caller-supplied catalog may also be passed per call for an endpoint whose refusals map differently from the rest of the host.

The problem type is `urn:litebus:` followed by the code with dots replaced by colons, such as `urn:litebus:tenancy:required`. A refusal that supplied no code uses `urn:litebus:mediation:invalid` for invalid input and `urn:litebus:mediation:denied` for a denial. An idempotency refusal keeps its own detail: a key another delivery is still applying answers `409 Conflict` so the client retries, and a key already used for a different request answers `422 Unprocessable Content` because retrying the same request cannot help. See [ASP.NET Core Idempotency Key](aspnetcore-idempotency-key.md).

## Limitations

The provider resolves for every mediation from the dispatch scope, not only for commands. A mediation that carries an explicit identity on its settings, or that runs nested inside another mediation, never reaches the provider, because the resolution order prefers both. A background reaction restored from an envelope carries the stored tenant and no caller, so an authorization policy that requires a signed-in principal denies it.

`RefusalProblemCatalog.Default` answers every host in the process. Extending the shared instance changes answers process-wide, so extend a catalog built by `CreateDefault` and register that instance. The catalog maps refusals only: a mediation that threw still propagates its exception, and only a registered fault refusal classifier turns a storage fault into a refusal with a code. See [Marten Unit of Work, Idempotency, and Audit](commands-marten.md) for the Marten classifier.

## Related Docs

- [Execution Context](../concepts/execution-context.md)
- [Tenancy](../concepts/tenancy.md)
- [ASP.NET Core Idempotency Key](aspnetcore-idempotency-key.md)
