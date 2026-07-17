using System;
using LiteBus.Runtime.Abstractions;

namespace LiteBus.Transport.InMemory;

/// <summary>
///     Adds the shared in-memory transport at the root LiteBus composition boundary.
/// </summary>
public static class LiteBusBuilderInMemoryTransportExtensions
{
    /// <summary>
    ///     Registers one in-memory transport for dispatch and ingress modules to share.
    /// </summary>
    /// <param name="builder">The root LiteBus builder.</param>
    /// <returns>The root builder for chaining.</returns>
    public static ILiteBusBuilder AddInMemoryTransport(this ILiteBusBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Modules.Register(new InMemoryTransportModule());
        return builder;
    }
}
