using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace LiteBus.Analyzers.Analysis;

/// <summary>
///     Shared helpers for durable contract registration discovery.
/// </summary>
internal static class ContractRegistrationAnalysis
{
    /// <summary>
    ///     Determines whether the message type is covered by explicit registration or assembly scanning.
    /// </summary>
    /// <param name="messageType">The message type symbol.</param>
    /// <param name="registeredTypes">Types registered through <c>Register</c>.</param>
    /// <param name="registeredAssemblies">Assemblies registered through <c>RegisterFromAssembly</c>.</param>
    /// <returns><see langword="true" /> when the type is explicitly registered.</returns>
    internal static bool IsExplicitlyRegistered(
        ITypeSymbol messageType,
        ImmutableHashSet<ITypeSymbol> registeredTypes,
        ImmutableHashSet<IAssemblySymbol> registeredAssemblies)
    {
        if (registeredTypes.Contains(messageType, SymbolEqualityComparer.Default))
        {
            return true;
        }

        return registeredAssemblies.Contains(messageType.ContainingAssembly, SymbolEqualityComparer.Default);
    }

    /// <summary>
    ///     Gets the closed registration type display string for diagnostics.
    /// </summary>
    /// <param name="messageType">The message type symbol.</param>
    /// <returns>The closed type display string.</returns>
    internal static string GetClosedRegistrationTypeDisplay(ITypeSymbol messageType)
    {
        if (messageType is INamedTypeSymbol { IsGenericType: true, TypeArguments.Length: > 0 } namedType &&
            !HandlerAnalysis.IsGenericTypeDefinition(namedType))
        {
            return namedType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
        }

        return messageType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
    }

    /// <summary>
    ///     Determines whether the type declares <c>[MessageContract]</c>.
    /// </summary>
    /// <param name="messageType">The message type symbol.</param>
    /// <returns><see langword="true" /> when the attribute is present; otherwise, <see langword="false" />.</returns>
    internal static bool HasMessageContractAttribute(ITypeSymbol messageType)
    {
        if (messageType is not INamedTypeSymbol namedType)
        {
            return false;
        }

        foreach (var attribute in namedType.GetAttributes())
        {
            if (attribute.AttributeClass?.Name == "MessageContractAttribute" &&
                attribute.AttributeClass.ContainingNamespace?.ToDisplayString() is
                    "LiteBus.Messaging.Abstractions" or "LiteBus.Analyzers")
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     Determines whether the type is a durable command or event message.
    /// </summary>
    /// <param name="messageType">The message type symbol.</param>
    /// <param name="compilation">The compilation being analyzed.</param>
    /// <returns><see langword="true" /> when the type implements a durable message contract.</returns>
    internal static bool IsDurableMessageType(ITypeSymbol messageType, Compilation compilation)
    {
        var command = compilation.GetTypeByMetadataName("LiteBus.Commands.Abstractions.ICommand");
        var commandWithResult = compilation.GetTypeByMetadataName("LiteBus.Commands.Abstractions.ICommand`1");
        var eventType = compilation.GetTypeByMetadataName("LiteBus.Events.Abstractions.IEvent");

        foreach (var candidate in messageType.AllInterfaces)
        {
            var original = candidate.OriginalDefinition;

            if (command is not null && SymbolEqualityComparer.Default.Equals(original, command))
            {
                return true;
            }

            if (commandWithResult is not null && SymbolEqualityComparer.Default.Equals(original, commandWithResult))
            {
                return true;
            }

            if (eventType is not null && SymbolEqualityComparer.Default.Equals(original, eventType))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     Determines whether any durable message marker interface resolves in the compilation.
    /// </summary>
    /// <param name="compilation">The compilation being analyzed.</param>
    /// <returns><see langword="true" /> when a durable marker resolves; otherwise, <see langword="false" />.</returns>
    internal static bool AnyDurableMarkerResolves(Compilation compilation)
    {
        return compilation.GetTypeByMetadataName("LiteBus.Commands.Abstractions.ICommand") is not null ||
            compilation.GetTypeByMetadataName("LiteBus.Commands.Abstractions.ICommand`1") is not null ||
            compilation.GetTypeByMetadataName("LiteBus.Events.Abstractions.IEvent") is not null;
    }
}
