using System;
using LiteBus.Inbox.Abstractions;
using LiteBus.Transport.AwsSqs;

namespace LiteBus.Inbox.Dispatch.AwsSqs;

/// <summary>
///     Registers the AWS SQS inbox dispatcher through <see cref="InboxModuleBuilder" />.
/// </summary>
public static class InboxModuleBuilderAwsDispatchExtensions
{
    /// <summary>
    ///     Registers an AWS SQS inbox dispatcher and the matching transport module.
    /// </summary>
    /// <param name="builder">The inbox module builder.</param>
    /// <param name="configure">The dispatcher configuration action.</param>
    /// <param name="transportOptions">The AWS SQS connection settings.</param>
    /// <returns>The inbox module builder for chaining.</returns>
    public static InboxModuleBuilder UseAwsSqsDispatch(
        this InboxModuleBuilder builder,
        Action<TransportInboxDispatcherOptions> configure,
        AwsSqsTransportOptions transportOptions)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);
        ArgumentNullException.ThrowIfNull(transportOptions);

        var options = new TransportInboxDispatcherOptions();
        configure(options);

        return builder.RegisterDispatcher(
            new TransportInboxDispatchModule<AwsSqsTransportModule>(
                options,
                new AwsSqsTransportModule(transportOptions)));
    }

    /// <summary>
    ///     Registers an AWS SQS inbox dispatcher that uses an <see cref="AwsSqsTransportModule" /> registered elsewhere
    ///     in the module graph.
    /// </summary>
    /// <param name="builder">The inbox module builder.</param>
    /// <param name="configure">The dispatcher configuration action.</param>
    /// <returns>The inbox module builder for chaining.</returns>
    public static InboxModuleBuilder UseAwsSqsDispatchWithRegisteredTransport(
        this InboxModuleBuilder builder,
        Action<TransportInboxDispatcherOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new TransportInboxDispatcherOptions();
        configure(options);
        return builder.RegisterDispatcher(new TransportInboxDispatchModule<AwsSqsTransportModule>(options));
    }
}
