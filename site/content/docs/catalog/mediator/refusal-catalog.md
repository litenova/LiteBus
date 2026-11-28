# Refusal Catalog

- **ID**: `mediator.refusal-catalog`
- **Name**: Refusal catalog
- **Maturity**: GA
- **Summary**: Registers what each refusal code means once, so every transport renders product refusals from one registry.
- **Guide**: [Validation](../../concepts/validation.md)

## What It Does

A product refusal code needs a meaning every transport agrees on: whether retrying can help, what kind of refusal it is, and the stable title clients read. Without a shared registry each transport keeps its own map, so an HTTP problem title, a gRPC status, and a dead-letter reason for the same code drift apart, and a code the application forgot to map answers a silent fallback. The refusal catalog registers the transport-neutral meaning once, on the messaging builder, and each transport reads it.

An application maps its codes beside the LiteBus defaults, naming its module as the owner so the composition model shows which module owns each answer:

```csharp
registry.AddMessaging(messaging =>
{
    messaging.Refusals.Map("billing.unpaid", RefusalKind.Conflict, "Billing conflict", retryable: true, owner: "billing");
    messaging.Refusals.Map("billing.*", RefusalKind.Denied, "Billing denied", retryable: false, owner: "billing");
});
```

A code ending in `.*` is a prefix pattern covering every code under that prefix. Resolution is the exact code, then the longest prefix, then nothing: the catalog reports whether a mapping matches and leaves defaulting to the transport, so there is one defaulting story per transport instead of a neutral default competing with it. The ASP.NET Core adapter reads the registry into problem details (see [ASP.NET Core Problem Details](../../integrations/aspnetcore-problem-details.md)); any other transport reads `IRefusalCatalog` from its container the same way.

A message can also declare the codes it produces beside the message itself, through a definition class, an attribute, a family default, a convention, or a multi-message declaration source. At composition, every message's declarations are harvested into the same catalog, so a declaration-fed mapping and a builder mapping for one pattern follow the same conflict rule:

```csharp
internal sealed class BillCustomerCommandDefinition : IMessageDefinition<BillCustomerCommand>
{
    public void Describe(IMessageDeclarations declarations)
    {
        declarations.Refusals(
            new RefusalDeclaration("billing.unpaid", RefusalKind.Conflict, "Billing conflict", Retryable: true));
    }
}
```

Each harvested entry is owned by the definition or attribute type that declared it, or by the scope a default or convention declared it for, so the `refusals` composition section shows which declaration fed each answer.

## Public Surface

```csharp
IRefusalWriter Map(string code, RefusalKind kind, string title, bool retryable, string? owner = null);
IRefusalWriter Replace(string code, RefusalKind kind, string title, bool retryable, string? owner = null);
bool TryResolve(string? code, out RefusalEntry? entry);
```

| API | Role |
| --- | --- |
| `MessageModuleBuilder.Refusals` | Registers refusal code meanings at composition time |
| `IRefusalWriter.Map(code, kind, title, retryable, owner?)` | Maps a code or prefix pattern; an identical repeat is a no-op and a conflicting repeat fails composition naming both sources |
| `IRefusalWriter.Replace(code, kind, title, retryable, owner?)` | Deliberately overrides a mapping |
| `IRefusalReader.TryResolve(code, out entry)` | Resolves the exact, then longest-prefix, mapping; reports whether one matches |
| `IRefusalReader.Entries` | Lists every mapping with its owner, ordered by pattern |
| `IRefusalCatalog` | Extends the writer and the reader; the dependency injection singleton key |
| `IMessageDeclarations.Refusals(...)` | Declares the refusal codes one message can produce; harvested into the catalog at composition |
| `RefusalDeclaration` | One declared code: code, kind, title, and retryability |
| `RefusalDeclarations` | The per-message holder for a message's declared codes |
| `RefusalEntry` | One mapping: pattern, kind, title, retryability, and owner |
| `RefusalKind` | The closed set of kinds: denied, invalid, conflict, not found, unavailable |
| `RefusalProblemCatalog.MapFrom(refusals)` | Copies neutral entries into an HTTP problem catalog through the conflict rule |
| `RefusalProblemCatalog.CreateFrom(refusals)` | Builds an HTTP problem catalog from the neutral registry alone |
| `AddRefusalProblemCatalog(configure?)` | Registers the HTTP problem catalog built from the composed registry |

## Packages

- `LiteBus.Messaging.Abstractions` (`IRefusalCatalog`, `IRefusalReader`, `IRefusalWriter`, `RefusalDeclaration`, `RefusalDeclarations`, `RefusalEntry`, `RefusalKind`)
- `LiteBus.Messaging` (the catalog, the `Refusals` builder surface, the declaration harvest, the `refusals` composition section)
- `LiteBus.Extensions.AspNetCore` (`MapFrom`, `CreateFrom`, `AddRefusalProblemCatalog`)

## Requires

- `mediator.handler-pipeline` (guards and validators supply the codes the catalog describes)
- `mediator.fault-refusal-classification` (a classified fault carries a code the catalog can describe)

## Invariants

- Registrations follow one conflict rule across every source: an identical repeat is a no-op keeping the first owner, a conflicting repeat fails composition naming the pattern and both mappings, and a deliberate override calls `Replace`.
- A prefix pattern covers codes under its prefix only: it does not match its own bare prefix, and an exact code does not match longer codes that start with it.
- A fault is never an entry: registering a known `MessageFaultException` code throws, and a `Failed` outcome bypasses every catalog to answer a server error.
- The composition model names every entry with its owner in the `refusals` section, so CI diffs and compliance records show the full refusal list.
- An unmapped denied code is always reported: a warning when answered from the denied default, an error when reported as a server error. An unmapped invalid code logs at debug level on the fallback path, because per-validator codes are unmapped by design.
- Per-message refusal declarations are harvested into the catalog through `Map`, so a declaration-fed mapping and a builder mapping for one pattern follow the same conflict rule: an identical repeat is a no-op, and a conflicting repeat fails composition naming both.

## Non-Goals

- HTTP defaulting. Outcome defaults and the fallback stay on the problem catalog, which the adapter owns.
- Fault-code registration. Faults bypass catalogs by design.
- A compile-time rule requiring registration. Codes are runtime strings, so enforcement is the composition section, the diagnostic probe, the unmapped-code policy, and fallback logging.

## Observability

No catalog-specific meter or activity source. The `refusals` composition section lists every entry with its owner, the `litebus.problem-catalog` probe lists the HTTP mappings, and unmapped codes log through the problem details factory (warning for a denied code answered from the default, debug for an invalid one, error when reported as a server error).

Operational alternatives:
- Diff the composition export between builds to catch a removed or renamed refusal code.
- Group the mediation decision counter by refusal code to find codes no mapping covers.

## Test Coverage

### Covered

| Test method | Project |
| --- | --- |
| `TryResolve_KnownCode_MatchesEntry` | `LiteBus.Mediator.UnitTests` |
| `TryResolve_KnownCode_ReportsKindAndRetryability` | `LiteBus.Mediator.UnitTests` |
| `TryResolve_UnknownCode_ReportsNoMatch` | `LiteBus.Mediator.UnitTests` |
| `TryResolve_ExactMapping_WinsOverPrefixPattern` | `LiteBus.Mediator.UnitTests` |
| `TryResolve_LongestPrefix_WinsWhenSeveralPatternsCoverOneCode` | `LiteBus.Mediator.UnitTests` |
| `TryResolve_ExactCode_DoesNotMatchLongerCodes` | `LiteBus.Mediator.UnitTests` |
| `TryResolve_PrefixPattern_DoesNotMatchTheBarePrefix` | `LiteBus.Mediator.UnitTests` |
| `Map_IdenticalRepeat_IsNoOpKeepingFirstOwner` | `LiteBus.Mediator.UnitTests` |
| `Map_ConflictingRemap_ThrowsNamingCodeAndBothMappings` | `LiteBus.Mediator.UnitTests` |
| `Replace_ExistingMapping_OverridesMapping` | `LiteBus.Mediator.UnitTests` |
| `Replace_NewPattern_AddsMapping` | `LiteBus.Mediator.UnitTests` |
| `Entries_ListsEveryMappingWithItsOwnerOrderedByPattern` | `LiteBus.Mediator.UnitTests` |
| `CreateDefault_RegistersNoFaultCode` | `LiteBus.Mediator.UnitTests` |
| `CompositionSection_NamesRefusalsAndListsEntries` | `LiteBus.Mediator.UnitTests` |
| `CompositionSection_EmptyCatalog_ContributesNothing` | `LiteBus.Mediator.UnitTests` |
| `Map_BlankCode_Throws` | `LiteBus.Mediator.UnitTests` |
| `Map_NullKind_Throws` | `LiteBus.Mediator.UnitTests` |
| `Map_BlankTitle_Throws` | `LiteBus.Mediator.UnitTests` |
| `Map_MalformedPattern_Throws` | `LiteBus.Mediator.UnitTests` |
| `ComposedCatalog_ResolvesProductMapping` | `LiteBus.Mediator.UnitTests` |
| `ValidateComposition_ModelCarriesRefusalsSection` | `LiteBus.Mediator.UnitTests` |
| `ToJson_CarriesRefusalsSection` | `LiteBus.Mediator.UnitTests` |
| `ConflictingMappingsAcrossCallbacks_FailCompositionNamingBoth` | `LiteBus.Mediator.UnitTests` |
| `IdenticalMappingsAcrossCallbacks_ComposeKeepingFirstOwner` | `LiteBus.Mediator.UnitTests` |
| `CreateFrom_MapsKindToStatus` | `LiteBus.Extensions.UnitTests` |
| `CreateFrom_AnswersNotSignedInWithUnauthorized` | `LiteBus.Extensions.UnitTests` |
| `CreateFrom_CarriesOwnerAsContributor` | `LiteBus.Extensions.UnitTests` |
| `MapFrom_IdenticalNeutralEntries_AreNoOpOnDefaults` | `LiteBus.Extensions.UnitTests` |
| `MapFrom_ConflictingNeutralEntry_ThrowsNamingBoth` | `LiteBus.Extensions.UnitTests` |
| `AddRefusalProblemCatalog_ResolvesCatalogFromComposedRefusals` | `LiteBus.Extensions.UnitTests` |
| `AddRefusalProblemCatalog_ConfigureCallback_MapsExtraCodes` | `LiteBus.Extensions.UnitTests` |
| `AddRefusalProblemCatalog_ExistingRegistration_Wins` | `LiteBus.Extensions.UnitTests` |
| `AddRefusalProblemCatalog_MissingComposition_ThrowsNamingAddMessaging` | `LiteBus.Extensions.UnitTests` |
| `ToHttpResult_UnmappedDeniedCode_AnswersDefaultAndLogsWarning` | `LiteBus.Extensions.UnitTests` |
| `ToHttpResult_MappedDeniedCode_LogsNothing` | `LiteBus.Extensions.UnitTests` |
| `ToHttpResult_UnmappedInvalidCode_AnswersDefaultAndLogsDebug` | `LiteBus.Extensions.UnitTests` |
| `HarvestedDefinitionRefusals_ResolveFromComposedCatalog` | `LiteBus.Mediator.UnitTests` |
| `HarvestedRefusals_AppearInRefusalsSection` | `LiteBus.Mediator.UnitTests` |
| `DeclaredDefaultRefusals_AreHarvested` | `LiteBus.Mediator.UnitTests` |
| `CatalogSourceRefusals_AreHarvested` | `LiteBus.Mediator.UnitTests` |
| `ConflictingDeclarationAndRegistration_FailsCompositionNamingBoth` | `LiteBus.Mediator.UnitTests` |
| `IdenticalDeclarationAndRegistration_ComposeKeepingFirstOwner` | `LiteBus.Mediator.UnitTests` |
| `Map_FaultCode_Throws` | `LiteBus.Mediator.UnitTests` |
| `Replace_FaultCode_Throws` | `LiteBus.Mediator.UnitTests` |
| `RefusalDeclarations_ConflictingDuplicates_Throw` | `LiteBus.Mediator.UnitTests` |
| `RefusalDeclarations_IdenticalDuplicates_KeepFirst` | `LiteBus.Mediator.UnitTests` |

### Untested

- A gRPC host reading the composed registry end to end (no gRPC adapter ships yet).

### Out-of-Scope

- HTTP outcome-default tuning, covered by the problem details tests.
- OpenAPI schema output for seeded catalogs.

## References

- [The handler pipeline](../../concepts/handler-pipeline.md)
- [Message definitions](../../concepts/message-definitions.md)
- [Validation](../../concepts/validation.md)
- [ASP.NET Core Problem Details](../../integrations/aspnetcore-problem-details.md)
