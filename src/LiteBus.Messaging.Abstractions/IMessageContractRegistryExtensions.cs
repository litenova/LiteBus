using System;
using System.Reflection;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Extension methods for <see cref="IMessageContractRegistry" /> contract discovery.
/// </summary>
public static class IMessageContractRegistryExtensions
{
    /// <summary>
    ///     Registers every closed type in an assembly that declares <see cref="MessageContractAttribute" />.
    /// </summary>
    /// <param name="registry">The contract registry to populate.</param>
    /// <param name="assembly">The assembly to scan.</param>
    /// <returns>The same registry instance for chaining.</returns>
    public static IMessageContractRegistry RegisterFromAssembly(this IMessageContractRegistry registry, Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(registry);
        registry.AddFromAssembly(assembly);
        return registry;
    }
}
