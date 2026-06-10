using System;
using LiteBus.Inbox.Abstractions;

namespace LiteBus.Inbox.Ingress.Aws;

/// <summary>
///     Registers AWS SQS inbox ingress through <see cref="InboxModuleBuilder" />.
/// </summary>
public static class InboxModuleBuilderAwsIngressExtensions
{
    /// <summary>
    ///     Registers AWS SQS inbox ingress as an inbox child module.
    /// </summary>
    /// <param name="builder">The inbox module builder.</param>
    /// <param name="configure">The SQS ingress configuration action.</param>
    /// <returns>The inbox module builder for chaining.</returns>
    public static InboxModuleBuilder UseAwsSqsIngress(
        this InboxModuleBuilder builder,
        Action<AwsSqsInboxIngressModuleBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);
        return builder.RegisterIngress(new AwsSqsInboxIngressModule(configure));
    }
}
