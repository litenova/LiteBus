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
    ///     Registers an AWS SQS outbox dispatcher and the matching transport module.
    /// </summary>
    /// <param name="builder">The outbox module builder.</param>
    /// <param name="configure">The dispatcher configuration action.</param>
    /// <param name="transportOptions">The AWS SQS connection settings.</param>
    /// <returns>The outbox module builder for chaining.</returns>
    public static OutboxModuleBuilder UseAwsSqsDispatch(
        this OutboxModuleBuilder builder,
        Action<TransportOutboxDispatcherOptions> configure,
        AwsSqsTransportOptions transportOptions)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);
        ArgumentNullException.ThrowIfNull(transportOptions);

        var options = new TransportOutboxDispatcherOptions();
        configure(options);

        return builder.RegisterDispatcher(
            new TransportOutboxDispatchModule(options, new AwsSqsTransportModule(transportOptions)));
    }
}