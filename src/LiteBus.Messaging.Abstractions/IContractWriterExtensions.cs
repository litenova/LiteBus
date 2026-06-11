using System.Reflection;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Extension methods for <see cref="IContractWriter" /> contract discovery.
/// </summary>
public static class IContractWriterExtensions
{
    /// <summary>
    ///     Registers every closed type in an assembly that declares <see cref="MessageContractAttribute" />.
    /// </summary>
    /// <param name="writer">The contract writer to populate.</param>
    /// <param name="assembly">The assembly to scan.</param>
    /// <returns>The same writer instance for chaining.</returns>
    public static IContractWriter RegisterFromAssembly(this IContractWriter writer, Assembly assembly)
    {
        return writer.AddFromAssembly(assembly);
    }
}