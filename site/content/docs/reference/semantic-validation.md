# Documentation Semantic Validation

LiteBus documentation CI has a fast structural gate and a post-build semantic gate. The structural gate checks Markdown, links, path casing, prose rules, and the evaluated package inventory. The semantic gate runs after the Release build and checks claims that must stay aligned with executable code.

## Enforced Contracts

| Contract | Source of truth | Gate |
| --- | --- | --- |
| Shipping package inventory | Evaluated `IsPackable`, `PackageId`, `ProjectReference`, and directly declared `PackageReference` MSBuild items | `Get-PackageInventory.ps1` regenerates `architecture/generated-package-inventory.md`; structural validation rejects a byte-level difference |
| C# snippets | Marked regions in compiled sample projects | The semantic gate compares the Markdown fence with its source region and rebuilds the owning project |
| Referenced tests | Tests discovered from the built solution, plus methods declared by abstract test bases | Every test class and method reference in `docs/` must resolve |
| Analyzer rules | `LiteBus.Analyzers/DiagnosticIds.cs` | Implemented IDs must match the analyzer table; LB1002 must remain the only reserved slot |
| ASP.NET management routes | `LiteBusManagementEndpointExtensions` | The documented inbox route table and health endpoint must match mapped methods and paths |

Run both gates after a Release build:

```powershell
./scripts/Test-Documentation.ps1
./scripts/Test-DocumentationSemantics.ps1 -Configuration Release
```

## Source-Linked Snippets

Place a named region in a compiled source file:

```csharp
// <docs-snippet example-name>
services.AddLiteBus(liteBus =>
{
});
// </docs-snippet>
```

Place the source marker immediately before the matching Markdown fence:

````markdown
<!-- snippet-source: samples/MySample/Registration.cs#example-name -->
```csharp
services.AddLiteBus(liteBus =>
{
});
```
````

The semantic gate removes common source indentation before comparing. The Markdown block must otherwise match exactly.

## Executable Capability Anchors

These tests are executable anchors for claims that must stay aligned with runtime behavior:

| Capability claim | Executable anchor |
| --- | --- |
| Inbox and outbox dead-letter replay routes are mapped and callable | `ManagementEndpointOperationsTests.ManagementRoutes_WithBothAxes_ShouldReturnSuccessfulResponses` |
| One root transport can serve inbox dispatch, outbox dispatch, and inbox ingress | `BrokerDispatchIngressRegistrationTests.RegisteredTransport_ShouldSupportCombinedDurableComposition` |
| Event mediation supports multiple handlers | `EventModuleTests.mediating_simple_event_goes_through_registered_handlers_correctly` |
| A real broker preserves the common transport contract | `RabbitMqTransportContractTests.PublishAsync_ThenConsume_PreservesPayloadAndHeaders` |

The normal unit and integration jobs execute these tests after semantic discovery verifies their names. A renamed or removed anchor therefore breaks documentation CI before the claim becomes stale.
