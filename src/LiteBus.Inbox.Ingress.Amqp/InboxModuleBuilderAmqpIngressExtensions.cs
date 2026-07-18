using LiteBus.Inbox.Abstractions;

namespace LiteBus.Inbox.Ingress.Amqp;

/// <summary>
///     Registers AMQP inbox ingress through <see cref="InboxModuleBuilder" />.
/// </summary>
public static class InboxModuleBuilderAmqpIngressExtensions
{
    /// <summary>
    ///     Registers AMQP inbox ingress as an inbox child module.
    /// </summary>
    /// <param name="builder">The inbox module builder.</param>
    /// <param name="configure">The AMQP ingress configuration action.</param>
    /// <returns>The inbox module builder for chaining.</returns>
    public static InboxModuleBuilder UseAmqpIngress(
        this InboxModuleBuilder builder,
        Action<AmqpInboxIngressModuleBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);
        return builder.RegisterIngress(new AmqpInboxIngressModule(configure));
    }
}