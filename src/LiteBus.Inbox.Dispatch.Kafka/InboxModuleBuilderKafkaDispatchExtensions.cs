using System;
using LiteBus.Inbox.Abstractions;
using LiteBus.Transport.Kafka;

namespace LiteBus.Inbox.Dispatch.Kafka;

/// <summary>
///     Registers the Kafka inbox dispatcher through <see cref="InboxModuleBuilder" />.
/// </summary>
public static class InboxModuleBuilderKafkaDispatchExtensions
{
    /// <summary>
    ///     Registers a Kafka inbox dispatcher that uses a <see cref="KafkaTransportModule" /> registered at the root of the
    ///     module graph.
    /// </summary>
    /// <param name="builder">The inbox module builder.</param>
    /// <param name="configure">The dispatcher configuration action.</param>
    /// <returns>The inbox module builder for chaining.</returns>
    public static InboxModuleBuilder UseKafkaDispatch(
        this InboxModuleBuilder builder,
        Action<TransportInboxDispatcherOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new TransportInboxDispatcherOptions();
        configure(options);
        return builder.RegisterDispatcher(new TransportInboxDispatchModule<KafkaTransportModule>(options));
    }
}
