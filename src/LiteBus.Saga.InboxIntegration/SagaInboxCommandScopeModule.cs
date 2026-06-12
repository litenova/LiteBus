using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Runtime.Abstractions;

namespace LiteBus.Saga.InboxIntegration;

/// <summary>
///     Registers inbox command pre-handlers that restore saga scope during in-process dispatch.
/// </summary>
public sealed class SagaInboxCommandScopeModule : IModule, IRequires<MessageModule>
{
    /// <inheritdoc />
    public void Build(IModuleConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(SagaInboxCommandScopePreHandler),
            typeof(SagaInboxCommandScopePreHandler),
            InstanceLifetime.Singleton));

        var messageRegistry = configuration.GetContext<IMessageRegistry>();
        messageRegistry.Register(typeof(SagaInboxCommandScopePreHandler));
    }
}
