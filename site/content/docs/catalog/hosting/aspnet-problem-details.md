# ASP.NET Core Problem Details

## Header

- **ID**: `hosting.aspnet-problem-details`
- **Name**: ASP.NET Core problem details
- **Maturity**: GA
- **Summary**: Maps mediation refusals to RFC 9457 problem details through `IRefusalProblemCatalog`, with per-member validation errors on invalid results, an exception handler for the throwing send path, and faults answered as server errors.

## What It Does

`LiteBus.Extensions.AspNetCore` answers a mediation refusal with the status code and title the refusal code maps to in an `IRefusalProblemCatalog`. An endpoint that sends through `TrySendAsync` answers with `ToHttpResult`. An endpoint that sends through the throwing `SendAsync` relies on `LiteBusExceptionHandler`, which maps `LiteBusMessageDeniedException` and `LiteBusMessageInvalidException` through the same catalog to the same problem details. Both paths build the response in one shared mapping: the catalog status and title, the detail sentence, the `code` extension, a problem `type` built by the configured formatter, the request path as instance, the request trace identifier as `traceId`, and, for an invalid result that carries failures, a validation errors extension.

```csharp
services.AddLiteBusExceptionHandler();
app.UseExceptionHandler();

endpoints.MapPost("/orders", async (HttpContext context, ICommandMediator mediator, PlaceOrder command, CancellationToken cancellationToken) =>
{
    var result = await mediator.TrySendAsync(command, cancellationToken: cancellationToken).ConfigureAwait(false);
    return result.ToHttpResult(context, () => Results.Ok());
});
```

An application contributes its own codes with `Map` on a catalog built by `RefusalProblemCatalog.CreateDefault` and registers that instance in request services, so both paths answer product-specific refusals with the status the product chooses. Domain exceptions become refusals inside the mediation pipeline through `IRefusalExceptionTranslator`, so the refusal the catalog renders is the same one every transport observes. A `MessageFaultException` is never a refusal and answers `500 Internal Server Error` on both paths.

## Public Surface

### Result Mapping

- `IResult ToHttpResult(this MediationResult result, HttpContext httpContext, Func<IResult> onSuccess, IRefusalProblemCatalog? catalog = null, RefusalProblemOptions? options = null)`
- `IResult ToHttpResult<TMessageResult>(this MediationResult<TMessageResult> result, HttpContext httpContext, Func<TMessageResult?, IResult> onSuccess, IRefusalProblemCatalog? catalog = null, RefusalProblemOptions? options = null)`

When the mediation ran or was answered from the store, the helper runs the `onSuccess` callback. Otherwise it resolves the refusal code through the catalog and writes the problem response. The catalog resolves in order: the caller-supplied catalog, the catalog registered in request services, and `RefusalProblemCatalog.Default` as the fallback. The options resolve in the same order, with a default instance as the fallback.

### Catalog Resolution

- `IRefusalProblemCatalog Map(string code, int statusCode, string title, string? contributor = null)`
- `IRefusalProblemCatalog Replace(string code, int statusCode, string title, string? contributor = null)`
- `IRefusalProblemCatalog MapInvalid(int statusCode, string title)`
- `IRefusalProblemCatalog MapDenied(int statusCode, string title)`
- `RefusalProblemMap Resolve(string? code)`
- `RefusalProblemMap Resolve(string? code, MediationOutcome outcome)`
- `bool TryResolve(string? code, out RefusalProblemMap map)`
- `bool TryResolve(string? code, MediationOutcome outcome, out RefusalProblemMap map)`
- `IReadOnlyList<RefusalProblemEntry> Entries` on `RefusalProblemCatalog`
- `RefusalProblemCatalogExport Export()` on `RefusalProblemCatalog`
- `string ExportJson(JsonSerializerOptions? options = null)` on `RefusalProblemCatalog`

Resolution order is the exact code mapping, then the longest prefix mapping, then the outcome default for the mediation outcome, then the `400 Bad Request` fallback. Both outcome defaults start as the fallback answer. `Map` follows the conflict rule: an identical repeat is a no-op that keeps the first contributor, a conflicting remap throws `LiteBusConfigurationException` naming the pattern and both mappings, and `Replace` overrides deliberately. `TryResolve` returns `true` only for an exact or prefix match; an outcome-default answer reports `false`, and the default interface member returns `false`. `ExportJson` serializes every mapping with its contributor plus both outcome defaults with the web serializer defaults.

### Problem Type Format

- `string Format(string? code, MediationOutcome outcome)` on `IProblemTypeFormatter`
- `UrnProblemTypeFormatter` (default): `urn:litebus:` plus the code with dots replaced by colons, or `urn:litebus:mediation:<outcome>` when no code was supplied
- `DocumentationUrlProblemTypeFormatter(string template)`: the template with its required `{code}` placeholder replaced by the code, or by `mediation.<outcome>` when no code was supplied
- `AboutBlankProblemTypeFormatter`: `about:blank` for every refusal

The formatter is selected by `RefusalProblemOptions.TypeFormatter` for both paths. The mapped status and title stay as mapped whatever the formatter answers.

### Validation Errors

- `void WriteErrors(ProblemDetails problem, IReadOnlyList<ValidationFailure> failures)` on `IValidationErrorsWriter`
- `DictionaryValidationErrorsWriter` (default): `errors` as `IDictionary<string, string[]>` keyed by member with message-level failures under an empty key, plus `errorCodes` keyed by the same members with each failure code aligned by index to its message
- `ArrayValidationErrorsWriter`: `errors` as one `ValidationErrorEntry` per failure, carrying the message as `detail`, the member as a JSON pointer, and the failure code
- `FieldValidationErrorsWriter`: one `{ field, code, message }` object per failure under `fields`, with the extension and member names set through `FieldValidationErrorsWriterOptions`
- `ValidationErrorEntry(string Detail, string? pointer, string? Code)`: a message-level failure carries a `null` pointer, and an uncoded failure carries a `null` code

The writer is selected by `RefusalProblemOptions.ValidationErrors` for both paths, and runs only for an `Invalid` outcome that carries failures. The shape of `errors` under the default writer matches ASP.NET Core's `ValidationProblemDetails.Errors`.

### Application Content

- `void Enrich(ProblemDetails problem, ProblemDetailsEnrichmentContext context)` on `IProblemDetailsEnricher`
- `ProblemDetailsEnrichmentContext(HttpContext HttpContext, MediationOutcome Outcome, string? Code, string? Reason)`
- `NullProblemDetailsEnricher` (default): leaves every problem response unchanged

The enricher is selected by `RefusalProblemOptions.Enricher` for both paths, and runs after the catalog status and title, the detail sentence, the problem type, the refusal code, and the validation errors are written, so it can replace or project any of them.

### Refusal Code Extension

- `RefusalProblemOptions.IncludeRefusalCode` (default `true`)

Every problem response carries the refusal code as a `code` extension when the refusal supplied one. Setting the option to `false` omits it on both paths.

### Exception Handling

- `LiteBusExceptionHandler` (`IExceptionHandler` implementation)
- `IServiceCollection AddLiteBusExceptionHandler(this IServiceCollection services, Action<LiteBusExceptionHandlerOptions>? configure = null)`

The handler resolves the refusal code through the catalog read from request services with `RefusalProblemCatalog.Default` as the fallback, and writes through `IProblemDetailsService` when the host registered it with `AddProblemDetails`; otherwise it writes the same shape directly. Every exception other than the two mediation refusal exceptions and `MessageFaultException` returns `false` without writing, leaving it for the next handler. For an invalid exception the reason joins every failure message and the code is the single failure code when there is exactly one failure, otherwise no code, which selects the outcome default while the `errors` extension still carries every failure.

### Pipeline Exception Conversion

- `ValueTask<Refusal?> TranslateAsync(Exception exception, CancellationToken cancellationToken)` on `IRefusalExceptionTranslator`
- `MessageModuleBuilder UseRefusalExceptionTranslator<TTranslator>()`
- `MessageModuleBuilder UseRefusalExceptionTranslatorInstance(IRefusalExceptionTranslator translator)`

Translators are scoped and asynchronous, resolved from the dispatch scope in registration order when a handler throws, and the first translation wins. They run after the synchronous fault refusal classifiers. Cancellation, an exception that already carries a refusal, and a `MessageFaultException` are never offered. The HTTP catalog maps refusal codes only and has no exception-mapping member.

### Configuration

- `RefusalProblemOptions.TypeFormatter` (default `UrnProblemTypeFormatter.Default`)
- `RefusalProblemOptions.ValidationErrors` (default `DictionaryValidationErrorsWriter.Default`)
- `RefusalProblemOptions.Enricher` (default `NullProblemDetailsEnricher.Default`)
- `RefusalProblemOptions.UnmappedRefusalCode` (default `UnmappedRefusalCode.UseFallback`), applied to denied refusals
- `RefusalProblemOptions.UnmappedValidationCode` (default `UnmappedValidationCode.UseFallback`), applied to invalid refusals, so a host strict about denial codes can stay lenient about per-field validation codes
- `RefusalProblemOptions.IncludeRefusalCode` (default `true`)
- `LiteBusExceptionHandlerOptions.UnmappedRefusalCode` (default `UnmappedRefusalCode.UseFallback`)
- `LiteBusExceptionHandlerOptions.UnmappedValidationCode` (default `UnmappedValidationCode.UseFallback`)
- `UnmappedRefusalCode.UseFallback`: answer the outcome default with the refusal detail
- `UnmappedRefusalCode.TreatAsServerError`: answer `500 Internal Server Error` with the `about:blank` problem type and log the unmapped code at error level

`RefusalProblemOptions` registered in request services governs both paths; `LiteBusExceptionHandlerOptions.UnmappedRefusalCode` and `UnmappedValidationCode` apply only when none is registered, and `AddLiteBusExceptionHandler` seeds the shared options with the configured policies. The `LiteBus.Extensions.AspNetCore.OpenApi` package adds the problem members and the catalog's refusal codes to the OpenAPI document through `AddLiteBusProblemSchema`. A refusal without a code always uses the outcome default, because there is no mapping to miss. A `Failed` outcome bypasses the catalog and answers `500 Internal Server Error` on both paths.

## Packages

- `LiteBus.Extensions.AspNetCore`

## Requires

- `hosting.add-lite-bus-microsoft-di` or `hosting.add-lite-bus-autofac`
- `app.UseExceptionHandler()` for the exception path; without the middleware the handler never runs

## Invariants

- `ToHttpResult` and `LiteBusExceptionHandler` write the same problem for the same refusal: status, title, detail, type, instance, trace identifier, code extension, and validation errors extension are built in one shared mapping.
- Resolution order is the exact code mapping, then the longest prefix mapping, then the outcome default for the mediation outcome, then the fallback. An invalid refusal answers the invalid default whatever the number of validation failures, unless a code mapping is more specific.
- An exact mapping wins over a prefix pattern, and the longest prefix wins when several patterns cover one code.
- A code ending in `.*` is a prefix pattern covering every code under that prefix, so `authorization.*` covers `authorization.forbidden`. A prefix pattern does not match its own bare prefix, and an exact code does not match longer codes that start with it.
- Mapping a pattern that already carries the same status code and title is a no-op. Mapping it to a different status code or title throws naming the pattern and both mappings. A deliberate override calls `Replace`. `MapInvalid` and `MapDenied` overwrite the previous default instead of throwing.
- `TryResolve` returns `true` only for an exact or prefix match. Null, empty, and whitespace codes take the fallback or outcome-default path and return `false`. An outcome-default answer reports `false` even though a mapping was answered.
- The `code` extension is written whenever the refusal supplied a code, unless `IncludeRefusalCode` is `false`.
- The enricher runs after every built-in member is written, so it can replace or project any of them.
- `TreatAsServerError` answers `500 Internal Server Error` with the `about:blank` problem type and the `Internal server error` title. The refusal detail and the `code` extension stay on the response.
- A `MessageFaultException` is never translated, classified, or rewritten into a refusal, and answers `500 Internal Server Error` on both paths.
- The HTTP layer maps refusal codes only. Domain exceptions become refusals through `IRefusalExceptionTranslator` in the pipeline, so every transport observes the same refusal.
- `RefusalProblemCatalog.Default` is shared by the process. Mapping on it changes answers process-wide, so an application maps on a catalog built by `CreateDefault` and registers that instance.

## Non-Goals

- Mapping exception types in the HTTP layer. Exception conversion belongs to `IRefusalExceptionTranslator` in the pipeline.
- Client-side rendering of the `errors` and `errorCodes` extensions.
- Exporter setup for logged unmapped codes beyond the error-level log entry.

## Observability

The result path logs an unmapped code at error level only when `TreatAsServerError` is configured, through `ILogger<RefusalProblemCatalog>` resolved from request services, skipping logging when logging is not registered. The exception handler logs an unmapped code at error level under the same policy with the code as a log property and the mediation exception attached, and logs every mediation fault at error level with its fault code and reason. `RefusalProblemCatalogDiagnosticCheck`, reported as `litebus.problem-catalog`, lists the mapping count, the mapped patterns, the contributors, and both outcome defaults; a host that registered no catalog reports the shared default catalog instead of failing.

## Test Coverage

### Covered Use Cases

#### `RefusalProblemHttpTests.ToHttpResult_Precedence_ExactBeatsPrefixBeatsOutcomeDefault`

- **Use case**: catalog resolution order across exact, prefix, and outcome-default mappings
- **Test kind**: Unit
- **Description**: maps an invalid refusal whose code matches an exact mapping, a prefix pattern, and the invalid default
- **Behavior**: maps the result through `ToHttpResult` with each mapping removed in turn
- **Expected outcome**: the exact mapping wins first, then the longest prefix, then the outcome default
- **Remarks**: `tests/LiteBus.Extensions.UnitTests/AspNetCore/Version8/RefusalProblemHttpTests.cs`

#### `RefusalProblemHttpTests.ToHttpResult_InvalidWithZeroOneAndTwoFailures_AnswersIdenticalInvalidDefault`

- **Use case**: one stable invalid answer for any failure count
- **Test kind**: Unit
- **Description**: maps invalid refusals with zero, one, and two failures through `ToHttpResult`
- **Behavior**: writes each problem response
- **Expected outcome**: identical invalid-default status and title on all three
- **Remarks**: `tests/LiteBus.Extensions.UnitTests/AspNetCore/Version8/RefusalProblemHttpTests.cs`

#### `RefusalProblemHttpTests.ToHttpResult_WritesCodeExtensionByDefault`

- **Use case**: refusal code carried as a `code` extension
- **Test kind**: Unit
- **Description**: maps a coded refusal through `ToHttpResult` with default options
- **Behavior**: writes the problem response
- **Expected outcome**: the `code` extension carries the refusal code
- **Remarks**: `tests/LiteBus.Extensions.UnitTests/AspNetCore/Version8/RefusalProblemHttpTests.cs`

#### `RefusalProblemHttpTests.ToHttpResult_IncludeRefusalCodeFalse_OmitsCodeExtension`

- **Use case**: refusal code extension disabled
- **Test kind**: Unit
- **Description**: maps a coded refusal with `IncludeRefusalCode` set to `false`
- **Behavior**: writes the problem response
- **Expected outcome**: the response carries no `code` entry
- **Remarks**: `tests/LiteBus.Extensions.UnitTests/AspNetCore/Version8/RefusalProblemHttpTests.cs`

#### `RefusalProblemHttpTests.ToHttpResult_DocumentationUrlFormatter_WritesTemplateType`

- **Use case**: documentation-URL problem type
- **Test kind**: Unit
- **Description**: maps a refusal with a URL-template type formatter
- **Behavior**: writes the problem response
- **Expected outcome**: the `type` carries the template with the code substituted
- **Remarks**: `tests/LiteBus.Extensions.UnitTests/AspNetCore/Version8/RefusalProblemHttpTests.cs`

#### `RefusalProblemHttpTests.ToHttpResult_AboutBlankFormatter_WritesAboutBlankKeepingMappedTitle`

- **Use case**: blank problem type
- **Test kind**: Unit
- **Description**: maps a refusal with the `about:blank` type formatter
- **Behavior**: writes the problem response
- **Expected outcome**: the `type` is `about:blank` while the mapped title stays
- **Remarks**: `tests/LiteBus.Extensions.UnitTests/AspNetCore/Version8/RefusalProblemHttpTests.cs`

#### `RefusalProblemHttpTests.ToHttpResult_UnmappedCodeWithTreatAsServerError_WritesGenericTypeAndLogs`

- **Use case**: unmapped code reported as a server error on the result path
- **Test kind**: Unit
- **Description**: maps a refusal with an unknown code and `TreatAsServerError`
- **Behavior**: writes the problem response
- **Expected outcome**: `500 Internal Server Error` with the `about:blank` type and the unmapped code logged at error level
- **Remarks**: `tests/LiteBus.Extensions.UnitTests/AspNetCore/Version8/RefusalProblemHttpTests.cs`

#### `RefusalProblemHttpTests.ToHttpResult_UnmappedCodeWithUseFallback_WritesOutcomeDefault`

- **Use case**: unmapped code answered from the outcome default
- **Test kind**: Unit
- **Description**: maps a refusal with an unknown code and `UseFallback`
- **Behavior**: writes the problem response
- **Expected outcome**: the outcome-default status and title with the refusal detail
- **Remarks**: `tests/LiteBus.Extensions.UnitTests/AspNetCore/Version8/RefusalProblemHttpTests.cs`

#### `RefusalProblemCatalogTests.Map_ConflictingRemap_ThrowsNamingCodeAndBothMappings`

- **Use case**: conflicting mapping fails naming both sources
- **Test kind**: Unit
- **Description**: maps a pattern that already carries a different status code and title
- **Behavior**: calls `Map` with the conflicting mapping
- **Expected outcome**: `LiteBusConfigurationException` naming the pattern and both mappings
- **Remarks**: `tests/LiteBus.Extensions.UnitTests/AspNetCore/Version8/RefusalProblemCatalogTests.cs`

#### `RefusalProblemCatalogTests.Map_IdenticalRepeat_IsNoOpKeepingFirstContributor`

- **Use case**: identical mapping repeat is a no-op
- **Test kind**: Unit
- **Description**: maps a pattern twice with the same status code, title, and a different contributor
- **Behavior**: calls `Map` with the identical mapping
- **Expected outcome**: the mapping keeps the first contributor and no error is raised
- **Remarks**: `tests/LiteBus.Extensions.UnitTests/AspNetCore/Version8/RefusalProblemCatalogTests.cs`

#### `RefusalProblemCatalogTests.Replace_ExistingMapping_OverridesMapping`

- **Use case**: deliberate mapping override
- **Test kind**: Unit
- **Description**: replaces a pattern that already carries a mapping
- **Behavior**: calls `Replace` with the new mapping
- **Expected outcome**: the pattern answers the new status code and title
- **Remarks**: `tests/LiteBus.Extensions.UnitTests/AspNetCore/Version8/RefusalProblemCatalogTests.cs`

#### `RefusalProblemCatalogTests.MapInvalid_SetsInvalidDefaultUsedWithoutCodeMapping`

- **Use case**: invalid outcome default
- **Test kind**: Unit
- **Description**: sets the invalid default and resolves an uncoded invalid refusal
- **Behavior**: calls `MapInvalid` and resolves with the invalid outcome
- **Expected outcome**: the invalid default mapping is answered
- **Remarks**: `tests/LiteBus.Extensions.UnitTests/AspNetCore/Version8/RefusalProblemCatalogTests.cs`

#### `RefusalProblemCatalogTests.MapDenied_SetsDeniedDefaultUsedWithoutCodeMapping`

- **Use case**: denied outcome default
- **Test kind**: Unit
- **Description**: sets the denied default and resolves an uncoded denied refusal
- **Behavior**: calls `MapDenied` and resolves with the denied outcome
- **Expected outcome**: the denied default mapping is answered
- **Remarks**: `tests/LiteBus.Extensions.UnitTests/AspNetCore/Version8/RefusalProblemCatalogTests.cs`

#### `RefusalProblemCatalogTests.Entries_ListsEveryMappingWithItsContributorOrderedByPattern`

- **Use case**: catalog contents listed with per-module attribution
- **Test kind**: Unit
- **Description**: maps several patterns with contributors
- **Behavior**: reads `Entries`
- **Expected outcome**: every mapping listed with its contributor, ordered by pattern
- **Remarks**: `tests/LiteBus.Extensions.UnitTests/AspNetCore/Version8/RefusalProblemCatalogTests.cs`

#### `RefusalProblemCatalogTests.ExportJson_ListsEveryCodeWithMappingAndContributor`

- **Use case**: catalog exported as JSON for tooling
- **Test kind**: Unit
- **Description**: maps patterns with contributors and exports the catalog
- **Behavior**: calls `ExportJson`
- **Expected outcome**: JSON listing every code with its mapping and contributor
- **Remarks**: `tests/LiteBus.Extensions.UnitTests/AspNetCore/Version8/RefusalProblemCatalogTests.cs`

#### `MediationResultValidationErrorsTests.ToHttpResult_InvalidWithFieldFailures_WritesErrorsExtension`

- **Use case**: invalid result with per-member failures carries the errors extension
- **Test kind**: Unit
- **Description**: maps an invalid result with field failures through `ToHttpResult`
- **Behavior**: writes the problem response
- **Expected outcome**: `errors` keyed by member alongside the catalog status, title, and type
- **Remarks**: `tests/LiteBus.Extensions.UnitTests/AspNetCore/Problems/MediationResultValidationErrorsTests.cs`

#### `MediationResultValidationErrorsTests.ToHttpResult_InvalidWithCodedFailures_WritesErrorCodesAlignedWithErrors`

- **Use case**: per-failure codes aligned with messages in the dictionary shape
- **Test kind**: Unit
- **Description**: maps an invalid result with coded failures through `ToHttpResult`
- **Behavior**: writes the problem response
- **Expected outcome**: `errorCodes` keyed by the same members with each code aligned by index to its message
- **Remarks**: `tests/LiteBus.Extensions.UnitTests/AspNetCore/Problems/MediationResultValidationErrorsTests.cs`

#### `MediationResultValidationErrorsTests.ToHttpResult_ArrayValidationWriter_WritesErrorsArrayWithDetailPointerAndCode`

- **Use case**: validation failures written in the RFC 9457 array shape
- **Test kind**: Unit
- **Description**: maps an invalid result with the array validation writer
- **Behavior**: writes the problem response
- **Expected outcome**: `errors` carries one entry per failure with detail, pointer, and code
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

#### `ProblemTypeFormatterTests.UrnFormatter_CodedRefusal_WritesCodeWithColons`

- **Use case**: stable URN problem type for a coded refusal
- **Test kind**: Unit
- **Description**: formats a coded refusal with the URN formatter
- **Behavior**: calls `Format` with the code and outcome
- **Expected outcome**: `urn:litebus:` plus the code with dots replaced by colons
- **Remarks**: `tests/LiteBus.Extensions.UnitTests/AspNetCore/Problems/ProblemTypeFormatterTests.cs`

#### `ProblemTypeFormatterTests.UrnFormatter_UncodedRefusal_WritesOutcomeType`

- **Use case**: outcome-derived URN problem type when no code was supplied
- **Test kind**: Unit
- **Description**: formats an uncoded refusal with the URN formatter
- **Behavior**: calls `Format` with no code
- **Expected outcome**: the outcome identifier such as `urn:litebus:mediation:invalid`
- **Remarks**: `tests/LiteBus.Extensions.UnitTests/AspNetCore/Problems/ProblemTypeFormatterTests.cs`

#### `ProblemTypeFormatterTests.DocumentationUrlFormatter_TemplateWithoutPlaceholder_Throws`

- **Use case**: URL-template formatter requires its placeholder
- **Test kind**: Unit
- **Description**: builds a documentation-URL formatter from a template without `{code}`
- **Behavior**: constructs the formatter
- **Expected outcome**: `ArgumentException`
- **Remarks**: `tests/LiteBus.Extensions.UnitTests/AspNetCore/Problems/ProblemTypeFormatterTests.cs`

#### `ProblemDetailsEnricherTests.CustomEnricher_AddsProjectionToResponse`

- **Use case**: application content added to a problem response
- **Test kind**: Unit
- **Description**: maps a refusal with a custom enricher through a real response
- **Behavior**: writes the problem response
- **Expected outcome**: the response carries the projected members beside the built-in ones
- **Remarks**: `tests/LiteBus.Extensions.UnitTests/AspNetCore/Problems/ProblemDetailsEnricherTests.cs`

#### `ProblemDetailsEnricherTests.NullEnricher_LeavesProblemUnchanged`

- **Use case**: default enricher changes nothing
- **Test kind**: Unit
- **Description**: maps a refusal with the no-op enricher
- **Behavior**: writes the problem response
- **Expected outcome**: the response carries only the built-in members
- **Remarks**: `tests/LiteBus.Extensions.UnitTests/AspNetCore/Problems/ProblemDetailsEnricherTests.cs`

#### `ValidationErrorsWriterTests.DictionaryWriter_WritesCodesAlignedWithMessages`

- **Use case**: per-failure codes aligned with messages
- **Test kind**: Unit
- **Description**: writes failures with the dictionary writer
- **Behavior**: calls `WriteErrors` on a problem response
- **Expected outcome**: `errorCodes` keyed by the same members with codes aligned by index
- **Remarks**: `tests/LiteBus.Extensions.UnitTests/AspNetCore/Problems/ValidationErrorsWriterTests.cs`

#### `ValidationErrorsWriterTests.ArrayWriter_WritesOneEntryPerFailureWithDetailPointerAndCode`

- **Use case**: one array entry per failure
- **Test kind**: Unit
- **Description**: writes failures with the array writer
- **Behavior**: calls `WriteErrors` on a problem response
- **Expected outcome**: one `ValidationErrorEntry` per failure with detail, pointer, and code
- **Remarks**: `tests/LiteBus.Extensions.UnitTests/AspNetCore/Problems/ValidationErrorsWriterTests.cs`

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

#### `LiteBusExceptionHandlerTests.TryHandleAsync_MessageFaultException_WritesInternalServerErrorAndLogs`

- **Use case**: mediation fault answered as a server error
- **Test kind**: Unit
- **Description**: handles a `MessageFaultException` with a reason
- **Behavior**: writes the problem response
- **Expected outcome**: `500 Internal Server Error` with the fault detail and the fault logged at error level
- **Remarks**: `tests/LiteBus.Extensions.UnitTests/AspNetCore/Problems/LiteBusExceptionHandlerTests.cs`

#### `LiteBusExceptionHandlerTests.TryHandleAsync_MessageFaultExceptionWithoutReason_WritesGenericDetail`

- **Use case**: mediation fault without a reason answers a generic detail
- **Test kind**: Unit
- **Description**: handles a `MessageFaultException` without a reason
- **Behavior**: writes the problem response
- **Expected outcome**: `500 Internal Server Error` with a generic detail sentence
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

- **Use case**: unmapped code reported as a server error on the exception path
- **Test kind**: Unit
- **Description**: handles a denied exception with an unknown code and `TreatAsServerError`
- **Behavior**: writes the problem response
- **Expected outcome**: `500 Internal Server Error` with the `about:blank` type and the unmapped code logged at error level
- **Remarks**: `tests/LiteBus.Extensions.UnitTests/AspNetCore/Problems/LiteBusExceptionHandlerTests.cs`

#### `LiteBusExceptionHandlerTests.TryHandleAsync_NullCodeWithTreatAsServerError_UsesFallback`

- **Use case**: refusal without a code keeps the outcome default under `TreatAsServerError`
- **Test kind**: Unit
- **Description**: handles a refusal with no code and `TreatAsServerError`
- **Behavior**: writes the problem response
- **Expected outcome**: outcome-default mapping, not a server error
- **Remarks**: `tests/LiteBus.Extensions.UnitTests/AspNetCore/Problems/LiteBusExceptionHandlerTests.cs`

#### `LiteBusExceptionHandlerTests.TryHandleAsync_RegisteredProblemOptions_OverrideHandlerOptions`

- **Use case**: registered rendering options govern the exception path
- **Test kind**: Unit
- **Description**: handles a refusal with `RefusalProblemOptions` registered in request services
- **Behavior**: writes the problem response
- **Expected outcome**: the registered options decide the rendering, not the handler options
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

#### `RefusalProblemTryResolveTests.TryResolve_WithOutcome_UncodedInvalid_ReturnsFalseWithInvalidDefault`

- **Use case**: outcome-default answer reports the unmapped path
- **Test kind**: Unit
- **Description**: resolves an uncoded invalid refusal with its outcome
- **Behavior**: calls the outcome-aware `TryResolve` on the default catalog
- **Expected outcome**: `false` with the invalid-default mapping
- **Remarks**: `tests/LiteBus.Extensions.UnitTests/AspNetCore/Problems/RefusalProblemTryResolveTests.cs`

#### `RefusalProblemTryResolveTests.TryResolve_DefaultInterfaceMember_NonNullCodeReturnsFalse`

- **Use case**: default member fails fast instead of reporting a match
- **Test kind**: Unit
- **Description**: calls `TryResolve` on an implementer that does not override it
- **Behavior**: runs the default interface member
- **Expected outcome**: `false` even for a non-null code, so the unmapped-code policy applies
- **Remarks**: `tests/LiteBus.Extensions.UnitTests/AspNetCore/Problems/RefusalProblemTryResolveTests.cs`

#### `RefusalProblemCatalogCheckTests.CheckAsync_WithCatalog_ListsMappingsContributorsAndOutcomeDefaults`

- **Use case**: diagnostic probe lists the catalog contents
- **Test kind**: Unit
- **Description**: runs the `litebus.problem-catalog` probe against a registered catalog
- **Behavior**: checks the catalog
- **Expected outcome**: healthy result naming the mappings, contributors, and outcome defaults
- **Remarks**: `tests/LiteBus.Extensions.UnitTests/AspNetCore/Problems/RefusalProblemCatalogCheckTests.cs`

#### `RefusalProblemCatalogCheckTests.CheckAsync_WithoutCatalog_ReportsHealthyWithDefaultCatalog`

- **Use case**: diagnostic probe without a registered catalog
- **Test kind**: Unit
- **Description**: runs the `litebus.problem-catalog` probe with no catalog registered
- **Behavior**: checks for the catalog
- **Expected outcome**: healthy result reporting the shared default catalog
- **Remarks**: `tests/LiteBus.Extensions.UnitTests/AspNetCore/Problems/RefusalProblemCatalogCheckTests.cs`

#### `LiteBusExceptionHandlerTests.AddLiteBusExceptionHandler_SeedsProblemOptionsWithUnmappedPolicy`

- **Use case**: registration seeds the shared rendering options with the unmapped policy
- **Test kind**: Unit
- **Description**: registers with a configuration action setting `TreatAsServerError` and no explicit problem options
- **Behavior**: builds the service collection
- **Expected outcome**: the seeded `RefusalProblemOptions` singleton carries the configured policy
- **Remarks**: `tests/LiteBus.Extensions.UnitTests/AspNetCore/Problems/LiteBusExceptionHandlerTests.cs`

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
