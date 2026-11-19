# ASP.NET Core Problem Details

`LiteBus.Extensions.AspNetCore` maps mediation refusals to RFC 9457 problem details through `IRefusalProblemCatalog`. An endpoint that sends through `TrySendAsync` answers with `ToHttpResult`, and an endpoint that sends through the throwing `SendAsync` relies on `LiteBusExceptionHandler`, so both paths write the same problem shape. An invalid result carries its validation failures as an `errors` extension keyed by member, in the same shape as `ValidationProblemDetails.Errors`, while the catalog status, title, and type stay as mapped.

For the default catalog mappings and how an application contributes its own codes, see [ASP.NET Core Execution Identity](aspnetcore-execution-identity.md). For keying a command by the request header on the same endpoints, see [ASP.NET Core Idempotency Key](aspnetcore-idempotency-key.md).

## Register

To answer refusals as problem details from both the result path and the exception path:

1. Reference `LiteBus.Extensions.AspNetCore` and `LiteBus.Commands`.
2. Register the exception handler and enable the ASP.NET Core exception-handling middleware.
3. Optionally call `AddProblemDetails` to customize the problem writer.
4. Send through `TrySendAsync` and answer with `ToHttpResult`:

```csharp
services.AddProblemDetails();
services.AddLiteBusExceptionHandler();

services.AddLiteBus(builder =>
{
    builder.AddMessaging(_ => { });
    builder.AddCommands(commands =>
    {
        commands.RegisterFromAssembly(typeof(PlaceOrder).Assembly);
    });
});

app.UseExceptionHandler();

endpoints.MapPost("/orders", async (HttpContext context, ICommandMediator mediator, PlaceOrder command, CancellationToken cancellationToken) =>
{
    var result = await mediator.TrySendAsync(command, cancellationToken: cancellationToken).ConfigureAwait(false);
    return result.ToHttpResult(context, () => Results.Ok());
});
```

`AddLiteBusExceptionHandler` registers the configured `LiteBusExceptionHandlerOptions` as a singleton and the handler with `AddExceptionHandler`, so `UseExceptionHandler` routes mediation exceptions through the refusal catalog. For a result-producing command, pass the value through with `result.ToHttpResult(context, value => Results.Ok(value))`.

## Validation Errors

An `Invalid` result adds an `errors` extension: an `IDictionary<string, string[]>` keyed by `ValidationFailure` member, with an empty string for a message-level failure. The shape matches ASP.NET Core's `ValidationProblemDetails.Errors`, so a client that already renders field errors reads it the same way. The catalog status, title, and type stay as mapped.

```json
{
  "status": 422,
  "title": "Request validation failed",
  "type": "urn:litebus:validation:order:quantity",
  "errors": {
    "Quantity": ["Quantity must be positive."],
    "": ["The order total does not match its lines."]
  }
}
```

The extension appears only on an `Invalid` outcome that carries failures. A denial omits it, and an invalid result without failures omits it, because there is nothing to group.

## Exception Handling

`LiteBusExceptionHandler` handles `LiteBusMessageDeniedException` and `LiteBusMessageInvalidException` by resolving the refusal code through `IRefusalProblemCatalog` and writing the problem details `ToHttpResult` writes for the same refusal. Both paths build the status, title, detail, stable `urn:litebus:` type, request path as instance, trace identifier, and validation errors extension in one shared mapping, so the two answers cannot drift. The handler reads the catalog from request services with `RefusalProblemCatalog.Default` as the fallback.

For a denied exception the handler uses the exception's reason and code. For an invalid exception it uses the carried failures: the reason joins every failure message, and the code is the single failure's code when exactly one failure carries one, otherwise no code, which selects the catalog fallback mapping and the `urn:litebus:mediation:invalid` type while the `errors` extension still carries every failure.

The problem is written through `IProblemDetailsService` when the host registered it with `AddProblemDetails`; otherwise the handler writes the same shape directly. Every other exception returns `false` without writing, leaving it for the next handler.

## Unmapped Codes

`IRefusalProblemCatalog.TryResolve` reports whether a mapping matched, unlike `Resolve`, which returns the `400 Bad Request` fallback for unknown codes with no signal. `RefusalProblemCatalog` overrides it to return `false` for the fallback path, covering exact hits, prefix hits with longest-prefix precedence, and custom mappings added with `Map`. The default interface member calls `Resolve` and returns `true` when the code is non-null, so existing implementers keep compiling; it cannot tell a fallback from a match, so an application that treats an unmapped code as a server error uses `RefusalProblemCatalog`.

The exception handler answers a refusal code with no mapping through `LiteBusExceptionHandlerOptions.UnmappedRefusalCode`:

```csharp
services.AddLiteBusExceptionHandler(options =>
{
    options.UnmappedRefusalCode = UnmappedRefusalCode.TreatAsServerError;
});
```

`UseFallback`, the default, answers the catalog fallback. `TreatAsServerError` answers `500 Internal Server Error` and logs the unmapped code at error level, for an application whose contract fails fast on missing product mappings. A refusal without a code always uses the fallback, because there is no mapping to miss.

## Limitations

The exception handler maps mediation refusals only: a fault the pipeline did not classify still propagates through the remaining handlers, and only a registered fault refusal classifier turns a storage fault into a refusal with a code. The catalog maps refusals only, so extending the shared `RefusalProblemCatalog.Default` changes answers process-wide; map on a catalog built by `CreateDefault` and register that instance instead.

`TreatAsServerError` reports the refusal as a server error but keeps the refusal detail and type. A client that branches on the problem type still sees the original code-derived type, not a generic server-error type.

## Related Docs

- [ASP.NET Core Execution Identity](aspnetcore-execution-identity.md)
- [ASP.NET Core Idempotency Key](aspnetcore-idempotency-key.md)
