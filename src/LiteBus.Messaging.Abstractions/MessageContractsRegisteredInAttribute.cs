using System;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Declares that the <see cref="MessageContractAttribute" /> types of this assembly are registered by another
///     assembly, such as the module that composes them.
/// </summary>
/// <remarks>
///     <para>
///         A modular monolith keeps published contracts in a small assembly that other modules reference, and registers
///         them where the owning module composes. Roslyn analyzers see one compilation at a time, so without this
///         attribute LB1017 reports every contract in the contracts assembly, and LB1007 reports every handler of those
///         contracts in a consuming module.
///     </para>
///     <para>
///         With the attribute, LB1017 and LB1007 trust the named assembly for the types declared here, and the named
///         assembly's own compilation reports any contract of this assembly that it does not register.
///     </para>
/// </remarks>
/// <example>
///     <code>[assembly: MessageContractsRegisteredIn("Shop.Ordering")]</code>
/// </example>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true, Inherited = false)]
public sealed class MessageContractsRegisteredInAttribute : Attribute
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="MessageContractsRegisteredInAttribute" /> class.
    /// </summary>
    /// <param name="assemblyName">The simple name of the assembly that registers this assembly's contracts.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="assemblyName" /> is null or whitespace.</exception>
    public MessageContractsRegisteredInAttribute(string assemblyName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyName);
        AssemblyName = assemblyName;
    }

    /// <summary>
    ///     Gets the simple name of the assembly that registers this assembly's contracts.
    /// </summary>
    public string AssemblyName { get; }
}
