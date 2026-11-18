using System;
using System.Collections.Generic;
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

    /// <summary>
    ///     Determines whether the message type's assembly delegates contract registration to another assembly.
    /// </summary>
    /// <param name="messageType">The message type symbol.</param>
    /// <param name="compilation">The compilation being analyzed.</param>
    /// <returns>
    ///     <see langword="true" /> when the containing assembly carries <c>MessageContractsRegisteredIn</c> naming
    ///     an assembly other than the analyzed compilation; otherwise, <see langword="false" />.
    /// </returns>
    internal static bool IsRegisteredInAnotherAssembly(ITypeSymbol messageType, Compilation compilation)
    {
        var names = GetRegisteredInAssemblyNames(messageType.ContainingAssembly);

        return names.Count > 0 && !names.Contains(compilation.Assembly.Name);
    }

    /// <summary>
    ///     Determines whether the assembly delegates contract registration to the analyzed compilation.
    /// </summary>
    /// <param name="assembly">The referenced assembly symbol.</param>
    /// <param name="compilation">The compilation being analyzed.</param>
    /// <returns>
    ///     <see langword="true" /> when the assembly names the analyzed compilation; otherwise,
    ///     <see langword="false" />.
    /// </returns>
    internal static bool IsRegisteredInCurrentAssembly(IAssemblySymbol assembly, Compilation compilation)
    {
        return GetRegisteredInAssemblyNames(assembly).Contains(compilation.Assembly.Name);
    }

    /// <summary>
    ///     Gets the assembly names declared by <c>MessageContractsRegisteredIn</c> attributes on an assembly.
    /// </summary>
    /// <param name="assembly">The assembly symbol.</param>
    /// <returns>The declared registering assembly names, compared without regard to case.</returns>
    internal static ImmutableHashSet<string> GetRegisteredInAssemblyNames(IAssemblySymbol assembly)
    {
        var builder = ImmutableHashSet.CreateBuilder<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var attribute in assembly.GetAttributes())
        {
            if (attribute.AttributeClass?.Name != "MessageContractsRegisteredInAttribute" ||
                attribute.AttributeClass.ContainingNamespace?.ToDisplayString() is not
                    ("LiteBus.Messaging.Abstractions" or "LiteBus.Analyzers"))
            {
                continue;
            }

            if (attribute.ConstructorArguments.Length == 0 ||
                attribute.ConstructorArguments[0].Value is not string assemblyName ||
                string.IsNullOrWhiteSpace(assemblyName))
            {
                continue;
            }

            builder.Add(assemblyName);
        }

        return builder.ToImmutable();
    }

    /// <summary>
    ///     Gets the attributed durable message types of referenced assemblies that name the analyzed compilation
    ///     and lack explicit contract registration.
    /// </summary>
    /// <param name="compilation">The compilation being analyzed.</param>
    /// <param name="registeredTypes">Types registered through <c>Register</c>.</param>
    /// <param name="registeredAssemblies">Assemblies registered through <c>RegisterFromAssembly</c>.</param>
    /// <returns>The unregistered referenced contract types, ordered by assembly and metadata name.</returns>
    internal static ImmutableArray<INamedTypeSymbol> GetUnregisteredReferencedContracts(
        Compilation compilation,
        ImmutableHashSet<ITypeSymbol> registeredTypes,
        ImmutableHashSet<IAssemblySymbol> registeredAssemblies)
    {
        List<INamedTypeSymbol> unregistered = [];

        foreach (var assembly in compilation.SourceModule.ReferencedAssemblySymbols
                     .OrderBy(candidate => candidate.Name, StringComparer.Ordinal))
        {
            if (!IsRegisteredInCurrentAssembly(assembly, compilation))
            {
                continue;
            }

            foreach (var type in GetDeclaredTypes(assembly))
            {
                if (type.TypeKind == TypeKind.TypeParameter ||
                    HandlerAnalysis.IsGenericTypeDefinition(type) ||
                    !HasMessageContractAttribute(type) ||
                    !IsDurableMessageType(type, compilation))
                {
                    continue;
                }

                if (IsExplicitlyRegistered(type, registeredTypes, registeredAssemblies))
                {
                    continue;
                }

                unregistered.Add(type);
            }
        }

        return unregistered
            .OrderBy(type => type.ContainingAssembly.Name, StringComparer.Ordinal)
            .ThenBy(type => LiteBusSymbols.GetMetadataName(type), StringComparer.Ordinal)
            .ToImmutableArray();
    }

    /// <summary>
    ///     Gets every named type declared in an assembly, including nested types.
    /// </summary>
    /// <param name="assembly">The assembly symbol.</param>
    /// <returns>Every named type declared in the assembly's namespaces.</returns>
    internal static IEnumerable<INamedTypeSymbol> GetDeclaredTypes(IAssemblySymbol assembly)
    {
        var namespaces = new Stack<INamespaceSymbol>();
        namespaces.Push(assembly.GlobalNamespace);

        while (namespaces.Count > 0)
        {
            var current = namespaces.Pop();

            foreach (var nested in current.GetNamespaceMembers())
            {
                namespaces.Push(nested);
            }

            foreach (var type in current.GetTypeMembers())
            {
                yield return type;

                foreach (var nestedType in GetNestedTypes(type))
                {
                    yield return nestedType;
                }
            }
        }
    }

    /// <summary>
    ///     Gets a named type's nested types, recursively.
    /// </summary>
    /// <param name="type">The containing type symbol.</param>
    /// <returns>The nested type symbols.</returns>
    private static IEnumerable<INamedTypeSymbol> GetNestedTypes(INamedTypeSymbol type)
    {
        foreach (var nested in type.GetTypeMembers())
        {
            yield return nested;

            foreach (var descendant in GetNestedTypes(nested))
            {
                yield return descendant;
            }
        }
    }
}
