# Authorization Declarations

Every command states its authorization position beside the message: either it requires authorization under a policy, or it is deliberately exempt and says why. LiteBus carries that position from the message to the metadata, where one guard enforces it for every message. This page explains the four shapes a position takes, how to require one of every message, and where the framework's part ends.

## What a Position Declares

An authorization position answers one question: what must the caller be permitted to do before this message is mediated? The answer is a policy in your own vocabulary, a permission name, a role, or whatever your guards read:

```csharp
[Authorized("vaults.open")]
public sealed record OpenVaultCommand(Guid VaultId) : ICommand;
```

A message that needs no authorization says so, with the reason written down:

```csharp
[AuthorizationExempt("the vault catalogue is public, so there is no actor to authorize")]
public sealed record BrowseVaultCatalogueCommand : ICommand;
```

An exemption is a decision, not an omission. Recording the rationale beside the message is what makes the selection of authorized messages reviewable, and it is what lets a requirement tell a considered exemption apart from a message nobody looked at.

LiteBus never evaluates the policy. Declaring `vaults.open` authorizes nothing by itself; it states what the message requires, and your application decides whether the current actor holds it. That division is what keeps the framework honest: what a use case requires is a declaration LiteBus carries, and whether the actor satisfies it stays in your application, because no messaging library can model your tenancy and role structure.

## The Four Shapes

A position is an `AuthorizationDeclaration`, read back through [`IMessageMetadataAccessor`](message-definitions.md#reading-metadata) whichever shape declared it. The two positions are separate types, `AuthorizedDeclaration` carrying the policy and `AuthorizationExemptDeclaration` carrying the rationale, so a declaration cannot hold a combination that means nothing.

With an attribute, when the declaration is a single fact, as above.

With a keyed definition, when you want real types and shared constants:

```csharp
public sealed class OpenVaultCommandDefinition : IAuthorizationDefinition<OpenVaultCommand>
{
    public AuthorizationDeclaration Authorization => AuthorizationDeclaration.Authorized(Policies.Vaults.Open);
}
```

With `Describe`, once a message declares more than one value:

```csharp
internal sealed class OpenVaultCommandDefinition : IMessageDefinition<OpenVaultCommand>
{
    public void Describe(IMessageDeclarations declarations)
    {
        declarations.Authorized("vaults.open");
        declarations.Audited("vaults.open-vault", category: "security");
    }
}
```

An exemption in `Describe` form records both halves, exactly as `[AuthorizationExempt]` does: the declaration a guard reads, and the exemption a requirement reads:

```csharp
declarations.AuthorizationExempt("a liveness probe has no actor to authorize");
```

A definition wins when both an attribute and a definition declare the same message, because both contribute the same `AuthorizationDeclaration` and the definition is applied last. See [Message Definitions](message-definitions.md) for the mechanism all four shapes share, including family defaults and precedence.

## Requiring a Position

A declaration is only as good as its coverage. One command that forgets to state its position looks exactly like a command that needs no authorization, and a guard that allows what is undeclared turns that omission into an unguarded use case. Require the declaration at composition time, scoped to the messages the rule is actually about:

```csharp
registry.AddMessaging(messaging => messaging
    .RequireDeclaration<AuthorizationDeclaration, ICommand>());
```

Every registered command must then declare its position or record an exemption from it, and composition fails naming every offender rather than the first. Scope on a type rather than a namespace: a namespace is a string a refactoring tool moves without telling anyone, so a requirement keyed on one silently stops applying when a folder is renamed, and for an authorization rule that failure is a command left unguarded.

At compile time, with `LB1020`:

```ini
# .editorconfig
[*.cs]
litebus_required_declarations = LiteBus.Messaging.Abstractions.AuthorizationDeclaration
dotnet_diagnostic.LB1020.severity = warning
```

Use both. The analyzer reports the omission on the message itself while you are writing it, and it sees all four shapes: the attribute, the exemption attribute, the keyed definition, and both `Describe` helpers. The composition check covers a message registered from an assembly the analyzer never saw. See [Required Declarations](../catalog/analyzers/required-declarations.md) for the full rule.

## Enforcing It in a Guard

One guard covers every message that declares a policy. Derive it from `AuthorizationGuard<TMessage>` and implement only the check itself:

```csharp
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
```

Register it once as an open generic and it covers every command:

```csharp
registry.AddCommands(builder => builder.Register(typeof(VaultAuthorizationGuard<>)));
```

The guard reads the declaration for the runtime message type, allows what is exempt, and calls `AuthorizeAsync` with the declared policy on everything else. A message that declares nothing is denied with the `declaration.missing` code, because a use case nobody considered must not pass a check nobody wrote it for. Override `DecideUndeclaredAsync` to allow while the declaration is still being rolled out, and pair the guard with the requirement above so the override becomes unreachable once every message states its position. A denied message is refused before the handler runs, and the denial is recorded like any other mediation outcome.

## What LiteBus Deliberately Leaves to You

- **Whether the actor satisfies the policy.** LiteBus does not model identity, roles, or tenancy. Your guard reads the declared policy and answers from whatever principal your host provides.
- **What a policy means.** The policy string is your vocabulary and LiteBus never interprets it. Keep the set closed where you construct the declaration, with shared constants a rename can find.
- **Where the check runs.** A guard denies before the handler; an application that refuses by throwing its own exception owns that type and its classification. Either way, enforcement is application code.

## Next

Read [Message Definitions](message-definitions.md) for the declaration mechanism, and [Auditing](auditing.md) for the declaration this one mirrors and the trail it feeds.
