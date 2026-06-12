using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LiteBus.Analyzers.Analysis;

/// <summary>
///     Shared handler discovery helpers.
/// </summary>
internal static class HandlerAnalysis
{
    private static readonly (string MetadataName, string Pipeline)[] HandlerInterfaceMap =
    {
        ("LiteBus.Commands.Abstractions.ICommandHandler`1", "command"),
        ("LiteBus.Commands.Abstractions.ICommandHandler`2", "command"),
        ("LiteBus.Events.Abstractions.IEventHandler`1", "event"),
        ("LiteBus.Queries.Abstractions.IQueryHandler`2", "query"),
        ("LiteBus.Queries.Abstractions.IStreamQueryHandler`2", "stream query"),
        ("LiteBus.Commands.Abstractions.ICommandPreHandler`1", "command pre-handler"),
        ("LiteBus.Events.Abstractions.IEventPreHandler`1", "event pre-handler"),
        ("LiteBus.Queries.Abstractions.IQueryPreHandler`1", "query pre-handler"),
        ("LiteBus.Commands.Abstractions.ICommandPostHandler`1", "command post-handler"),
        ("LiteBus.Events.Abstractions.IEventPostHandler`1", "event post-handler"),
        ("LiteBus.Queries.Abstractions.IQueryPostHandler`1", "query post-handler"),
        ("LiteBus.Commands.Abstractions.ICommandErrorHandler`1", "command error-handler"),
        ("LiteBus.Commands.Abstractions.ICommandErrorHandler`2", "command error-handler"),
        ("LiteBus.Events.Abstractions.IEventErrorHandler`1", "event error-handler"),
        ("LiteBus.Queries.Abstractions.IQueryErrorHandler`1", "query error-handler"),
        ("LiteBus.Queries.Abstractions.IQueryErrorHandler`2", "query error-handler")
    };

    /// <summary>
    ///     Collects handler registrations declared in the compilation.
    /// </summary>
    /// <param name="compilation">The compilation being analyzed.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>All discovered handler registrations.</returns>
    internal static ImmutableArray<HandlerRegistration> CollectHandlerRegistrations(
        Compilation compilation,
        CancellationToken cancellationToken = default)
    {
        var builder = ImmutableArray.CreateBuilder<HandlerRegistration>();
        var processed = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);

        foreach (var syntaxTree in compilation.SyntaxTrees)
        {
            foreach (var typeDeclaration in syntaxTree.GetRoot(cancellationToken).DescendantNodes()
                         .OfType<TypeDeclarationSyntax>())
            {
                var model = compilation.GetSemanticModel(syntaxTree);

                if (model.GetDeclaredSymbol(typeDeclaration, cancellationToken) is INamedTypeSymbol symbol)
                {
                    TryAddHandlerRegistration(compilation, symbol, builder, processed);
                }
            }
        }

        foreach (var reference in compilation.References)
        {
            if (compilation.GetAssemblyOrModuleSymbol(reference) is not IAssemblySymbol assemblySymbol ||
                !ShouldScanReferencedAssembly(assemblySymbol, compilation.Assembly))
            {
                continue;
            }

            foreach (var module in assemblySymbol.Modules)
            {
                CollectHandlerRegistrationsFromNamespace(
                    compilation,
                    module.GlobalNamespace,
                    builder,
                    processed);
            }
        }

        return builder.ToImmutable();
    }

    /// <summary>
    ///     Adds handler registrations declared on one named type symbol when it has not already been processed.
    /// </summary>
    /// <param name="compilation">The compilation being analyzed.</param>
    /// <param name="symbol">The candidate handler type symbol.</param>
    /// <param name="builder">The registration builder.</param>
    /// <param name="processed">The set of handler types already processed.</param>
    private static void TryAddHandlerRegistration(
        Compilation compilation,
        INamedTypeSymbol symbol,
        ImmutableArray<HandlerRegistration>.Builder builder,
        HashSet<INamedTypeSymbol> processed)
    {
        if (symbol.TypeKind == TypeKind.Interface || !processed.Add(symbol))
        {
            return;
        }

        var location = symbol.Locations.FirstOrDefault(locationCandidate => locationCandidate.IsInSource) ?? symbol.Locations.FirstOrDefault() ?? Location.None;

        foreach (var handlerInterface in symbol.AllInterfaces)
        {
            var pipeline = GetPipeline(compilation, handlerInterface);

            if (pipeline is null)
            {
                continue;
            }

            var messageType = handlerInterface.TypeArguments[0];
            builder.Add(new HandlerRegistration(symbol, messageType, pipeline, location));
        }

        foreach (var nestedType in symbol.GetTypeMembers())
        {
            TryAddHandlerRegistration(compilation, nestedType, builder, processed);
        }
    }

    /// <summary>
    ///     Walks a namespace and registers handler types declared beneath it.
    /// </summary>
    /// <param name="compilation">The compilation being analyzed.</param>
    /// <param name="namespaceSymbol">The namespace to walk.</param>
    /// <param name="builder">The registration builder.</param>
    /// <param name="processed">The set of handler types already processed.</param>
    private static void CollectHandlerRegistrationsFromNamespace(
        Compilation compilation,
        INamespaceSymbol namespaceSymbol,
        ImmutableArray<HandlerRegistration>.Builder builder,
        HashSet<INamedTypeSymbol> processed)
    {
        foreach (var member in namespaceSymbol.GetMembers())
        {
            if (member is INamespaceSymbol nestedNamespace)
            {
                CollectHandlerRegistrationsFromNamespace(compilation, nestedNamespace, builder, processed);
            }
            else if (member is INamedTypeSymbol namedType)
            {
                TryAddHandlerRegistration(compilation, namedType, builder, processed);
            }
        }
    }

    /// <summary>
    ///     Determines whether handler registrations should be collected from a referenced assembly.
    /// </summary>
    /// <param name="assemblySymbol">The referenced assembly symbol.</param>
    /// <param name="compilationAssembly">The assembly under analysis.</param>
    /// <returns><see langword="true" /> when the referenced assembly may contain application handlers.</returns>
    private static bool ShouldScanReferencedAssembly(IAssemblySymbol assemblySymbol, IAssemblySymbol compilationAssembly)
    {
        if (SymbolEqualityComparer.Default.Equals(assemblySymbol, compilationAssembly))
        {
            return false;
        }

        var name = assemblySymbol.Name;

        return !name.StartsWith("System", StringComparison.Ordinal) &&
               !name.StartsWith("Microsoft", StringComparison.Ordinal) &&
               !name.StartsWith("netstandard", StringComparison.Ordinal) &&
               !name.StartsWith("LiteBus", StringComparison.Ordinal);
    }

    /// <summary>
    ///     Resolves the pipeline stage for a handler interface symbol.
    /// </summary>
    /// <param name="compilation">The compilation being analyzed.</param>
    /// <param name="handlerInterface">The implemented handler interface symbol.</param>
    /// <returns>The pipeline stage name, if matched.</returns>
    private static string? GetPipeline(Compilation compilation, INamedTypeSymbol handlerInterface)
    {
        var original = handlerInterface.OriginalDefinition;

        foreach (var entry in HandlerInterfaceMap)
        {
            var interfaceType = compilation.GetTypeByMetadataName(entry.MetadataName);

            if (interfaceType is not null &&
                SymbolEqualityComparer.Default.Equals(original, interfaceType))
            {
                return entry.Pipeline;
            }
        }

        return null;
    }

    /// <summary>
    ///     Determines whether the type implements any LiteBus handler interface.
    /// </summary>
    /// <param name="handlerType">The candidate handler type symbol.</param>
    /// <param name="compilation">The compilation being analyzed.</param>
    /// <returns><see langword="true" /> when the type implements a handler interface.</returns>
    internal static bool IsHandlerType(INamedTypeSymbol handlerType, Compilation compilation)
    {
        foreach (var handlerInterface in handlerType.AllInterfaces)
        {
            if (GetPipeline(compilation, handlerInterface) is not null)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     Gets a stable display string for a message type symbol.
    /// </summary>
    /// <param name="messageType">The message type symbol.</param>
    /// <returns>The display string.</returns>
    internal static string GetMessageTypeDisplay(ITypeSymbol messageType)
    {
        return messageType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
    }

    /// <summary>
    ///     Determines whether the handler type is an unsupported open generic handler shape.
    /// </summary>
    /// <param name="handlerType">The handler type symbol.</param>
    /// <returns><see langword="true" /> when the open generic shape is unsupported; otherwise, <see langword="false" />.</returns>
    internal static bool IsUnsupportedOpenGenericHandler(INamedTypeSymbol handlerType)
    {
        if (!handlerType.IsGenericType)
        {
            return false;
        }

        var openDefinition = IsGenericTypeDefinition(handlerType)
            ? handlerType
            : handlerType.OriginalDefinition;

        if (!UsesBareMessageTypeParameter(openDefinition))
        {
            return false;
        }

        return openDefinition.TypeParameters.Length != 1;
    }

    /// <summary>
    ///     Determines whether an open generic handler uses a bare message type parameter.
    /// </summary>
    /// <param name="openDefinition">The open generic handler type definition.</param>
    /// <returns><see langword="true" /> when a handler interface message argument is a type parameter.</returns>
    internal static bool UsesBareMessageTypeParameter(INamedTypeSymbol openDefinition)
    {
        foreach (var handlerInterface in openDefinition.AllInterfaces)
        {
            if (handlerInterface.TypeArguments.Length == 0)
            {
                continue;
            }

            if (handlerInterface.TypeArguments[0].TypeKind == TypeKind.TypeParameter)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     Determines whether the named type symbol is an open generic type definition.
    /// </summary>
    /// <param name="type">The type symbol.</param>
    /// <returns><see langword="true" /> when the symbol is a generic type definition; otherwise, <see langword="false" />.</returns>
    internal static bool IsGenericTypeDefinition(INamedTypeSymbol type)
    {
        return type is { IsGenericType: true } &&
               SymbolEqualityComparer.Default.Equals(type, type.OriginalDefinition);
    }

    /// <summary>
    ///     Determines whether the type symbol is an open generic type definition.
    /// </summary>
    /// <param name="type">The type symbol.</param>
    /// <returns><see langword="true" /> when the symbol is a generic type definition; otherwise, <see langword="false" />.</returns>
    internal static bool IsGenericTypeDefinition(ITypeSymbol type)
    {
        return type is INamedTypeSymbol namedType && IsGenericTypeDefinition(namedType);
    }
}