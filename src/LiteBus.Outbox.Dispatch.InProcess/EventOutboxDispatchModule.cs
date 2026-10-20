using System;
using System.Linq;
using LiteBus.Events;
using LiteBus.Outbox;
using LiteBus.Outbox.Abstractions;
using LiteBus.Runtime.Abstractions;
using LiteBus.Runtime.Abstractions.Exceptions;

namespace LiteBus.Outbox.Dispatch.InProcess;

/// <summary>
///     Module that registers <see cref="EventOutboxDispatcher" /> as <see cref="IOutboxDispatcher" />.
/// </summary>
/// <remarks>
///     Register this module through <see cref="OutboxModuleBuilderEventDispatchExtensions.UseInProcessDispatch" />
///     inside
///     <c>AddOutboxModule</c> after <c>AddEventModule</c>. The outbox module supplies contract registration and the
///     event module supplies <c>IEventMediator</c> from <c>LiteBus.Events.Abstractions</c>.
/// </remarks>
public sealed class EventOutboxDispatchModule :
    IOutboxDispatcherModule,
    IRequires<OutboxModule>,
    IRequires<EventModule>
{
    /// <summary>
    ///     The dispatch options applied to every published message.
    /// </summary>
    private readonly InProcessOutboxDispatchOptions _options;

    /// <summary>
    ///     Initializes a new instance of the <see cref="EventOutboxDispatchModule" /> class with default options.
    /// </summary>
    public EventOutboxDispatchModule()
        : this(new InProcessOutboxDispatchOptions())
    {
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="EventOutboxDispatchModule" /> class.
    /// </summary>
    /// <param name="options">The dispatch options applied to every published message.</param>
    public EventOutboxDispatchModule(InProcessOutboxDispatchOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    /// <inheritdoc />
    public void Build(IModuleConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(InProcessOutboxDispatchOptions),
            _options));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IOutboxDispatcher),
            typeof(EventOutboxDispatcher)));
    }
}
