# ASP.NET Core Problem Details

`LiteBus.Extensions.AspNetCore` maps mediation refusals to RFC 9457 problem details through `IRefusalProblemCatalog`. An endpoint that sends through `TrySendAsync` answers with `ToHttpResult`, and an endpoint that sends through the throwing `SendAsync` relies on `LiteBusExceptionHandler`, so both paths write the same problem shape. Every response carries the catalog status and title, the refusal detail, a `code` extension with the refusal code, a problem `type` built by the configured formatter, the request path as instance, and the request trace identifier as `traceId`. An invalid result carries its validation failures as an `errors` extension, and a mediation fault answers `500 Internal Server Error`.

For the execution identity the same endpoints run under, see [ASP.NET Core Execution Identity](aspnetcore-execution-identity.md). For keying a command by the request header on the same endpoints, see [ASP.NET Core Idempotency Key](aspnetcore-idempotency-key.md).

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

`AddLiteBusExceptionHandler` registers the configured `LiteBusExceptionHandlerOptions` as a singleton, seeds `RefusalProblemOptions` with the same unmapped-code policy when the application registered none, and registers the handler with `AddExceptionHandler`, so `UseExceptionHandler` routes mediation exceptions through the refusal catalog. For a result-producing command, pass the value through with `result.ToHttpResult(context, value => Results.Ok(value))`.

## Catalog Resolution

`RefusalProblemCatalog` resolves a refusal code in this order: the exact code mapping, then the longest prefix mapping, then the outcome default for the mediation outcome, then the `400 Bad Request` fallback. The default mappings answer a missing declaration, an authorization denial, and a missing tenant with `403 Forbidden`; a caller that is not signed in with `401 Unauthorized`; a validation failure with `422 Unprocessable Content`; a concurrency conflict with `409 Conflict`; an idempotency key another delivery still holds with `409 Conflict`; an idempotency key reused for different content with `422 Unprocessable Content`; and anything else with the fallback.

A code ending in `.*` is a prefix pattern covering every code under that prefix, so `authorization.*` covers `authorization.forbidden`. A prefix pattern does not match its own bare prefix, and an exact code does not match longer codes that start with it. A pattern containing `*` anywhere else throws `ArgumentException`.

Both outcome defaults start as the fallback answer, so an unmapped refusal answers `400 Bad Request` unless the host sets an outcome default or maps the code. `MapInvalid` sets the mapping answered for an invalid refusal with no code mapping, and `MapDenied` sets the one for a denied refusal. Either call overwrites the previous default, so an application that answers every invalid refusal with one status makes that a one-line change:

```csharp
var catalog = RefusalProblemCatalog.CreateDefault();
catalog.MapInvalid(StatusCodes.Status400BadRequest, "Invalid request");
services.AddSingleton<IRefusalProblemCatalog>(catalog);
```

With the invalid default in place, an invalid refusal answers the same status and title whether it carries zero, one, or several validation failures, unless a code mapping is more specific. `TryResolve` reports `true` only for an exact or prefix match; an outcome-default answer reports `false`, so the unmapped-code policy still applies to codes with no explicit mapping. The default interface member returns `false`, so a catalog that does not report matches fails fast under an unmapped-code policy instead of silently reporting every code as mapped.

## Module Mappings

Each module contributes its own codes on a catalog built by `RefusalProblemCatalog.CreateDefault`, passing its name as the contributor so the catalog export and the diagnostic probe show which module owns each answer:

```csharp
var catalog = RefusalProblemCatalog.CreateDefault();
catalog.Map("orders.limit-exceeded", StatusCodes.Status409Conflict, "Order limit exceeded", "orders");
catalog.Map("orders.*", StatusCodes.Status400BadRequest, "Invalid order request", "orders");
services.AddSingleton<IRefusalProblemCatalog>(catalog);
```

Registrations follow one conflict rule: mapping a pattern that already carries the same status code and title is a no-op that keeps the first contributor, mapping it to a different status code or title throws `LiteBusConfigurationException` naming the pattern and both mappings, and a deliberate override calls `Replace`. `Entries` lists every registered mapping with its contributor, ordered by pattern. `RefusalProblemCatalog.Default` is shared by the process, so an application maps on a catalog built by `CreateDefault` and registers that instance instead of extending the shared default.

## Refusal Code Extension

Every problem response carries the refusal code as a `code` extension when the refusal supplied one, so one client handler reads the machine-readable code without parsing the detail sentence. Set `RefusalProblemOptions.IncludeRefusalCode` to `false` to omit it:

```csharp
services.AddSingleton(new RefusalProblemOptions { IncludeRefusalCode = false });
```

`ToHttpResult` also accepts caller-supplied options that override the registered instance for that call.

## Problem Type Format

`IProblemTypeFormatter` formats the problem `type` URI from the refusal code and the mediation outcome, and `RefusalProblemOptions.TypeFormatter` selects the formatter for both paths. `UrnProblemTypeFormatter`, the default, answers `urn:litebus:` plus the code with dots replaced by colons for a coded refusal, and the outcome identifier such as `urn:litebus:mediation:invalid` for a refusal without a code. `DocumentationUrlProblemTypeFormatter` answers a documentation URL template with a `{code}` placeholder, substituting the code for a coded refusal and `mediation.<outcome>` for a refusal without one; a template without the placeholder throws `ArgumentException`. `AboutBlankProblemTypeFormatter` answers `about:blank` for every refusal while the mapped status and title stay as mapped:

```csharp
services.AddSingleton(new RefusalProblemOptions
{
    TypeFormatter = new DocumentationUrlProblemTypeFormatter("https://errors.example.com/{code}"),
});
```

## Validation Errors

An `Invalid` outcome that carries failures writes them through `IValidationErrorsWriter`, selected by `RefusalProblemOptions.ValidationErrors`. The writer runs only for an invalid outcome with at least one failure; a denial and an invalid result without failures omit the extension. The catalog status, title, and type stay as mapped either way.

`DictionaryValidationErrorsWriter`, the default, writes `errors` keyed by `ValidationFailure` member, with message-level failures under an empty key, in the same shape as `ValidationProblemDetails.Errors`. It writes `errorCodes` beside it, keyed by the same members with each failure code aligned by index to its message, so clients read per-failure codes without parsing prose. A failure the validator left uncoded carries a `null` code entry.

```json
{
  "status": 422,
  "title": "Request validation failed",
  "type": "urn:litebus:validation:order:quantity",
  "code": "validation.order.quantity",
  "errors": {
    "Quantity": ["Quantity must be positive."],
    "": ["The order total does not match its lines."]
  },
  "errorCodes": {
    "Quantity": ["validation.order.quantity"],
    "": [null]
  }
}
```

`ArrayValidationErrorsWriter` writes the RFC 9457 array shape instead: `errors` becomes one `ValidationErrorEntry` per failure, carrying the message as `detail`, the member as a JSON pointer, and the failure code. A message-level failure carries a `null` pointer, and an uncoded failure carries a `null` code.

```json
{
  "status": 422,
  "title": "Request validation failed",
  "type": "urn:litebus:validation:order:quantity",
  "code": "validation.order.quantity",
  "errors": [
    { "detail": "Quantity must be positive.", "pointer": "/Quantity", "Code": "validation.order.quantity" },
    { "Detail": "The order total does not match its lines.", "pointer": null, "Code": null }
  ]
}
```

```csharp
services.AddSingleton(new RefusalProblemOptions
{
    ValidationErrors = ArrayValidationErrorsWriter.Default,
});
```

A client that renders a message and branches on a code per field reads `FieldValidationErrorsWriter`, which writes one object per failure under `fields` with `field`, `code` and `message` members. `FieldValidationErrorsWriterOptions` renames the extension and each member to match an existing client contract:

```csharp
services.AddSingleton(new RefusalProblemOptions
{
    ValidationErrors = new FieldValidationErrorsWriter(new FieldValidationErrorsWriterOptions
    {
        ExtensionName = "fields",
        FieldMemberName = "field",
        CodeMemberName = "code",
        MessageMemberName = "message",
    }),
});
```

```json
{
  "status": 400,
  "code": "client-required",
  "fields": [
    { "field": "ClientId", "code": "client-required", "message": "A client is required." }
  ]
}
```

## Application Content

`IProblemDetailsEnricher` adds application-specific content to a problem response, such as localized titles or projected members. It runs after the catalog status and title, the detail sentence, the problem type, the refusal code, and the validation errors are written, so it can replace or project any of them. The `ProblemDetailsEnrichmentContext` it receives carries the request context, the mediation outcome, the refusal code, and the reason. `NullProblemDetailsEnricher`, the default, leaves every response unchanged. LiteBus ships no string tables: localization belongs to the application behind this port, for example an enricher that reads `Accept-Language` from the request context and replaces the title.

```csharp
public sealed class AzureErrorProjection : IProblemDetailsEnricher
{
    public void Enrich(ProblemDetails problem, ProblemDetailsEnrichmentContext context)
    {
        ArgumentNullException.ThrowIfNull(problem);
        ArgumentNullException.ThrowIfNull(context);

        problem.Extensions["error"] = new Dictionary<string, string?>
        {
            ["code"] = context.Code,
            ["message"] = problem.Detail,
        };
    }
}

services.AddSingleton(new RefusalProblemOptions
{
    Enricher = new AzureErrorProjection(),
});
```

Classification stays on the catalog either way: outcome defaults and module mappings still decide the status and title, and only the wire shape is a port.

## Exception Conversion in the Pipeline

A domain exception a handler raises becomes a refusal inside the mediation pipeline through `IRefusalExceptionTranslator`, so HTTP, gRPC, and in-process callers all observe the same refusal. Transports never catch application exception types, and the HTTP catalog maps refusal codes only: it has no exception-mapping member.

```csharp
public sealed class OrderLimitTranslator : IRefusalExceptionTranslator
{
    public ValueTask<Refusal?> TranslateAsync(Exception exception, CancellationToken cancellationToken)
    {
        if (exception is OrderLimitExceeded exceeded)
        {
            return new ValueTask<Refusal?>(Refusal.Denied(exceeded.Message, "orders.limit-exceeded"));
        }

        return new ValueTask<Refusal?>(result: null);
    }
}

services.AddLiteBus(builder =>
{
    builder.AddMessaging(messaging =>
    {
        messaging.UseRefusalExceptionTranslator<OrderLimitTranslator>();
    });
});
```

Translators are scoped and asynchronous, resolved from the dispatch scope in registration order when a handler throws, and the first translation wins. They run after the synchronous fault refusal classifiers: a fault the classifiers already recognize keeps the refusal it always had, and only what they leave unrecognized is offered here. Cancellation, an exception that already carries a refusal, and a `MessageFaultException` are never offered. A translator returns `null` for exceptions it does not recognize and must not throw: a fault it raises escapes the mediation in place of the exception it was offered.

## Exception Handling

`LiteBusExceptionHandler` handles `LiteBusMessageDeniedException` and `LiteBusMessageInvalidException` by resolving the refusal code through `IRefusalProblemCatalog` and writing the problem details `ToHttpResult` writes for the same refusal. Both paths build the status, title, detail, type, instance, trace identifier, refusal code, and validation errors extension in one shared mapping, so the two answers cannot drift. The handler reads the catalog from request services with `RefusalProblemCatalog.Default` as the fallback.

For a denied exception the handler uses the exception reason and code. For an invalid exception it uses the carried failures: the reason joins every failure message, and the code is the single failure code when there is exactly one failure, otherwise no code, which selects the outcome default while the `errors` extension still carries every failure.

The problem is written through `IProblemDetailsService` when the host registered it with `AddProblemDetails`; otherwise the handler writes the same shape directly. Every other exception returns `false` without writing, leaving it for the next handler.

## Faults

A `MessageFaultException` reports a configuration or dependency failure an operator must fix, such as a missing permission lookup or an unreachable store. It is never a refusal, so refusal rewriting cannot turn it into a caller-actionable end, and translators and classifiers never see it. `LiteBusExceptionHandler` answers it as `500 Internal Server Error` with the `Internal server error` title and the safe fault detail, and logs it at error level. A fault without a reason answers a generic detail sentence. The result path treats a `Failed` outcome the same way, bypassing the catalog, so a fault answers `500 Internal Server Error` on both paths.

## Unmapped Codes

A refusal code with no exact or prefix mapping follows the unmapped-code policy of its outcome on both the result path and the exception path: `UnmappedRefusalCode` for a denial and `UnmappedValidationCode` for an invalid result. `UseFallback`, the default of both, answers the outcome default with the refusal detail. `TreatAsServerError` answers `500 Internal Server Error` with the `about:blank` problem type and logs the unmapped code at error level, for an application whose contract fails fast on missing product mappings. The refusal detail and the `code` extension stay on the response either way.

```csharp
services.AddLiteBusExceptionHandler(options =>
{
    options.UnmappedRefusalCode = UnmappedRefusalCode.TreatAsServerError;
});
```

The two policies are separate because the codes differ in kind. A denial code names a product rule, so a strict product answers an unmapped one as a missing mapping. Validation codes are per field and per validator, such as `client-required` and `amount-positive`, and no catalog maps each of them, so a product that is strict about denials keeps `UnmappedValidationCode.UseFallback` and answers every invalid result with the invalid default. Set `options.UnmappedValidationCode = UnmappedValidationCode.TreatAsServerError` only when every validation code is mapped.

`RefusalProblemOptions` registered in request services governs both paths; `LiteBusExceptionHandlerOptions.UnmappedRefusalCode` and `UnmappedValidationCode` apply only when none is registered, and `AddLiteBusExceptionHandler` seeds the shared options so the result path and the exception path answer unmapped codes with one policy. A refusal without a code always uses the outcome default, because there is no mapping to miss.

## Catalog Export and Diagnostics

`Export` returns every registered mapping with its contributor plus the invalid and denied defaults as a `RefusalProblemCatalogExport`, and `ExportJson` serializes it with the web serializer defaults, for OpenAPI response examples, documentation tooling, and compliance pipelines that consume the full error list. `RefusalProblemCatalogDiagnosticCheck`, reported as `litebus.problem-catalog`, carries the mapping count, the mapped patterns, the contributors, and both outcome defaults, so composition explains which module owns each answer. A host that registered no catalog answers from the shared default catalog, which the probe reports instead of failing.

## OpenAPI Problem Schema

The OpenAPI document ASP.NET Core generates describes `ProblemDetails` with `type`, `title`, `status`, `detail` and `instance` only, so a generated client misses the members LiteBus always sends. The `LiteBus.Extensions.AspNetCore.OpenApi` package adds a document transformer that adds `code`, `traceId` and `errors` to the problem schemas, and with `IncludeRefusalCodes` lists each status's refusal codes from the catalog on the problem responses:

```csharp
services.AddLiteBusProblemSchema(schema =>
{
    schema.IncludeRefusalCodes = true;
});

app.MapOpenApi();
```

`AddLiteBusProblemSchema` registers the transformer through `AddOpenApi`. `ProblemSchemaNames` names the component schemas it enriches (`ProblemDetails` and `HttpValidationProblemDetails` by default), and `RefusalCatalog` overrides the catalog the codes are read from; by default it is the catalog registered in the container, or `RefusalProblemCatalog.Default`.

## Limitations

The HTTP layer maps mediation refusals and faults only: any other exception propagates through the remaining handlers. `TreatAsServerError` answers the generic `about:blank` problem type with the `Internal server error` title, so a client that branches on the problem type sees a server error rather than the code-derived type. The catalog maps refusals only, so extending the shared `RefusalProblemCatalog.Default` changes answers process-wide; map on a catalog built by `CreateDefault` and register that instance instead.

## Related Docs

- [ASP.NET Core Execution Identity](aspnetcore-execution-identity.md)
- [ASP.NET Core Idempotency Key](aspnetcore-idempotency-key.md)
