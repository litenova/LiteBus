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
    /// <returns>The outbox module builder for chaining.</returns>
    public static OutboxModuleBuilder UseEventOutboxDispatcher(this OutboxModuleBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.RegisterDispatcher(new EventOutboxDispatchModule());
    }
}