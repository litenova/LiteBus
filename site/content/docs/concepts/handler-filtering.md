# Handler Filtering

Handler filtering selects which handlers run for a message on each call. You tag handlers at registration and select tags per call, or you pass a predicate that inspects each handler descriptor. Tags and predicates work on commands, queries, and events. Each axis exposes routing settings on its mediation settings type (`CommandRoutingSettings`, `QueryRoutingSettings`, `EventRoutingSettings`).

This page builds on the handler stages in [The Handler Pipeline](handler-pipeline.md).

## Tag-Based Filtering

Apply the `[HandlerTag]` or `[HandlerTags]` attribute to handlers that belong to a named context, then pass the active tags when you mediate:

```csharp
[HandlerTag("PublicAPI")]
public sealed class StrictValidationPreHandler : ICommandPreHandler<CreateUserCommand>
{
    public Task PreHandleAsync(CreateUserCommand command, CancellationToken cancellationToken = default)
    {
        // ...
        return Task.CompletedTask;
    }
}

// Untagged, so it runs in every context.
public sealed class CommonValidationPreHandler : ICommandPreHandler<CreateUserCommand>
{
    public Task PreHandleAsync(CreateUserCommand command, CancellationToken cancellationToken = default)
    {
        // ...
        return Task.CompletedTask;
    }
}
```

```csharp
// One tag, through the sugar overload.
await commandMediator.SendAsync(command, "PublicAPI", cancellationToken);

// One or more tags, through the settings object.
var settings = new CommandMediationSettings
{
    Routing = new CommandRoutingSettings
    {
        Tags = ["Admin", "HighPriority"]
    }
};

await commandMediator.SendAsync(command, settings, cancellationToken);
```

Queries and events use the same pattern, with `QueryMediationSettings` / `QueryRoutingSettings` and `EventMediationSettings` / `EventRoutingSettings`.

### Tag Selection Logic

When you mediate with a set of tags:

1. Untagged handlers always run, because they belong to every context.
2. Tagged handlers run when at least one of their tags matches a tag passed for the call.
3. Handlers whose tags match none of the passed tags are skipped.

## Predicate-Based Filtering

For filtering beyond static labels, pass a predicate. It receives the handler descriptor for each candidate handler and returns whether that handler runs:

```csharp
var settings = new EventMediationSettings
{
    Routing = new EventRoutingSettings
    {
        HandlerPredicate = descriptor =>
            descriptor.HandlerType.Namespace?.StartsWith("MyProject.Core") == true
    }
};

await eventMediator.PublishAsync(myEvent, settings, cancellationToken);
```

The same pattern applies to commands and queries:

```csharp
var commandSettings = new CommandMediationSettings
{
    Routing = new CommandRoutingSettings
    {
        HandlerPredicate = d => d.HandlerType.Namespace?.StartsWith("MyApp.Admin") == true
    }
};

await commandMediator.SendAsync(command, commandSettings, cancellationToken);
```

The predicate type differs by axis. Command and query routing use `Func<IHandlerDescriptor, bool>`. Event routing uses the named `EventHandlerFilter` delegate, which takes an `IHandlerDescriptor` and returns whether the handler runs. The lambda you write looks the same on every axis. Name the delegate type when you store a predicate in a field or combine several with logical operators.

### `IHandlerDescriptor` Properties

The descriptor carries the message type, the handler type, the tags, the priority, and the registration metadata:

- `HandlerType`: the `System.Type` of the handler class.
- `MessageType`: the `System.Type` of the message it handles.
- `Priority`: the handler priority value.
- `Tags`: the tags applied to the handler.
- `RegistrationSequence`: the sequence assigned when the descriptor was committed to the registry. Lower values mean earlier registration and break priority ties.
- `ContractType`: the closed handler contract that the dispatch was discovered from, such as `IMessagePostHandler<PlaceOrderCommand, OrderId>`. A handler class may implement contracts for several message types. This property names the contract the pipeline invokes, which is the one that appears in diagnostics.

### Filtering by Marker Interface

To filter handlers on a marker interface rather than on type names, define the interface, apply it to handlers, and test for it in the predicate:

```csharp
// 1. Define a marker interface
public interface IHighPriorityEventHandler { }

// 2. Apply it to a handler
public sealed class CriticalNotificationHandler : IEventHandler<SystemAlertEvent>, IHighPriorityEventHandler
{
    public Task HandleAsync(SystemAlertEvent @event, CancellationToken cancellationToken = default)
    {
        // ...
        return Task.CompletedTask;
    }
}

// 3. Filter using the predicate
var settings = new EventMediationSettings
{
    Routing = new EventRoutingSettings
    {
        HandlerPredicate = descriptor => descriptor.HandlerType.IsAssignableTo(typeof(IHighPriorityEventHandler))
    }
};

await eventMediator.PublishAsync(alert, settings, cancellationToken);
```

## Combining Tags and Predicates

Tags and predicates combine. Filtering applies tags first to build the candidate set, then applies the predicate to refine it.

## Best Practices

1. **Use tags for static contexts**: tags fit well-defined contexts such as "PublicAPI", "InternalService", or "Reporting".
2. **Use predicates for dynamic logic**: predicates fit runtime decisions, feature flags, and rules that static labels cannot express.
3. **Define constants for tags**: hold tags as constants in a shared class instead of repeating string literals.
4. **Document your filters**: record what each tag and predicate means so a team applies them consistently.

## Next

Read [Polymorphic Dispatch](polymorphic-dispatch.md) to apply one handler across a family of related messages.
