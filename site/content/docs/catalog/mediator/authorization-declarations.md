# Authorization Declarations

- **ID**: `mediator.authorization-declarations`
- **Name**: Authorization declarations
- **Maturity**: GA
- **Summary**: States each message's authorization position beside the message, enforced by one guard for every message.
- **Guide**: [Authorization declarations](../../concepts/authorization-declarations.md)

## What It Does

Every command states its authorization position beside the message. Either it requires authorization under a policy, or it is deliberately exempt and says why. LiteBus carries that position from the message to the metadata, where one guard enforces it for every message. A position is an `AuthorizationDeclaration`, read back through `IMessageMetadataAccessor` whichever shape declared it. The two positions are separate types, `AuthorizedDeclaration` carrying the policy and `AuthorizationExemptDeclaration` carrying the rationale, so no declaration can hold a meaningless combination.

A position takes four shapes: the `[Authorized]` and `[AuthorizationExempt]` attributes, the keyed `IAuthorizationDefinition<TMessage>`, and the `Authorized` and `AuthorizationExempt` helpers in a `Describe` body. When both an attribute and a definition declare the same message, the definition wins, because both contribute the same `AuthorizationDeclaration` and the definition is applied last. LiteBus never evaluates the policy. Whether the actor satisfies it stays in the application.

Enforcement is a guard derived from `AuthorizationGuard<TMessage>`, registered once as an open generic. The guard reads the declaration for the runtime message type, allows what is exempt, and calls `AuthorizeAsync` with the declared policy on everything else. A message that declares nothing is denied with the `declaration.missing` code. For a declaration of any other value type, `DeclarationGuard<TMessage, TValue>` applies the same shape. A declared value reaches the typed `DecideAsync`, a recorded exemption allows without calling it, and an undeclared message reaches `DecideUndeclaredAsync`.

## Public Surface

```csharp
[Authorized("vaults.open")]
public sealed record OpenVaultCommand(Guid VaultId) : ICommand;

public sealed class VaultAuthorizationGuard<TCommand> : AuthorizationGuard<TCommand>
    where TCommand : ICommand
{
    private readonly IAccessAuthorizer _authorizer;

    public VaultAuthorizationGuard(IMessageMetadataAccessor metadata, IAccessAuthorizer authorizer)
        : base(metadata)
    {
        _authorizer = authorizer;
    }

    protected override async Task<Verdict> AuthorizeAsync(string policy, TCommand message, CancellationToken cancellationToken)
    {
        return await _authorizer.HoldsAsync(policy, cancellationToken).ConfigureAwait(false)
            ? Verdict.Allow
            : Verdict.Deny($"the caller is not authorized under {policy}");
    }
}

registry.AddMessaging(messaging => messaging.RequireDeclaration<AuthorizationDeclaration, ICommand>());
registry.AddCommands(builder => builder.Register(typeof(VaultAuthorizationGuard<>)));
```

| API | Role |
| --- | --- |
| `AuthorizationDeclaration` | The closed base of the two authorization positions, with `Authorized(policy)` and `Exempt(rationale)` factories |
| `AuthorizedDeclaration.Policy` | The required policy, in the application's own vocabulary |
| `AuthorizationExemptDeclaration.Rationale` | The recorded reason the message needs no authorization |
| `[Authorized]` / `[AuthorizationExempt]` | Attribute-form authorization declaration |
| `IAuthorizationDefinition<TMessage>` | Definition-form authorization declaration, exposing `Authorization` |
| `IMessageDeclarations.Authorized` / `AuthorizationExempt` | The `Describe` helpers. The exemption helper records the exemption that a requirement reads |
| `DeclarationGuard<TMessage, TValue>` | Guard base over any declaration value type, with a typed `DecideAsync`, an exemption that allows without calling it, and `DecideUndeclaredAsync` denying with `UndeclaredCode` (`declaration.missing`) by default |
| `AuthorizationGuard<TMessage>` | Guard base over authorization positions. An exempt position allows, and an authorized position delegates to `AuthorizeAsync` with the declared policy |
| `MessageModuleBuilder.RequireDeclaration<AuthorizationDeclaration, TScope>()` | Fails composition for every message in scope that states no authorization position |
| Analyzer `LB1020` | Compile-time report for a message stating no position on a configured declaration, here `AuthorizationDeclaration` |

## Packages

- `LiteBus.Messaging.Abstractions` (declarations, attributes, guard bases, accessor)
- `LiteBus.Messaging` (`RequireDeclaration`, definition collection)
- `LiteBus.Commands` (axis registration for the guard)

## Requires

- `mediator.handler-pipeline`
- `mediator.message-definitions`
- `mediator.open-generic-handlers` (one open generic guard covers every message)

## Invariants

- The lookup reads the runtime message type, so a guard closed over a base type still enforces the concrete message's declaration.
- A recorded exemption allows without calling the typed decision method.
- An undeclared message is denied with the `declaration.missing` code unless `DecideUndeclaredAsync` is overridden. Pair the guard with `RequireDeclaration` so the override becomes unreachable once every message states its position.
- Scope a requirement on a type rather than a namespace, so a refactoring cannot silently stop the rule from applying.
- LiteBus never interprets the policy string. Evaluation stays in the application's `AuthorizeAsync`.

## Non-Goals

- Modelling identity, roles, or tenancy. The guard reads those from whatever principal the host provides.
- A fixed policy vocabulary. The policy string belongs to the application.
- Compile-time policy evaluation. `LB1020` reports a missing position, never a wrong one.

## Observability

No authorization-specific meter or activity source. Denials report `MediationOutcome.Denied` through mediation telemetry and are recorded as denials by the audit trail, with the guard's reason.

Operational alternatives:
- Group the mediation decision counter by the deciding guard to see which policy denies most.
- Read the audit trail for denied actions and their reasons.

## Test Coverage

### Covered

| Test method | Project |
| --- | --- |
| `A_declared_value_reaches_the_typed_DecideAsync` | `LiteBus.Mediator.UnitTests` |
| `An_attribute_declared_value_reaches_the_typed_DecideAsync` | `LiteBus.Mediator.UnitTests` |
| `A_guard_closed_over_a_base_type_reads_the_concrete_message_declaration` | `LiteBus.Mediator.UnitTests` |
| `An_undeclared_message_is_denied_with_the_stable_code` | `LiteBus.Mediator.UnitTests` |
| `An_undeclared_message_can_be_allowed_by_overriding` | `LiteBus.Mediator.UnitTests` |
| `An_exemption_allows_without_calling_the_typed_DecideAsync` | `LiteBus.Mediator.UnitTests` |
| `One_open_generic_guard_covers_every_command` | `LiteBus.Mediator.UnitTests` |
| `An_exempt_message_is_allowed_without_authorizing` | `LiteBus.Mediator.UnitTests` |
| `The_declared_policy_reaches_AuthorizeAsync` | `LiteBus.Mediator.UnitTests` |
| `A_denial_from_AuthorizeAsync_refuses_the_message` | `LiteBus.Mediator.UnitTests` |
| `One_open_generic_guard_covers_authorized_and_exempt_commands` | `LiteBus.Mediator.UnitTests` |

### Untested

- A guard reading a declaration through a predicate-scoped requirement where the scope and the guard constraint disagree.
- Authorization of stream queries through an open generic query guard.

### Out-of-Scope

- Authentication middleware and principal construction.
- Policy storage and administration user interfaces.

## References

- [Authorization declarations](../../concepts/authorization-declarations.md)
- [Message definitions](../../concepts/message-definitions.md)
- [The handler pipeline](../../concepts/handler-pipeline.md)
- [Required declarations](../analyzers/required-declarations.md)
