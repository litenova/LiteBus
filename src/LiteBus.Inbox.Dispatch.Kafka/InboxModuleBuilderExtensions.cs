using System;
using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Dispatch;
using LiteBus.Transport.Kafka;

namespace LiteBus.Inbox.Dispatch.Kafka;

/// <summary>
///     Registers the Kafka inbox dispatcher through <see cref="InboxModuleBuilder" />.
/// </summary>
public static class InboxModuleBuilderKafkaDispatchExtensions
{
    /// <summary>
    ///     Registers a Kafka inbox dispatcher and the matching transport module.
    /// </summary>
    /// <param name="builder">The inbox module builder.</param>
    /// <param name="configure">The dispatcher configuration action.</param>
    /// <param name="transportOptions">The Kafka connection settings.</param>
    /// <returns>The inbox module builder for chaining.</returns>
    public static InboxModuleBuilder UseKafkaDispatch(
        this InboxModuleBuilder builder,
        Action<TransportInboxDispatcherOptions> configure,
        KafkaTransportOptions transportOptions)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);
        ArgumentNullException.ThrowIfNull(transportOptions);

        var options = new TransportInboxDispatcherOptions();
        configure(options);
        return builder.RegisterDispatcher(
            new TransportInboxDispatchModule(options, new KafkaTransportModule(transportOptions)));
    }
}
