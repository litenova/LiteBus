using System;
using LiteBus.Runtime.Abstractions;

namespace LiteBus.Transport.AwsSqs;

/// <summary>
///     Adds the shared AWS SQS transport at the root LiteBus composition boundary.
/// </summary>
public static class LiteBusBuilderAwsSqsTransportExtensions
{
    /// <summary>
    ///     Registers one AWS SQS transport for dispatch and ingress modules to share.
    /// </summary>
    /// <param name="builder">The root LiteBus builder.</param>
    /// <param name="options">The AWS SQS connection settings.</param>
    /// <returns>The root builder for chaining.</returns>
    public static ILiteBusBuilder AddAwsSqsTransport(this ILiteBusBuilder builder, AwsSqsTransportOptions options)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(options);
        builder.Modules.Register(new AwsSqsTransportModule(options));
        return builder;
    }
}
