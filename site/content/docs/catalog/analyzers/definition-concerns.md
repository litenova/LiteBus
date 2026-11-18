# Definition Concerns

Authorization, audit, tenancy and idempotency are unrelated concerns. Write one definition class per concern, such as `PlaceOrderCommandAuthorizationDefinition` beside `PlaceOrderCommandAuditDefinition`, so each decision stays readable and reviewable on its own. `LB1028` is an informational note that suggests the split when a class implements two `IMessageDefinition<TMessage, TValue>` contracts.

## Header

- **ID**: `analyzers.definition-mixes-concerns`
- **Diagnostic**: `LB1028` (Info)
- **Maturity**: GA
- **Summary**: Reports a definition class that declares more than one message definition concern.

## Trigger Conditions

`LB1028` reports a class when:

- It implements two or more distinct `IMessageDefinition<TMessage, TValue>` value types, through `IAuditDefinition<TMessage>`, `IAuthorizationDefinition<TMessage>`, `ITenancyDefinition<TMessage>`, `IIdempotencyDefinition<TMessage>`, or `IMessageDefinition<TMessage, TValue>` directly.
- It is declared in the analyzed assembly.

The diagnostic lists the concern names and suggests the split class name for the first concern, such as `PlaceOrderCommandAuthorizationDefinition`. A class declaring one value type for two messages is not reported: that is one concern, not a mix. Declaring the same value through both its named contract and the open generic contract counts once for the same reason.

The registry accepts one class implementing several definitions, so the rule is advice, not an error. Only a second declaration of the same value for the same message is refused at registration. See [Analyzers](../../reference/analyzers.md).

## Bad Example

```csharp
using LiteBus.Commands.Abstractions;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.Auditing;

public sealed record PlaceOrderCommand : ICommand;

public sealed class PlaceOrderCommandDefinition
    : IAuthorizationDefinition<PlaceOrderCommand>, IAuditDefinition<PlaceOrderCommand>
{
    public AuthorizationDeclaration Authorization => AuthorizationDeclaration.Authorized("orders.place");

    public AuditDeclaration Audit => AuditDeclaration.Audited("orders.place-order");
}
```

Expected diagnostic:

- `LB1028` for `PlaceOrderCommandDefinition`, declaring authorization and audit.

## Good Example

```csharp
using LiteBus.Commands.Abstractions;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.Auditing;

public sealed record PlaceOrderCommand : ICommand;

public sealed class PlaceOrderCommandAuthorizationDefinition : IAuthorizationDefinition<PlaceOrderCommand>
{
    public AuthorizationDeclaration Authorization => AuthorizationDeclaration.Authorized("orders.place");
}

public sealed class PlaceOrderCommandAuditDefinition : IAuditDefinition<PlaceOrderCommand>
{
    public AuditDeclaration Audit => AuditDeclaration.Audited("orders.place-order");
}
```

Each concern has its own class beside the message, so a reviewer can approve the authorization position without reading the audit position.

## Suppression Guidance

- Split the class instead of suppressing. The note means two unrelated decisions share one declaration site.
- Suppress only for a transitional definition that is being split across commits, and remove the suppression with the split.
- Keep suppression at class scope, with a comment naming the concern that stays.

## Test Coverage

Source: `tests/LiteBus.Analyzers.UnitTests/DefinitionMixesConcernsAnalyzerTests.cs`

| Test method | Verifies |
| --- | --- |
| `SingleConcern_ProducesNoDiagnostic` | A definition declaring one concern is accepted |
| `TwoConcerns_ProducesDiagnostic` | Authorization mixed with audit reports `LB1028` |
| `ThreeConcerns_ProducesDiagnostic` | Three mixed concerns are all listed in report order |
| `SameConcernForDifferentMessages_ProducesNoDiagnostic` | One concern for two messages is not a mix |
| `SameValueThroughNamedAndDirectContracts_ProducesNoDiagnostic` | One value through two contract shapes counts once |
| `DirectCustomValueMix_ProducesDiagnostic` | A named concern mixed with a directly implemented custom value reports `LB1028` |
| `PlainClass_ProducesNoDiagnostic` | A class implementing no definition contract is out of scope |
| `Descriptor_IsInfoInDeclarationsCategoryAndEnabledByDefault` | Rule is an informational note in `LiteBus.Declarations`, on by default |
