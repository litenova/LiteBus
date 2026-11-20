# Authorization Policies

An authorization declaration states what a message requires, and a policy decides whether the caller satisfies it. Before this model, each product wrote its own policy engine for the same checks: signed-in, permission, claim, recent sign-in, system, and ownership. Those checks now live in LiteBus once, as combinators over one three-outcome evaluation, with a permission port, a general decision point port, and a condition contribution point for the parts that stay in the application. This page explains the checks, how to combine and register them, and the order they run in.

## Selecting a Policy From a Declaration

A message selects its policy by declaring a name:

```csharp
[Authorized("vaults.open")]
public sealed record OpenVaultCommand(Guid VaultId) : ICommand;
```

The composition registers a `MessagePolicy` under that name, and the guard evaluates it. Names compare ordinally: a policy name is vocabulary the composition owns, and two names differing only by case are two policies. A composition that calls `AddAuthorizationPolicies` fails at startup for a name a message declares that no call registered, with a `LiteBusConfigurationException` that names every unknown policy name and the messages that use it, so a typo fails once where it was written instead of denying every request. An unknown name that reaches mediation anyway is a configuration fault, not a denial: the guard raises `MessageFaultException` carrying the `authorization.unknown-policy` fault code. `MessagePolicyRegistry.PolicyNames` exposes the registered names, so a `ValidateComposition` check can assert over declared policies without restating them. See [Authorization Declarations](authorization-declarations.md) for the declaration shapes that carry the name.

Registered and declared policy names are also visible to composition checks through the composition model. A `ValidateComposition` check reads `model.RegisteredPolicyNames` for the names the composition registered and `model.DeclaredPolicyNames` for the names messages declare, and fails composition naming every mismatch:

```csharp
messaging.ValidateComposition(model =>
{
    var unregistered = model.DeclaredPolicyNames
        .Except(model.RegisteredPolicyNames, StringComparer.Ordinal)
        .ToList();

    if (unregistered.Count > 0)
    {
        throw new InvalidOperationException(
            $"Messages declare policies no composition registered: {string.Join(", ", unregistered)}");
    }
});
```

## The Evaluation Input

Every check reads one `AuthorizationContext`, built once per guard decision. It carries the message being mediated as `Message` with its runtime type as `MessageType`, who the mediation runs for as `Identity` (the calling principal and the tenant the work belongs to), the mediation's dispatch scope as `Services`, the mediation clock as `Clock`, and the instant the mediation started as `HandledAt`, read once from the configured time provider. `Services` is the mediation's dispatch scope, not the application root, so a permission lookup resolved from it observes the same scoped services the handlers do. `ExecutionContext` is the mediation's execution context, set by the guard from the ambient execution context, so a policy can read the enclosing mediation the message arrived through. Every contributed condition and the named policy observe the same instance, so each stage of one decision reads the same message, identity, clock, and instant.

`AuthorizationContext` derives from `MessageEvaluationContext`, which is the same evaluation input every per-evaluation port receives. A port that needs only the shared input takes the base record; authorization policies and conditions take this type.

## The Built-In Checks

Each check is a static factory on the abstract `MessagePolicy` record. A check the factories cannot express is a subclass that overrides `EvaluateAsync`; the subclass must itself be a record, because only records may inherit from a record. A class fails to compile, so write a record with an explicit constructor. The guard treats a custom policy exactly like a built one, so the composition vocabulary and the evaluation order stay in one place whatever the checks are.

```csharp
public sealed record BusinessHoursPolicy : MessagePolicy
{
    public BusinessHoursPolicy()
    {
    }

    public override ValueTask<PolicyResult> EvaluateAsync(
        AuthorizationContext context,
        CancellationToken cancellationToken)
    {
        var hour = context.HandledAt.Hour;

        return new ValueTask<PolicyResult>(hour is >= 9 and < 17
            ? PolicyResult.Allow
            : PolicyResult.Deny(
                AuthorizationPolicyRefusalCodes.Forbidden,
                "Writes outside business hours need an on-call approval."));
    }
}
```

Policies are records, so two policies built the same way are equal. That equality is what lets the registration conflict rule treat an identical repeated `Add` as a no-op. No combinator copies a policy with `with`: each combinator wraps the policy it is given.

`SignedIn()` allows a signed-in caller and denies anyone else. Signed in means the mediation identity carries an authenticated principal; presence of a principal object alone does not count, because an anonymous request still carries one with an unauthenticated identity. A denial carries `authorization.not-signed-in`, so a boundary can answer it with an authentication challenge rather than a refusal.

`Tenant()` allows when the mediation identity carries a tenant and denies otherwise with the `tenancy.required` code. When an `ITenantIdentifierValidator` is registered, the tenant is checked through it and a malformed tenant denies with the `tenancy.invalid` code; with none registered, a tenant is accepted when it is not blank. It lets an application order sign-in, tenant, and permission checks inside one policy, as in `All(SignedIn(), Tenant(), Permission("vaults.open"))`, so a signed-out caller is asked to authenticate (401) before a tenant-less caller is refused (403). See [Evaluation Order](#evaluation-order) for the guard-level order this complements, and [Tenancy](tenancy.md#validating-tenant-identifiers) for the validator.

`Permission(string permission)` allows when the caller holds a permission, read through the registered `IPermissionLookup`. With none registered, the check faults with `authorization.permission-lookup-missing` rather than guessing. When the lookup answers `false`, the denial carries `authorization.missing-permission`.

`Claim(string type, string? value = null)` allows when the principal carries a claim of the given type, and of the given value when one is passed. A call with no value is the presence check: `Claim("person_id")` allows when the principal carries that type under any value, so no separate presence factory is needed. Type and value compare ordinally. A caller that is not signed in denies with `authorization.not-signed-in`; a signed-in caller without the claim denies with `authorization.forbidden`.

`RecentSignIn(TimeSpan maxAge)` allows when the last sign-in is recent. It reads the sign-in instant from the registered `ISignInInstantSource` when one is present, else from the `auth_time` claim, in Unix seconds, and compares its age against the handled instant of the mediation as a `TimeSpan`. The overload `RecentSignIn(TimeSpan maxAge, string claimType)` reads the named claim instead of `auth_time` when no source is registered. Both overloads hold a constant window. When the window varies by tenant, time, or configuration, resolve it per evaluation with `RecentSignIn(IRecentSignInWindow)` or `RecentSignIn<TWindow>()` instead: the window is read from the evaluation context on every evaluation, so a per-tenant setting and a changed configuration apply without rebuilding the composition. A caller that is not signed in denies with `authorization.not-signed-in` without resolving the window; a signed-in caller whose sign-in is older than the window denies with `authorization.forbidden`; a signed-in caller with a missing or unparsable sign-in instant faults with `authorization.sign-in-instant-unknown`. When the `TWindow` type is not registered in the dispatch scope, the policy faults with `authorization.recent-sign-in-window-missing` rather than guessing. See [Reading Sign-In Instants Through a Port](#reading-sign-in-instants-through-a-port) and [Resolving the Sign-In Window Per Evaluation](#resolving-the-sign-in-window-per-evaluation).

`System()` allows a system run and denies any call with a principal. A system run is a mediation whose identity carries no principal, such as a background reaction or a recurring job. A call with a principal denies with `authorization.forbidden`. A principal-less identity may still carry a tenant: system work inside a tenant runs with `ExecutionIdentity.For(null, tenantId)`, which passes `System()` and the tenancy position together. See [System Work Inside a Tenant](#system-work-inside-a-tenant).

`Self<TMessage>(Func<TMessage, string?> subjectId)` allows when the caller is the subject the message names. The principal's identifier is its `ClaimTypes.NameIdentifier` claim, else its `sub` claim, which is the same order the audit actor resolution reads, and the identifiers compare ordinally. A caller that is not signed in denies with `authorization.not-signed-in`. A message of another type, a message naming no subject, a principal carrying no identifier, and an identifier that differs all deny with `authorization.forbidden`: ownership that cannot be confirmed is refused rather than guessed.

`Condition<TCondition>()` evaluates one authorization condition where the policy names it. It resolves `TCondition` from the dispatch scope and answers its result, so the check runs only for the policies that name it, unlike a condition registered with `AddCondition`, which runs for every authorized message. Register the type with `RegisterCondition<TCondition>()` on the policy builder; when the type is not registered, the policy faults with `authorization.condition-missing` rather than guessing. A condition reports a `PolicyResult` like a policy: a denial carries its own code, and a fault, such as a port the condition reads being missing, ends the mediation as `Failed` (HTTP 500) instead of a denial.

`Decide<TDecisionPoint>(string action, string? resourceKind = null, string? resourceId = null)` answers the decision an authorization decision point makes for an action. It resolves `TDecisionPoint` from the dispatch scope on every evaluation and answers whatever it decides: allow, deny, or fault when the decision cannot be made. When the decision point type is not registered, the policy faults with `authorization.decision-point-missing` rather than guessing. See [Deciding Through a Decision Point](#deciding-through-a-decision-point).

`SentBy<TParent>()` allows when the enclosing mediation handles a message assignable to `TParent`, read from `AuthorizationContext.ExecutionContext.Parent.MessageType`. A root mediation, with no enclosing mediation, denies, as does any other caller, with `authorization.not-sent-by`. It admits a named caller without granting it the permission itself, as in `Any(Permission("vaults.open"), SentBy<ExportArchiveQuery>())`.

## Allow, Deny, and Fault

Policies evaluate to one of three results on the abstract `PolicyResult` record: `PolicyAllow`, `PolicyDeny(code, reason)`, or `PolicyFault(code, reason, cause)`. A denial is an expected, caller-actionable end with a stable refusal code and a human reason. A fault is a configuration or dependency failure an operator must fix: a missing permission lookup, an unknown sign-in instant, a missing decision point, an unregistered condition, an unregistered sign-in window, or an unknown policy name.

A fault is never a refusal. Combinators pass a fault through unchanged: `All` propagates it past a later allow, `Any` returns it when no branch was decidable, `When` never erases one, and `WithRefusal` rewrites denials only. The guard keeps the two-state `Verdict` at the pipeline boundary: an allow becomes `Verdict.Allow`, a denial becomes a denial verdict carrying the same code and reason, and a fault raises `MessageFaultException` carrying the fault code, reason, and cause. The mediation ends as `MediationOutcome.Failed`, the fault is logged, and transports answer it as a server error (500). An application refusal code never masks one.

## Combining Checks

`All(params MessagePolicy[])` allows when every inner policy allows, evaluated in order. The first result that is not an allow wins and evaluation stops there, so a later policy never overrides an earlier refusal, and a fault propagates without a later allow erasing it. An empty conjunction allows.

`Any(params MessagePolicy[])` allows when any inner policy allows, evaluated in order. The first allow wins and evaluation stops there. When nothing allows, the first denial is returned, so the refusal names the earliest requirement the caller missed; when no branch was decidable, the first fault is returned instead. An empty disjunction denies with `authorization.forbidden`.

`When(Func<AuthorizationContext, bool> condition, MessagePolicy inner)` applies an inner policy only when a condition holds. When the condition does not hold, the policy allows without evaluating the inner policy, because a rule that does not apply must not refuse.

`Public()` always allows. It is the permanent policy for public routes such as sign-in start and token refresh, where there is no caller to authorize yet. `Allow()` also always allows, but it is the branch of a `When` that needs no check, and the placeholder a composition holds until the real rule is written. Use `Public()` where the message is public by design, so a reviewer does not read it as an unfinished rule.

`Deny(string reason)` always denies with `authorization.forbidden`. It is how a composition closes a message while stating, in its own words, what to do instead.

`WithRefusal(string code, string? reason = null)` wraps a policy so a denial carries the given code and, when one is passed, the given reason. An allow passes through unchanged, and a fault passes through unchanged, because a configuration or dependency failure is never a user refusal. A denial keeps its decision and carries the replacement code with the replacement reason, else the inner reason. It lets an application keep its own refusal codes on built-in combinators, as in `MessagePolicy.Permission("vaults.open").WithRefusal("vaults.no-access", "the vault is sealed for stocktake")`.

`WithRefusal(string code, string? reason, string whenInnerCode)` rewrites one inner code. Only a denial carrying `whenInnerCode` is rewritten; any other denial, any allow, and any fault pass through unchanged. It lets an application rename one expected denial of a combinator, such as a missing permission, while leaving the combinator's other answers alone:

```csharp
policies.Add("books.post-entry", MessagePolicy.All(
    MessagePolicy.SignedIn(),
    MessagePolicy.Tenant(),
    MessagePolicy.Permission("books.post-entry"))
    .WithRefusal("books.entry-forbidden", "the workspace cannot post this entry."));
```

```csharp
policies.Add("vaults.open", MessagePolicy.All(
    MessagePolicy.SignedIn(),
    MessagePolicy.Permission("vaults.open")));

policies.Add("vaults.close", MessagePolicy.Any(
    MessagePolicy.Permission("vaults.close"),
    MessagePolicy.When(
        static context => context.Identity.HasTenant,
        MessagePolicy.Self<VaultCommand>(static command => command.VaultId.ToString()))));

policies.Add("vaults.seal", MessagePolicy.All(
    MessagePolicy.SignedIn(),
    MessagePolicy.Tenant(),
    MessagePolicy.Permission("vaults.seal")));
```

## Reading Permissions Through a Port

`IPermissionLookup` is the port a permission check reads through:

```csharp
ValueTask<bool> HasPermissionAsync(
    ExecutionIdentity identity,
    string permission,
    AuthorizationContext context,
    CancellationToken cancellationToken);
```

LiteBus carries the permission name from the policy to the lookup and never interprets it: whether `vaults.open` is a role, a scope, or a row in a permissions table stays in the application. It is the narrow string-permission convenience; a policy that needs a relationship or attribute decision answers through a decision point instead. See [Deciding Through a Decision Point](#deciding-through-a-decision-point). Register one implementation when a composition uses `MessagePolicy.Permission`:

```csharp
public sealed class RolePermissionLookup : IPermissionLookup
{
    public ValueTask<bool> HasPermissionAsync(
        ExecutionIdentity identity,
        string permission,
        AuthorizationContext context,
        CancellationToken cancellationToken)
    {
        return new ValueTask<bool>(identity.Principal?.IsInRole(permission) == true);
    }
}
```

## Reading Sign-In Instants Through a Port

`ISignInInstantSource` is the port a recent sign-in policy reads through:

```csharp
ValueTask<DateTimeOffset?> GetSignInInstantAsync(
    AuthorizationContext context,
    CancellationToken cancellationToken);
```

Register one implementation when the sign-in instant lives outside the token claims, such as in a server-side session record. With none registered, the policy reads the `auth_time` claim instead, or the claim type the `RecentSignIn` overload names. A source that treats an unknown instant as allowed returns `context.HandledAt`, which makes the sign-in age zero:

```csharp
public sealed class SessionSignInInstantSource : ISignInInstantSource
{
    public ValueTask<DateTimeOffset?> GetSignInInstantAsync(
        AuthorizationContext context,
        CancellationToken cancellationToken)
    {
        var stamped = context.Identity.Principal?.FindFirst("session_start")?.Value;

        return new ValueTask<DateTimeOffset?>(long.TryParse(stamped, out var unixSeconds)
            ? DateTimeOffset.FromUnixTimeSeconds(unixSeconds)
            : null);
    }
}
```

## Resolving the Sign-In Window Per Evaluation

`IRecentSignInWindow` is the port a resolving recent sign-in policy reads through:

```csharp
ValueTask<TimeSpan> GetWindowAsync(
    AuthorizationContext context,
    CancellationToken cancellationToken);
```

The window is a business setting, so it is resolved on every evaluation from the evaluation context: the tenant comes from the mediation identity, and settings and stores resolve from the dispatch scope, so one policy serves a workspace with a five-minute window and another with fifteen, and a changed setting applies without rebuilding the composition. The `TimeSpan` overloads stay as the simple form over a constant window; use this port when the window varies by tenant, time, or configuration:

```csharp
public sealed class WorkspaceSignInWindow : IRecentSignInWindow
{
    public ValueTask<TimeSpan> GetWindowAsync(
        AuthorizationContext context,
        CancellationToken cancellationToken)
    {
        var settings = (WorkspaceSettings)context.Services.GetService(typeof(WorkspaceSettings))!;

        return new ValueTask<TimeSpan>(settings.GetSignInWindow(context.Identity.TenantId!));
    }
}
```

Pass an instance with `RecentSignIn(window)` when the window needs no services of its own, or name the type with `RecentSignIn<TWindow>()` to resolve it from the dispatch scope on every evaluation. A caller that is not signed in denies with `authorization.not-signed-in` before the window is resolved, so no port is read for a caller already refused.

## Deciding Through a Decision Point

`IAuthorizationDecisionPoint` is the general port a decision policy reads through:

```csharp
ValueTask<PolicyResult> DecideAsync(
    AuthorizationDecisionRequest request,
    CancellationToken cancellationToken);
```

Relationship-based and attribute-based engines plug in per policy behind it, each in its own opt-in adapter package that no core package references, so a composition that answers permissions from roles pays nothing for the engines it does not run. The decision point answers the full three-outcome result: allow when the action is permitted, deny with a caller-facing refusal when it is not, and fault when the decision cannot be made, such as when the backing service is unreachable. The policy answers whatever the decision point returns.

The request carries who acts, what action on what message, which resource it targets, and when the mediation started:

```csharp
public sealed record AuthorizationDecisionRequest(
    ExecutionIdentity Identity,
    string Action,
    object Message,
    Type MessageType,
    string? ResourceKind,
    string? ResourceId,
    DateTimeOffset HandledAt);
```

It is built once per evaluation by the decision policy from the evaluation context and the action and resource the policy names. The cancellation token stays a method parameter on the decision point and never becomes a member of the request, so one request serves decisions with different lifetimes.

```csharp
public sealed class LedgerDecisionPoint : IAuthorizationDecisionPoint
{
    public ValueTask<PolicyResult> DecideAsync(
        AuthorizationDecisionRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Identity.Principal?.Identity?.IsAuthenticated is not true)
        {
            return new ValueTask<PolicyResult>(PolicyResult.Deny(
                AuthorizationPolicyRefusalCodes.NotSignedIn,
                "The action requires a signed-in caller."));
        }

        return new ValueTask<PolicyResult>(PolicyResult.Allow);
    }
}
```

```csharp
policies.Add("ledger.post", MessagePolicy.All(
    MessagePolicy.SignedIn(),
    MessagePolicy.Decide<LedgerDecisionPoint>("ledger.post", resourceKind: "ledger", resourceId: "general")));
```

Each policy names its own decision point type, so one composition runs a relationship check for one policy and a role lookup for another. A condition that loads domain state for one policy, such as loading an invoice to check the caller's relation to it, stores the loaded value in execution-context data for the handler instead of loading it twice. See [Contributing Conditions](#contributing-conditions).

## Contributing Conditions

An `IAuthorizationCondition` is a check one feature contributes that runs for every authorized message before the named policy:

```csharp
ValueTask<PolicyResult> EvaluateAsync(AuthorizationContext context, CancellationToken cancellationToken);
```

Conditions are how a module adds its own authorization rule without owning the policy vocabulary. A tenancy module denies a tenant-scoped message evaluated outside its tenant; a maintenance module denies writes while a migration runs. Each registers one condition, and the policy guard runs every registered condition, in registration order, before the policy the message declares. The first deny wins, so a condition never overrides another check that already refused. A condition returns `PolicyResult`: `Allow`, `Deny(code, reason)`, or `Fault(code, reason, cause)` when it cannot decide, such as when a port it reads is not registered. A fault ends the mediation as `Failed` and maps to HTTP 500, so a configuration problem never reads as a user refusal.

```csharp
public sealed class BusinessHoursCondition : IAuthorizationCondition
{
    public ValueTask<PolicyResult> EvaluateAsync(AuthorizationContext context, CancellationToken cancellationToken)
    {
        var hour = context.HandledAt.Hour;

        return new ValueTask<PolicyResult>(hour is >= 9 and < 17
            ? PolicyResult.Allow
            : PolicyResult.Deny("authorization.outside-business-hours", "writes outside business hours need an on-call approval"));
    }
}
```

A condition observes the same `AuthorizationContext` the named policy does. It must stay free of effects a caller would not want from a "may I" question, because the guard stage also runs when the pipeline is asked to evaluate a message without performing it.

A condition that belongs to one policy rather than every message is named by that policy with `Condition<TCondition>()` and registered with `RegisterCondition<TCondition>()`, which registers the type as itself, scoped, without making it global:

```csharp
registry.AddMessaging(messaging => messaging.AddAuthorizationPolicies(policies => policies
    .Add("invoicing.sales-invoice-access", MessagePolicy.Condition<SalesInvoiceAccessCondition>())
    .RegisterCondition<SalesInvoiceAccessCondition>()));
```

Use `AddCondition` for a rule that applies to every authorized message and `Condition` with `RegisterCondition` for a rule one policy owns, so messages that never name it never pay for it.

## Registering Policies

Register names, conditions, and the ports together on the messaging builder:

```csharp
registry.AddMessaging(messaging => messaging.AddAuthorizationPolicies(policies => policies
    .Add("vaults.open", MessagePolicy.All(MessagePolicy.SignedIn(), MessagePolicy.Permission("vaults.open")))
    .AddCondition<BusinessHoursCondition>()
    .UsePermissionLookup<RolePermissionLookup>()));
```

`Add` registers a policy under the name messages declare. Each name carries one policy: two policies under one name would leave the guard guessing which one the message meant. An identical repeat is a no-op, because policies are records that compare by value; a conflicting repeat fails composition naming both policies. `Replace` overwrites deliberately, which is how a composition replaces a policy with an explicit choice. `AddCondition<TCondition>` registers a condition the guard constructs from the dispatch scope; conditions run in registration order, so register the cheapest, most general condition first. `RegisterCondition<TCondition>` registers a policy-scoped condition type without making it global. `UsePermissionLookup<TLookup>` registers the lookup permission checks read through; only one lookup serves a composition, so a second call naming another type fails composition, while repeating the same type is harmless. `UseSignInInstantSource<TSource>` registers the source recent sign-in policies read through, with the same one-type rule.

The call registers `PolicyAuthorizationGuard<TMessage>` once as an open generic, so it covers every message, including messages registered later by the axis modules. The call is additive: each assembly in a modular monolith contributes its own `AddAuthorizationPolicies` call, and every call configures the same policy set. The guard, the build step, and the composition check are registered once, on the first call. A conflicting policy name still fails, and a second `UsePermissionLookup` or `UseSignInInstantSource` naming another type fails, so two assemblies cannot silently disagree about which port serves the composition.

```csharp
// From another assembly: adds to the same set rather than replacing it.
registry.AddMessaging(messaging => messaging.AddAuthorizationPolicies(policies => policies
    .Add("vaults.seal", MessagePolicy.Permission("vaults.seal"))));
```

## Events Publish Without a Declaration

Events are facts, not requests: publishing an event notifies subscribers that something already happened, so it asks nothing of the caller. The event module declares a default `AuthorizationExemptDeclaration` for the `IEvent` scope, so a composition with authorization policies publishes undeclared events, including events replayed from durable storage. An explicit per-event declaration still applies, because a declaration written closer to the message overrides the family default. A composition that already states its own `IEvent` authorization default keeps it: the duplicate is the application's explicit choice, not a conflict.

```csharp
// No declaration needed: the event module default exempts it.
public sealed record VaultOpenedEvent(Guid VaultId) : IEvent;

// An explicit position still applies where an event needs one.
[Authorized("vaults.audit")]
public sealed record VaultSealedEvent(Guid VaultId) : IEvent;
```

## System Work Inside a Tenant

System work runs with a principal-less identity that may still carry a tenant. `ExecutionIdentity.For(null, tenantId)` describes a background run scoped to one tenant: it passes `MessagePolicy.System()`, because no principal is present, and it passes `MessagePolicy.Tenant()` and the tenancy guard, because a tenant is present. A blank tenant maps to the anonymous identity, so only a real tenant scopes the run:

```csharp
var settings = new CommandMediationSettings
{
    Identity = ExecutionIdentity.For(principal: null, tenantId: "tenant-a")
};

await mediator.SendAsync(new PurgeExpiredEntriesCommand(before), settings, cancellationToken)
    .ConfigureAwait(false);
```

Pair the identity with `TenancyDeclaration.Required()` on the message and a `System()` policy, and the mediation passes both guards together: the tenancy guard sees the tenant, and the authorization guard sees no principal. Without the tenant, the same mediation is denied with `tenancy.required`.

## Evaluation Order

One authorized message runs one fixed order: every registered condition in registration order, then the policy registered under the declared name. The first deny wins, and a fault ends the evaluation as a fault rather than a refusal. Exempt and undeclared messages never reach the policies. An exemption allows in the base guard, and a message declaring nothing is denied with the `declaration.missing` code, because a use case nobody considered must not pass a check nobody wrote for it. Undeclared events are exempt by the event module default, so they publish.

The authorization guard runs before the tenancy guard. `PolicyAuthorizationGuard<TMessage>` carries the `HandlerPriorities.Authorization` priority and `TenancyGuard<TMessage>` carries `HandlerPriorities.Tenancy`, both stable across releases, so a signed-out caller sees the authorization refusal (401) before the tenancy one, whatever order the two calls were registered in. An application that checks the tenant before a permission puts `Tenant()` inside its policy, as in `All(SignedIn(), Tenant(), Permission("vaults.open"))`, instead of reordering the guards. See [Security and Tenancy](../operations/security-and-tenancy.md) for the operational view of this order.

## Denial and Fault Codes

Denials are caller-actionable refusals carried on `TrySendAsync` results and on the exception `SendAsync` raises. Faults are operator-actionable failures that raise `MessageFaultException`, end the mediation as failed, and map to a server error. `WithRefusal` rewrites denials only, so an application code never masks a fault.

| Code | Kind | Reported when |
| --- | --- | --- |
| `authorization.not-signed-in` | Denial | The check requires a signed-in caller and the mediation has none |
| `authorization.missing-permission` | Denial | The caller does not hold the permission the policy requires |
| `authorization.forbidden` | Denial | A signed-in caller fails a claim, ownership, sign-in age, or system check |
| `authorization.not-sent-by` | Denial | The enclosing mediation does not handle the message type the policy requires |
| `authorization.unknown-policy` | Fault | The message declares a policy name no composition registered |
| `authorization.permission-lookup-missing` | Fault | A permission check runs with no `IPermissionLookup` registered |
| `authorization.condition-missing` | Fault | A policy names a condition type the dispatch scope did not register |
| `authorization.decision-point-missing` | Fault | A policy names a decision point type the dispatch scope did not register |
| `authorization.sign-in-instant-unknown` | Fault | The sign-in instant is missing or unparsable, so a recent sign-in cannot be confirmed |
| `authorization.recent-sign-in-window-missing` | Fault | A resolving recent sign-in policy names a window type the dispatch scope did not register |
| `tenancy.required` | Denial | A `Tenant()` policy runs while the mediation identity has no tenant |
| `tenancy.invalid` | Denial | A `Tenant()` policy runs with a tenant the registered validator rejects |

All but the last two are public constants on `AuthorizationPolicyRefusalCodes`; `tenancy.required` and `tenancy.invalid` are `TenancyRefusalCodes.TenantRequired` and `TenancyRefusalCodes.Invalid` in `LiteBus.Messaging.Abstractions.Tenancy`. Each denial is carried on the denial refusal, so `TrySendAsync` returns them and the exception `SendAsync` raises exposes them as its code. An HTTP boundary switches on them: a caller that is not signed in is asked to authenticate (401), while a signed-in caller that lacks the permission is refused (403). A malformed tenant is answered as an invalid request, because the tenant value itself cannot be used.

## Next

Read [Authorization Declarations](authorization-declarations.md) for the positions that select these policies, [Message Definitions](message-definitions.md) for the declaration mechanism, and [Execution Context](execution-context.md) for the identity and handled instant the checks read.
