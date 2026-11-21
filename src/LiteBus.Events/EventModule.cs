using System;
using System.Collections.Generic;
using LiteBus.Events.Abstractions;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Registry;
using LiteBus.Runtime.Abstractions;

namespace LiteBus.Events;

/// <summary>
///     Module for configuring event handling infrastructure.
///     Depends on the messaging module for core messaging functionality.
/// </summary>
public sealed class EventModule : IModule, IRequires<MessageModule>
{
    /// <summary>
    ///     The configuration callbacks invoked when the module is built, in registration order.
    /// </summary>
    private readonly List<Action<EventModuleBuilder>> _builders = [];

    /// <summary>
    ///     Initializes a new instance of the <see cref="EventModule" /> class.
    /// </summary>
    /// <param name="builder">The configuration action for the event module.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="builder" /> is <see langword="null" />.</exception>
    public EventModule(Action<EventModuleBuilder> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        _builders.Add(builder);
    }

    /// <summary>
    ///     Runs an additional configuration callback on this module's builder when the module builds.
    /// </summary>
    /// <param name="builderAction">The additional event configuration callback.</param>
    /// <param name="registerChild">Registers child modules with the module registry; the event module declares none.</param>
    internal void Configure(Action<EventModuleBuilder> builderAction, Action<IModule> registerChild)
    {
        ArgumentNullException.ThrowIfNull(builderAction);
        ArgumentNullException.ThrowIfNull(registerChild);
        _builders.Add(builderAction);
    }

    /// <summary>
    ///     Builds the event module by configuring event handlers and registering event-specific services.
    /// </summary>
    /// <param name="configuration">The module configuration containing dependency registry and shared context.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configuration" /> is <see langword="null" />.</exception>
    public void Build(IModuleConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var messageRegistry = configuration.GetContext<IMessageRegistry>();
        var contractRegistry = configuration.GetOrCreateContext(() => new MessageContractRegistry());

        var startIndex = messageRegistry.Handlers.Count;

        var moduleBuilder = new EventModuleBuilder(messageRegistry, contractRegistry);

        foreach (var configure in _builders)
        {
            configure(moduleBuilder);
        }

        DeclareEventAuthorizationDefault(messageRegistry);

        RegisterEventServices(configuration);
        // Recorded for the composition summary. Only this module knows how many messages belong to its axis, because
        // the messaging registry does not reference the axis contracts.
        if (configuration.TryGetContext<LiteBusCompositionSummary>(out var summary) && summary is not null)
        {
            summary.RecordAxis("events", ModuleRegistration.CountAxisMessages<IEvent>(messageRegistry));
        }

        ModuleRegistration.RegisterNewHandlers(configuration, messageRegistry, startIndex);
    }

    /// <summary>
    ///     Declares that events are exempt from authorization unless an event states its own position.
    /// </summary>
    /// <param name="messageRegistry">The message registry shared across LiteBus modules.</param>
    /// <remarks>
    ///     <para>
    ///         Publishing an event notifies subscribers that something already happened, so it asks nothing of the
    ///         caller: an undeclared event must publish even when the composition registers authorization policies.
    ///         Without this default, the policy guard would deny every undeclared event with
    ///         <c>declaration.missing</c>, including events replayed from durable storage.
    ///     </para>
    ///     <para>
    ///         An explicit per-event declaration still applies, because a declaration written closer to the message
    ///         overrides this family default. A composition that already states its own <c>IEvent</c>
    ///         authorization default keeps it: the framework default yields through <c>TryAddDeclaration</c>,
    ///         which returns <see langword="false" /> when a declaration is already present instead of throwing.
    ///     </para>
    /// </remarks>
    private static void DeclareEventAuthorizationDefault(IMessageRegistry messageRegistry)
    {
        ArgumentNullException.ThrowIfNull(messageRegistry);

        messageRegistry.TryAddDeclaration(MessageDeclarationItem.For<IEvent, AuthorizationDeclaration>(
            AuthorizationDeclaration.Exempt(
                "events record facts that already happened, so there is no request to authorize")));
    }

    /// <summary>
    ///     Registers event-specific services with the dependency registry.
    /// </summary>
    /// <param name="configuration">The module configuration.</param>
    private static void RegisterEventServices(IModuleConfiguration configuration)
    {
        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IEventMediator),
            typeof(EventMediator)));
    }
}
