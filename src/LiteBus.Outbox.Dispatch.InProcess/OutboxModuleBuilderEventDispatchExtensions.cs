using System;
using LiteBus.Outbox.Abstractions;

namespace LiteBus.Outbox.Dispatch.InProcess;

/// <summary>
///     Registers the event outbox dispatcher through <see cref="OutboxModuleBuilder" />.
/// </summary>
public static class OutboxModuleBuilderEventDispatchExtensions
{
    /// <summary>
    ///     Registers the in-process event outbox dispatcher as an outbox child module.
    /// </summary>
    /// <param name="builder">The outbox module builder.</param>
    /// <param name="configure">
    ///     An optional action that adjusts the dispatch options. When omitted, a message dispatched with no
    ///     registered handler fails the attempt rather than completing as published.
    /// </param>
    /// <returns>The outbox module builder for chaining.</returns>
    /// <example>
    ///     <code><![CDATA[
    /// outbox.UseInProcessDispatch(dispatch => dispatch.RequireHandler = false);
    /// ]]></code>
    /// </example>
    public static OutboxModuleBuilder UseInProcessDispatch(
        this OutboxModuleBuilder builder,
        Action<InProcessOutboxDispatchOptionsBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var optionsBuilder = new InProcessOutboxDispatchOptionsBuilder();
        configure?.Invoke(optionsBuilder);

        return builder.RegisterDispatcher(new EventOutboxDispatchModule(optionsBuilder.Build()));
    }
}
