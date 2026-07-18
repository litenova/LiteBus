using System;
using LiteBus.Inbox.Abstractions;

namespace LiteBus.Inbox.Ingress.Kafka;

/// <summary>
///     Registers Kafka inbox ingress through <see cref="InboxModuleBuilder" />.
/// </summary>
public static class InboxModuleBuilderKafkaIngressExtensions
{
    /// <summary>
    ///     Registers Kafka inbox ingress as an inbox child module.
    /// </summary>
    /// <param name="builder">The inbox module builder.</param>
    /// <param name="configure">The Kafka ingress configuration action.</param>
    /// <returns>The inbox module builder for chaining.</returns>
    public static InboxModuleBuilder UseKafkaIngress(
        this InboxModuleBuilder builder,
        Action<KafkaInboxIngressModuleBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);
        return builder.RegisterIngress(new KafkaInboxIngressModule(configure));
    }
}