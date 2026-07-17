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
        return builder.AddInMemoryTransport(new InMemoryTransportOptions());
    }

    /// <summary>
    ///     Registers one configured in-memory transport for dispatch and ingress modules to share.
    /// </summary>
    /// <param name="builder">The root LiteBus builder.</param>
    /// <param name="options">The process-local transport settings.</param>
    /// <returns>The root builder for chaining.</returns>
    public static ILiteBusBuilder AddInMemoryTransport(
        this ILiteBusBuilder builder,
        InMemoryTransportOptions options)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(options);
        builder.Modules.Register(new InMemoryTransportModule(options));
        return builder;
    }
}
