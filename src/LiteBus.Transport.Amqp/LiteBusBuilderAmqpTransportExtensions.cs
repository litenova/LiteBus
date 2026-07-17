using System;
using LiteBus.Runtime.Abstractions;

namespace LiteBus.Transport.Amqp;

/// <summary>
///     Adds the shared AMQP transport at the root LiteBus composition boundary.
/// </summary>
public static class LiteBusBuilderAmqpTransportExtensions
{
    /// <summary>
    ///     Registers one AMQP transport for dispatch and ingress modules to share.
    /// </summary>
    /// <param name="builder">The root LiteBus builder.</param>
    /// <param name="options">The AMQP connection settings.</param>
    /// <returns>The root builder for chaining.</returns>
    public static ILiteBusBuilder AddAmqpTransport(this ILiteBusBuilder builder, AmqpConnectionOptions options)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(options);
        builder.Modules.Register(new AmqpTransportModule(options));
        return builder;
    }
}
