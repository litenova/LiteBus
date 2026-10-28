# Extending

LiteBus modules, mediation strategies, and message resolution are extension points. A new module introduces a message type with its own contracts and mediation logic, composed through `ILiteBusBuilder` like the shipped modules. This page builds a Notification module, then shows custom strategies and resolution.

## Creating Custom Modules

A custom module introduces a new type of message with its own contracts and mediation logic. The example below defines a Notification module.

### Define Abstractions

Define the core interfaces for the module.

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

### Implement the Module Logic

Create the `IModule` implementation, a builder, and the mediator. Custom mediators on `IMessageMediator` use `MessageMediationRequest<TMessage, TResult>`. Pass `CancellationToken` to `Mediate`, not inside the request bag. Historical API mappings are documented in the [Migration Guide](../migration/v6.md).

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
            Tags = [],
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
        var messageRegistry = configuration.GetOrCreateContext<IMessageRegistry>(() => new MessageRegistry());
        _builderAction(new NotificationModuleBuilder(messageRegistry));

        // Register the module's services
        configuration.DependencyRegistry.Register(new DependencyDescriptor(typeof(INotificationMediator), typeof(NotificationMediator)));
    }
}
```

### Create the Extension Method

Create an extension method so application code registers the module through `ILiteBusBuilder`.

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

The application still calls `AddMessaging(...)`. The graph validates `IRequires<MessageModule>` after the callback, independent of declaration order. A semantic abstraction should describe notifications and handlers only. Do not expose registration-only marker interfaces through domain contracts.

## Custom Mediation Strategies

You can change how messages are processed by implementing `IMessageMediationStrategy<TMessage, TMessageResult>`. For example, you could create a strategy that retries failed handlers.

```csharp
public sealed class RetryMediationStrategy<TMessage> : IMessageMediationStrategy<TMessage, Task> where TMessage : notnull
{
    private readonly int _retryCount;

    public RetryMediationStrategy(int retryCount = 3)
    {
        _retryCount = retryCount;
    }

    public async Task Mediate(TMessage message, IMessageDependencies deps, IExecutionContext context)
    {
        var decision = await deps.RunAsyncPreStages(message, context.CancellationToken).ConfigureAwait(false);

        if (decision.StopsPipeline)
        {
            return;
        }

        var handler = deps.MainHandlers.Single().Handler.Value;

        for (var i = 0; i < _retryCount; i++)
        {
            try
            {
                await ((Task)handler.Handle(message)).ConfigureAwait(false);
                break; // Success
            }
            catch (Exception) when (i < _retryCount - 1)
            {
                await Task.Delay(100 * (i + 1), context.CancellationToken).ConfigureAwait(false); // Exponential backoff
            }
        }

        await deps.RunAsyncPostHandlers(message, null, context.CancellationToken).ConfigureAwait(false);
    }
}
```

## Custom Message Resolution

Change how LiteBus finds handlers for a message by implementing `IMessageResolveStrategy`. This is an advanced scenario for custom rules that match messages to handlers, such as versioning attributes. Resolution internals are documented in [Handler Resolution Internals](../architecture/handler-resolution.md).

## Best Practices for Extensibility

1.  Build custom mediators on `IMessageMediator`: delegate the core mediation logic to it and provide only the custom strategy and options.
2.  Model custom modules and extensions on the shipped command, query, and event modules, so registration and discovery stay consistent.
3.  Register every service the module needs with the `IDependencyRegistry` from the `IModule` implementation.

## Next

- [Custom Stores and Dispatchers](custom-stores-and-dispatchers.md): storage roles and dispatcher contracts for the durable axes
- [Handler Resolution Internals](../architecture/handler-resolution.md): how registration builds the handler lists strategies run
