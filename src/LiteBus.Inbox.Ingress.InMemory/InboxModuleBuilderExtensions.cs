using System;
using LiteBus.Inbox.Abstractions;

namespace LiteBus.Inbox.Ingress.InMemory;

/// <summary>
///     Registers in-memory inbox ingress through <see cref="InboxModuleBuilder" />.
/// </summary>
public static class InboxModuleBuilderInMemoryIngressExtensions
{
    /// <summary>
    ///     Registers in-memory inbox ingress as an inbox child module.
    /// </summary>
    /// <param name="builder">The inbox module builder.</param>
    /// <param name="configure">The in-memory ingress configuration action.</param>
    /// <returns>The inbox module builder for chaining.</returns>
    public static InboxModuleBuilder UseInMemoryIngress(
        this InboxModuleBuilder builder,
        Action<InMemoryInboxIngressModuleBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);
        return builder.RegisterIngress(new InMemoryInboxIngressModule(configure));
    }
}