using System;
using LiteBus.Runtime.Abstractions;

namespace LiteBus.Transport.Kafka;

/// <summary>
///     Adds the shared Kafka transport at the root LiteBus composition boundary.
/// </summary>
public static class LiteBusBuilderKafkaTransportExtensions
{
    /// <summary>
    ///     Registers one Kafka transport for dispatch and ingress modules to share.
    /// </summary>
    /// <param name="builder">The root LiteBus builder.</param>
    /// <param name="options">The Kafka connection settings.</param>
    /// <returns>The root builder for chaining.</returns>
    public static ILiteBusBuilder AddKafkaTransport(this ILiteBusBuilder builder, KafkaTransportOptions options)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(options);
        builder.Modules.Register(new KafkaTransportModule(options));
        return builder;
    }
}
