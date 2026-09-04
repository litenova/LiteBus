# Handler Filtering

Filtering lets the same message run a different set of handlers depending on the call. Use it to process a command differently for a public API versus an internal service, or to publish an event to only a subset of subscribers. There are two mechanisms: tags applied at compile time and a predicate evaluated at publish time. This page builds on the handler stages in [The Handler Pipeline](handler-pipeline.md).

Tags and predicates work on **commands**, **queries**, and **events**. Each axis exposes routing settings on its mediation settings type: `CommandRoutingSettings`, `QueryRoutingSettings`, and `EventRoutingSettings`.

## 1. Tag-Based Filtering

Tags are static labels applied to handlers at compile time. You can then specify which tags are active during mediation.

### Applying Tags to Handlers

Use the `[HandlerTag]` or `[HandlerTags]` attributes.

```csharp
using LiteBus.Messaging.Abstractions;

// This handler will only run if the "PublicAPI" tag is active.
[HandlerTag("PublicAPI")]
public class StrictValidationPreHandler : ICommandPreHandler<CreateUserCommand>
{
    // ... logic for strict validation
}

// This handler is untagged and will always be considered for execution.
public class CommonValidationPreHandler : ICommandPreHandler<CreateUserCommand>
{
    // ... logic for common validation
}

// This handler runs if either "Admin" or "Internal" tags are active.
[HandlerTags("Admin", "Internal")]
public class AdminDataEnrichmentHandler : IQueryPostHandler<GetUserQuery>
{
    // ... logic to add sensitive data
}
```

### Mediating with Tags

You can specify tags when sending a message using either the extension methods or the mediation settings object.

```csharp
// Using the extension method (for a single tag)
await _commandMediator.SendAsync(command, "PublicAPI");
await _queryMediator.QueryAsync(query, "Admin");
await _eventMediator.PublishAsync(orderPlaced, "Notifications");

// Using the settings object (for one or more tags)
var settings = new CommandMediationSettings
{
    Routing = new CommandRoutingSettings
    {
        Tags = ["Admin", "HighPriority"]
    }
};
await _commandMediator.SendAsync(command, settings);
```

Query and event calls follow the same shape with `QueryMediationSettings` / `QueryRoutingSettings` and `EventMediationSettings` / `EventRoutingSettings`.

### Tag Selection Logic

When you mediate with a set of tags:

1. **Untagged handlers are always executed.** They are considered universal.
2. **Tagged handlers are executed only if at least one of their tags matches** one of the tags provided during mediation.
3. Handlers whose tags do not match any of the provided tags are skipped.

## 2. Predicate-Based Filtering

For more dynamic or complex filtering logic, you can provide a predicate function. The predicate is evaluated for each potential handler and receives an `IHandlerDescriptor` object, which contains rich metadata about the handler.

`HandlerPredicate` is available on `CommandRoutingSettings`, `QueryRoutingSettings`, and `EventRoutingSettings`.

### Using a Predicate

```csharp
var settings = new EventMediationSettings
{
    Routing = new EventRoutingSettings
    {
        // The predicate receives a descriptor for each potential handler.
        // It should return true if the handler should be executed.
        HandlerPredicate = descriptor =>
        {
            // Example: Only execute handlers from the "Core" namespace.
            return descriptor.HandlerType.Namespace?.StartsWith("MyProject.Core") == true;
        }
    }
};

await _eventMediator.PublishAsync(myEvent, settings);
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
await _commandMediator.SendAsync(command, commandSettings);
```

### `IHandlerDescriptor` Properties

The descriptor provides access to:

- `HandlerType`: The `System.Type` of the handler class.
- `MessageType`: The `System.Type` of the message it handles.
- `Priority`: The handler's priority value.
- `Tags`: A collection of tags applied to the handler.

### Use Case: Filtering by Marker Interface

A common pattern is to filter handlers based on a marker interface, which is cleaner than checking type names.

```csharp
// 1. Define a marker interface
public interface IHighPriorityEventHandler { }

// 2. Apply it to a handler
public class CriticalNotificationHandler : IEventHandler<SystemAlertEvent>, IHighPriorityEventHandler
{
    // ...
}

// 3. Filter using the predicate
var settings = new EventMediationSettings
{
    Routing = new EventRoutingSettings
    {
        HandlerPredicate = descriptor => descriptor.HandlerType.IsAssignableTo(typeof(IHighPriorityEventHandler))
    }
};
await _eventMediator.PublishAsync(alert, settings);
```

## Combining Tags and Predicates

Tags and predicates can be used together. The filtering logic in LiteBus applies tags first to create an initial set of candidate handlers, and then the predicate is applied to further refine that set.

## Best Practices

1. **Use tags for static contexts**: Tags are ideal for well-defined, static contexts like "PublicAPI", "InternalService", or "Reporting".
2. **Use predicates for dynamic logic**: Predicates are better for runtime decisions, feature flags, or complex rules that can't be expressed with simple labels.
3. **Define constants for tags**: Avoid magic strings by defining your tags as constants in a shared class.
4. **Document your filters**: Maintain clear documentation for what your tags and predicates represent so your team uses them consistently.

## Next

Read [Polymorphic Dispatch](polymorphic-dispatch.md) to apply one handler across a family of related messages.
