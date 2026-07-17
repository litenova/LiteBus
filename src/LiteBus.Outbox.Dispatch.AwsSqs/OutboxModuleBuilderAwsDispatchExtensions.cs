using System;
using LiteBus.Outbox.Abstractions;
using LiteBus.Transport.AwsSqs;

namespace LiteBus.Outbox.Dispatch.AwsSqs;

/// <summary>
///     Registers the AWS SQS outbox dispatcher through <see cref="OutboxModuleBuilder" />.
/// </summary>
public static class OutboxModuleBuilderAwsDispatchExtensions
{
    /// <summary>
    ///     Registers an AWS SQS outbox dispatcher that uses an <see cref="AwsSqsTransportModule" /> registered elsewhere
    ///     in the module graph.
    /// </summary>
    /// <param name="builder">The outbox module builder.</param>
    /// <param name="configure">The dispatcher configuration action.</param>
    /// <returns>The outbox module builder for chaining.</returns>
    public static OutboxModuleBuilder UseAwsSqsDispatch(
        this OutboxModuleBuilder builder,
        Action<TransportOutboxDispatcherOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new TransportOutboxDispatcherOptions();
        configure(options);
        return builder.RegisterDispatcher(new TransportOutboxDispatchModule<AwsSqsTransportModule>(options));
    }
}
