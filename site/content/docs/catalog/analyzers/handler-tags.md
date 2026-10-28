# Handler Tags

## Header

- **ID**: `analyzers.orphan-handler-tag`
- **Diagnostic**: `LB1011` (Warning)
- **Maturity**: GA
- **Summary**: Reports handler tags declared through `HandlerTagAttribute` or `HandlerTagsAttribute` when the tag is never referenced by command, query, or event mediation routing.

## Trigger Conditions

`LB1011` reports when:

- A handler type has one or more declared tag strings.
- Tag reference scanning does not find those strings in mediation routing configuration in the same compilation.

Recognized tag reference sources include:

- `CommandRoutingSettings.Tags`
- `QueryRoutingSettings.Tags`
- `EventRoutingSettings.Tags`
- Mediator extension calls with `tag` parameter (`SendAsync`, `QueryAsync`, `PublishAsync`, `StreamAsync`) on LiteBus mediator extension types.

Tags set through target-typed `new()` initializers, including the `new(args)` form, are recognized the same way as explicit `new T { ... }` initializers. Tag references may use string literals or `const` values; constant expressions are resolved to their values before comparison.

The rule analyzes one compilation: only handlers declared in the analyzed assembly are candidates, and only tag filters invoked in the analyzed compilation satisfy it. A filter that lives in a dependent project stays invisible; co-locate it with the handler or suppress the diagnostic. A partial handler with two declarations reports once, at its first location. See [Analyzers](../../reference/analyzers.md).

## Bad Example

```csharp
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Commands.Abstractions;
using LiteBus.Messaging.Abstractions;

[HandlerTag("orphan")]
public sealed class OrphanTaggedHandler : ICommandHandler<SampleCommand>
{
    public Task HandleAsync(SampleCommand command, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

public sealed record SampleCommand : ICommand;
```

Expected diagnostic:

- `LB1011` for tag `orphan`.

## Good Example

```csharp
using LiteBus.Commands.Abstractions;

public sealed class Sender
{
    public void Send(ICommandMediator mediator)
    {
        var settings = new CommandMediationSettings
        {
            Routing = new CommandRoutingSettings
            {
                Tags = ["frontend"]
            }
        };
    }
}
```

When a handler declares `HandlerTag("frontend")`, this configuration satisfies the rule.

## Suppression Guidance

- Keep tags and routing values synchronized through constants when possible.
- Suppress only for staged rollouts where one side ships before the other.
- Remove unused tags instead of suppressing if the routing is not needed.

## Test Coverage

Source: `tests/LiteBus.Analyzers.UnitTests/OrphanHandlerTagAnalyzerTests.cs`, `tests/LiteBus.Analyzers.UnitTests/OrphanHandlerTagAnalyzerRegressionTests.cs`

| Test method | Verifies |
| --- | --- |
| `OrphanHandlerTag_ShouldReportWhenTagIsNotReferenced` | Unreferenced handler tag reports `LB1011` |
| `OrphanHandlerTag_ShouldNotReportWhenTagIsReferenced` | Command routing tags satisfy the rule |
| `OrphanHandlerTag_ShouldNotReportWhenQueryFilterTagsAreReferenced` | Query routing tags satisfy the rule |
| `OrphanHandlerTag_ShouldNotReportWhenExtensionMethodTagIsReferenced` | Mediator extension method tag parameter is recognized |
| `OrphanHandlerTag_ShouldNotReportWhenEventRoutingTagsAreReferenced` | Event routing tags satisfy the rule |
| `OrphanHandlerTag_ShouldReportOnceForPartialHandlerAcrossTwoDeclarations` | Partial handler reports its orphan tag once |
