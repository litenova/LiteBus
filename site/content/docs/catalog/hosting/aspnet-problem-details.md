# ASP.NET Core Problem Details

## Header

- **ID**: `hosting.aspnet-problem-details`
- **Name**: ASP.NET Core problem details
- **Maturity**: GA
- **Summary**: Maps mediation refusals to RFC 9457 problem details through `IRefusalProblemCatalog`, with per-member validation errors on invalid results and an exception handler for the throwing send path.

## What It Does

`LiteBus.Extensions.AspNetCore` answers a mediation refusal with the status code and title the refusal code maps to in an `IRefusalProblemCatalog`. An endpoint that sends through `TrySendAsync` answers with `ToHttpResult`. An endpoint that sends through the throwing `SendAsync` relies on `LiteBusExceptionHandler`, which maps `LiteBusMessageDeniedException` and `LiteBusMessageInvalidException` through the same catalog to the same problem details. Both paths build the response in one shared mapping: the catalog status and title, the detail sentence, a stable `urn:litebus:` type derived from the code, the request path as instance, the request trace identifier as `traceId`, and, for an invalid result that carries failures, an `errors` extension keyed by member.

```csharp
services.AddLiteBusExceptionHandler();
app.UseExceptionHandler();

endpoints.MapPost("/orders", async (HttpContext context, ICommandMediator mediator, PlaceOrder command, CancellationToken cancellationToken) =>
{
    var result = await mediator.TrySendAsync(command, cancellationToken: cancellationToken).ConfigureAwait(false);
    return result.ToHttpResult(context, () => Results.Ok());
});
```

An application contributes its own codes with `Map` on a catalog built by `RefusalProblemCatalog.CreateDefault` and registers that instance in request services, so both paths answer product-specific refusals with the status the product chooses.

## Public Surface

### Result Mapping

- `IResult ToHttpResult(this MediationResult result, HttpContext httpContext, Func<IResult> onSuccess, IRefusalProblemCatalog? catalog = null)`
- `IResult ToHttpResult<TMessageResult>(this MediationResult<TMessageResult> result, HttpContext httpContext, Func<TMessageResult?, IResult> onSuccess, IRefusalProblemCatalog? catalog = null)`

When the mediation ran or was answered from the store, the helper runs the `onSuccess` callback. Otherwise it resolves the refusal code through the catalog and writes the problem response. The catalog resolves in order: the caller-supplied catalog, the catalog registered in request services, and `RefusalProblemCatalog.Default` as the fallback.

### Validation Errors

- `errors` extension: `IDictionary<string, string[]>` keyed by `ValidationFailure` member, with an empty string for a message-level failure

The shape matches ASP.NET Core's `ValidationProblemDetails.Errors`. The catalog status, title, and type stay as mapped. The extension appears only on an `Invalid` outcome that carries failures; a denial and an invalid result without failures omit it.

### Exception Handling

- `LiteBusExceptionHandler` (`IExceptionHandler` implementation)
- `IServiceCollection AddLiteBusExceptionHandler(this IServiceCollection services, Action<LiteBusExceptionHandlerOptions>? configure = null)`

The handler resolves the refusal code through the catalog read from request services with `RefusalProblemCatalog.Default` as the fallback, and writes through `IProblemDetailsService` when the host registered it with `AddProblemDetails`; otherwise it writes the same shape directly. Every other exception returns `false` without writing, leaving it for the next handler. For an invalid exception the reason joins every failure message and the code is the single failure's code when exactly one failure carries one, otherwise no code, which selects the catalog fallback mapping while the `errors` extension still carries every failure.

### Catalog Resolution

- `bool TryResolve(string? code, out RefusalProblemMap map)` on `IRefusalProblemCatalog`

`RefusalProblemCatalog` returns `false` for the fallback path: a null, empty, or blank code, or a code with no exact, prefix, or custom mapping. The default interface member calls `Resolve` and returns `true` when the code is non-null, so existing implementers keep compiling; it cannot tell a fallback from a match.

### Configuration

- `LiteBusExceptionHandlerOptions.UnmappedRefusalCode` (default `UnmappedRefusalCode.UseFallback`)
- `UnmappedRefusalCode.UseFallback`: answer the catalog fallback (`400 Bad Request` with the refusal detail)
- `UnmappedRefusalCode.TreatAsServerError`: answer `500 Internal Server Error` and log the unmapped code at error level

A refusal without a code always uses the fallback, because there is no mapping to miss.

## Packages

- `LiteBus.Extensions.AspNetCore`

## Requires

- `hosting.add-lite-bus-microsoft-di` or `hosting.add-lite-bus-autofac`
- `app.UseExceptionHandler()` for the exception path; without the middleware the handler never runs

## Invariants

- `ToHttpResult` and `LiteBusExceptionHandler` write the same problem for the same refusal: status, title, detail, type, instance, trace identifier, and errors extension are built in one shared mapping.
- An exact mapping wins over a prefix pattern, and the longest prefix wins when several patterns cover one code.
- A code ending in `.*` is a prefix pattern covering every code under that prefix, so `authorization.*` covers `authorization.forbidden`. A prefix pattern does not match its own bare prefix, and an exact code does not match longer codes that start with it.
- `TryResolve` receives only the code: null, empty, and whitespace codes all take the fallback path and return `false`.
- `TreatAsServerError` keeps the refusal detail and the code-derived problem type; only the status reports a server error.
- `RefusalProblemCatalog.Default` is shared by the process. Mapping on it changes answers process-wide, so an application maps on a catalog built by `CreateDefault` and registers that instance.

## Non-Goals

- Mapping faults the pipeline did not classify. Only a registered fault refusal classifier turns a storage fault into a refusal with a code.
- Client-side rendering of the `errors` extension.
- Exporter setup for logged unmapped codes beyond the error-level log entry.

## Observability

The result path emits no telemetry; refusal mapping is visible through the HTTP problem responses it returns. The exception handler logs an unmapped code at error level only when `TreatAsServerError` is configured, with the code as a log property and the mediation exception attached.

## Test Coverage

### Covered Use Cases

#### `MediationResultValidationErrorsTests.ToHttpResult_InvalidWithFieldFailures_WritesErrorsExtension`

- **Use case**: invalid result with per-member failures carries the errors extension
- **Test kind**: Unit
- **Description**: maps an invalid result with field failures through `ToHttpResult`
- **Behavior**: writes the problem response
- **Expected outcome**: `errors` keyed by member alongside the catalog status, title, and type
- **Remarks**: `tests/LiteBus.Extensions.UnitTests/AspNetCore/Problems/MediationResultValidationErrorsTests.cs`

#### `MediationResultValidationErrorsTests.ToHttpResult_Denied_OmitsErrorsExtension`

- **Use case**: denial omits the errors extension
- **Test kind**: Unit
- **Description**: maps a denied result through `ToHttpResult`
- **Behavior**: writes the problem response
- **Expected outcome**: problem without an `errors` entry
- **Remarks**: `tests/LiteBus.Extensions.UnitTests/AspNetCore/Problems/MediationResultValidationErrorsTests.cs`

#### `MediationResultValidationErrorsTests.ToHttpResult_GenericInvalid_WritesErrorsExtension`

- **Use case**: result-producing invalid mediation carries the errors extension
- **Test kind**: Unit
- **Description**: maps an invalid `MediationResult<T>` through `ToHttpResult`
- **Behavior**: writes the problem response
- **Expected outcome**: `errors` keyed by member
- **Remarks**: `tests/LiteBus.Extensions.UnitTests/AspNetCore/Problems/MediationResultValidationErrorsTests.cs`

#### `LiteBusExceptionHandlerTests.TryHandleAsync_DeniedException_WritesSameProblemAsToHttpResult`

- **Use case**: denied exception writes the same problem as the result path
- **Test kind**: Unit
- **Description**: handles a `LiteBusMessageDeniedException` and maps the equivalent result
- **Behavior**: writes both problem responses
- **Expected outcome**: equal status, title, type, and detail on both
- **Remarks**: `tests/LiteBus.Extensions.UnitTests/AspNetCore/Problems/LiteBusExceptionHandlerTests.cs`

#### `LiteBusExceptionHandlerTests.TryHandleAsync_InvalidException_WritesSameProblemWithErrorsAsToHttpResult`

- **Use case**: invalid exception writes the same problem and errors as the result path
- **Test kind**: Unit
- **Description**: handles a `LiteBusMessageInvalidException` and maps the equivalent result
- **Behavior**: writes both problem responses
- **Expected outcome**: equal status, title, type, detail, and `errors` on both
- **Remarks**: `tests/LiteBus.Extensions.UnitTests/AspNetCore/Problems/LiteBusExceptionHandlerTests.cs`

#### `LiteBusExceptionHandlerTests.TryHandleAsync_OtherException_ReturnsFalseWithoutWriting`

- **Use case**: non-mediation exception is left for the next handler
- **Test kind**: Unit
- **Description**: handles an `InvalidOperationException`
- **Behavior**: runs the handler
- **Expected outcome**: returns `false` and writes nothing
- **Remarks**: `tests/LiteBus.Extensions.UnitTests/AspNetCore/Problems/LiteBusExceptionHandlerTests.cs`

#### `LiteBusExceptionHandlerTests.TryHandleAsync_WithoutProblemDetailsService_WritesProblemDirectly`

- **Use case**: handler writes the same shape without `IProblemDetailsService`
- **Test kind**: Unit
- **Description**: handles a mediation exception with no problem details service registered
- **Behavior**: writes the response directly
- **Expected outcome**: problem response with the mapped status and shape
- **Remarks**: `tests/LiteBus.Extensions.UnitTests/AspNetCore/Problems/LiteBusExceptionHandlerTests.cs`

#### `LiteBusExceptionHandlerTests.TryHandleAsync_UnmappedCodeWithTreatAsServerError_WritesInternalServerErrorAndLogs`

- **Use case**: unmapped code reported as a server error
- **Test kind**: Unit
- **Description**: handles a denied exception with an unknown code and `TreatAsServerError`
- **Behavior**: writes the problem response
- **Expected outcome**: `500 Internal Server Error` with the unmapped code logged at error level
- **Remarks**: `tests/LiteBus.Extensions.UnitTests/AspNetCore/Problems/LiteBusExceptionHandlerTests.cs`

#### `LiteBusExceptionHandlerTests.TryHandleAsync_NullCodeWithTreatAsServerError_UsesFallback`

- **Use case**: refusal without a code keeps the fallback under `TreatAsServerError`
- **Test kind**: Unit
- **Description**: handles a refusal with no code and `TreatAsServerError`
- **Behavior**: writes the problem response
- **Expected outcome**: catalog fallback mapping, not a server error
- **Remarks**: `tests/LiteBus.Extensions.UnitTests/AspNetCore/Problems/LiteBusExceptionHandlerTests.cs`

#### `RefusalProblemTryResolveTests.TryResolve_PrefixHit_ReturnsTrueWithMapping`

- **Use case**: prefix mapping resolves as a match
- **Test kind**: Unit
- **Description**: resolves a code covered by a `.*` pattern
- **Behavior**: calls `TryResolve` on the default catalog
- **Expected outcome**: `true` with the prefix mapping
- **Remarks**: `tests/LiteBus.Extensions.UnitTests/AspNetCore/Problems/RefusalProblemTryResolveTests.cs`

#### `RefusalProblemTryResolveTests.TryResolve_FallbackPath_ReturnsFalseWithFallbackMapping`

- **Use case**: unknown, null, and blank codes report the fallback path
- **Test kind**: Unit
- **Description**: resolves codes with no mapping, including null and blank inputs
- **Behavior**: calls `TryResolve` on the default catalog
- **Expected outcome**: `false` with the `400 Bad Request` fallback mapping
- **Remarks**: `tests/LiteBus.Extensions.UnitTests/AspNetCore/Problems/RefusalProblemTryResolveTests.cs`

#### `RefusalProblemTryResolveTests.TryResolve_DefaultInterfaceMember_NonNullCodeReturnsTrueEvenForFallback`

- **Use case**: default member keeps existing implementers compiling
- **Test kind**: Unit
- **Description**: calls `TryResolve` on an implementer that does not override it
- **Behavior**: runs the default interface member
- **Expected outcome**: `true` for a non-null code even when the mapping is the fallback
- **Remarks**: `tests/LiteBus.Extensions.UnitTests/AspNetCore/Problems/RefusalProblemTryResolveTests.cs`

#### `LiteBusExceptionHandlerTests.AddLiteBusExceptionHandler_HonoursConfiguredOption`

- **Use case**: registration applies the configured option
- **Test kind**: Unit
- **Description**: registers with a configuration action setting `TreatAsServerError`
- **Behavior**: builds the service collection
- **Expected outcome**: options singleton carries the configured value
- **Remarks**: `tests/LiteBus.Extensions.UnitTests/AspNetCore/Problems/LiteBusExceptionHandlerTests.cs`

### Untested Use Cases

| Gap | Priority | Notes |
| --- | --- | --- |
| Custom `IProblemDetailsService` customization flowing through the handler | Low | The handler delegates to the registered service; customization behavior belongs to the ASP.NET Core writer. |

### Out-of-Scope Use Cases

- Client-side retry policy for `409 Conflict` responses.
- Dashboards over logged unmapped codes.

## References

- [ASP.NET Core Problem Details](../../integrations/aspnetcore-problem-details.md)
- [ASP.NET Core Execution Identity](../../integrations/aspnetcore-execution-identity.md)
- [ASP.NET Core Idempotency Key](../../integrations/aspnetcore-idempotency-key.md)
