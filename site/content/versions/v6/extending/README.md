# Extensibility


## Creating Custom Modules

A custom module allows you to introduce a new type of message with its own contracts and mediation logic. Let's create a simple "Notification" module as an example.

### 1. Define Abstractions

First, define the core interfaces for your module.

```csharp
// The base notification message contract
public interface INotification { }

// The handler contract
public interface INotificationHandler<in TNotification> : IAsyncMessageHandler<TNotification>
    where TNotification : INotification
{
}

// The mediator contract
public interface INotificationMediator
{
    Task SendAsync(INotification notification, CancellationToken cancellationToken = default);
}
```

### 2. Implement the Module Logic

Create the `IModule` implementation, a builder, and the mediator. Custom mediators on `IMessageMediator` use `MessageMediationRequest<TMessage, TResult>`; pass `CancellationToken` to `Mediate`, not inside the request bag. Historical API mappings are documented in the [Migration Guide](../migration/v6.md).

```csharp
// The mediator that will orchestrate the process
internal sealed class NotificationMediator : INotificationMediator
{
    private readonly IMessageMediator _messageMediator;

    public NotificationMediator(IMessageMediator messageMediator)
    {
        _messageMediator = messageMediator;
    }

    public Task SendAsync(INotification notification, CancellationToken cancellationToken = default)
    {
        // For notifications, we might want to broadcast to all handlers, like events.
        var mediationStrategy = new AsyncBroadcastMediationStrategy<INotification>(new EventMediationSettings());
        var resolveStrategy = new ActualTypeOrFirstAssignableTypeMessageResolveStrategy();
        var request = new MessageMediationRequest<INotification, Task>
        {
            MessageMediationStrategy = mediationStrategy,
            MessageResolveStrategy = resolveStrategy,
        };
        return _messageMediator.Mediate(notification, request, cancellationToken);
    }
}

// The module builder for registering types
public sealed class NotificationModuleBuilder
{
    private readonly IMessageRegistry _messageRegistry;
    public NotificationModuleBuilder(IMessageRegistry messageRegistry) => _messageRegistry = messageRegistry;

    public NotificationModuleBuilder Register<T>()
    {
        var type = typeof(T);
        var isNotification = typeof(INotification).IsAssignableFrom(type);
        var isHandler = type.GetInterfaces().Any(candidate =>
            candidate.IsGenericType &&
            candidate.GetGenericTypeDefinition() == typeof(INotificationHandler<>));

        if (!isNotification && !isHandler)
        {
            throw new LiteBusNotSupportedException($"Type '{type}' is not a notification or notification handler.");
        }

        _messageRegistry.Register(type);
        return this;
    }
}

// The module itself, which handles DI registration
internal sealed class NotificationModule : IModule, IRequires<MessageModule>
{
    private readonly Action<NotificationModuleBuilder> _builderAction;

    public NotificationModule(Action<NotificationModuleBuilder> builderAction) => _builderAction = builderAction;

    public void Build(IModuleConfiguration configuration)
    {
        // Run the user's configuration
        var messageRegistry = configuration.GetOrCreateContext(() => new MessageRegistry());
        _builderAction(new NotificationModuleBuilder(messageRegistry));

        // Register the module's services
        configuration.DependencyRegistry.Register(new DependencyDescriptor(typeof(INotificationMediator), typeof(NotificationMediator)));
    }
}
```

### 3. Create the Extension Method

Finally, create an extension method to make registration easy.

```csharp
public static class ModuleRegistryExtensions
{
    public static ILiteBusBuilder AddNotifications(
        this ILiteBusBuilder builder,
        Action<NotificationModuleBuilder> builderAction)
    {
        builder.Modules.Register(new NotificationModule(builderAction));
        return builder;
    }
}
```

The application still calls `AddMessaging(...)`. The graph validates `IRequires<MessageModule>` after the callback, independent of declaration order. A semantic abstraction should describe notifications and handlers only; do not expose registration-only marker interfaces through domain contracts.

## Custom Mediation Strategies

You can change how messages are processed by implementing `IMessageMediationStrategy<TMessage, TMessageResult>`. For example, you could create a strategy that retries failed handlers.

```csharp
public class RetryMediationStrategy<TMessage> : IMessageMediationStrategy<TMessage, Task> where TMessage : notnull
{
    private readonly int _retryCount;

    public RetryMediationStrategy(int retryCount = 3) => _retryCount = retryCount;

    public async Task Mediate(TMessage message, IMessageDependencies deps, IExecutionContext context)
    {
        await deps.RunAsyncPreHandlers(message);

        var handler = deps.MainHandlers.Single().Handler.Value;

        for (int i = 0; i < _retryCount; i++)
        {
            try
            {
                await (Task)handler.Handle(message);
                break; // Success
            }
            catch (Exception) when (i < _retryCount - 1)
            {
                await Task.Delay(100 * (i + 1)); // Exponential backoff
            }
        }

        await deps.RunAsyncPostHandlers(message, null);
    }
}
```

## Custom Message Resolution

You can change how LiteBus finds handlers for a message by implementing `IMessageResolveStrategy`. This is an advanced scenario, useful if you have custom rules for matching messages to handlers (e.g., based on versioning attributes).

## Best Practices for Extensibility

1.  **Build on `IMessageMediator`**: Your custom mediators should delegate the core mediation logic to the built-in `IMessageMediator` and only provide the custom strategy and options.
2.  **Follow Existing Patterns**: Model your custom modules and extensions on the existing LiteBus architecture for consistency.
3.  **Register module services**: Your `IModule` implementation registers all services your module needs with the `IDependencyRegistry`.

## Next
