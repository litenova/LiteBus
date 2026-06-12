using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LiteBus.Analyzers.Analysis;

/// <summary>
///     Shared helpers for discovering LiteBus message types and handler coverage.
/// </summary>
internal static class MessageAnalysis
{
    /// <summary>
    ///     Metadata names for command message marker interfaces.
    /// </summary>
    private static readonly string[] CommandMessageMetadataNames =
    [
        "LiteBus.Commands.Abstractions.ICommand",
        "LiteBus.Commands.Abstractions.ICommand`1"
    ];

    /// <summary>
    ///     Metadata names for query message marker interfaces.
    /// </summary>
    private static readonly string[] QueryMessageMetadataNames =
    [
        "LiteBus.Queries.Abstractions.IQuery",
        "LiteBus.Queries.Abstractions.IQuery`1"
    ];

    /// <summary>
    ///     Metadata names for stream query message marker interfaces.
    /// </summary>
    private static readonly string[] StreamQueryMessageMetadataNames =
    [
        "LiteBus.Queries.Abstractions.IStreamQuery`1"
    ];

    /// <summary>
    ///     Metadata names for open generic main command handler interfaces.
    /// </summary>
    private static readonly string[] OpenGenericCommandHandlerMetadataNames =
    [
        "LiteBus.Commands.Abstractions.ICommandHandler`1",
        "LiteBus.Commands.Abstractions.ICommandHandler`2"
    ];

    /// <summary>
    ///     Metadata names for open generic main query handler interfaces.
    /// </summary>
    private static readonly string[] OpenGenericQueryHandlerMetadataNames =
    [
        "LiteBus.Queries.Abstractions.IQueryHandler`2"
    ];

    /// <summary>
    ///     Metadata names for open generic main stream query handler interfaces.
    /// </summary>
    private static readonly string[] OpenGenericStreamQueryHandlerMetadataNames =
    [
        "LiteBus.Queries.Abstractions.IStreamQueryHandler`2"
    ];

    /// <summary>
    ///     Collects concrete command, query, or stream query message types declared in the compilation and eligible referenced assemblies.
    /// </summary>
    /// <param name="compilation">The compilation being analyzed.</param>
    /// <param name="kind">The message kind to collect.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The discovered message type symbols.</returns>
    internal static ImmutableArray<MessageTypeRegistration> CollectMessageTypes(
        Compilation compilation,
        MessageKind kind,
        CancellationToken cancellationToken = default)
    {
        var markerMetadataNames = GetMarkerMetadataNames(kind);
        var builder = ImmutableArray.CreateBuilder<MessageTypeRegistration>();
        var processed = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);

        foreach (var syntaxTree in compilation.SyntaxTrees)
        {
            foreach (var typeDeclaration in syntaxTree.GetRoot(cancellationToken).DescendantNodes()
                         .OfType<TypeDeclarationSyntax>())
            {
                var model = compilation.GetSemanticModel(syntaxTree);
                var symbol = model.GetDeclaredSymbol(typeDeclaration, cancellationToken) as INamedTypeSymbol;

                if (symbol is not null)
                {
                    TryAddMessageTypeRegistration(compilation, symbol, kind, markerMetadataNames, builder, processed);
                }
            }
        }

        foreach (var reference in compilation.References)
        {
            if (compilation.GetAssemblyOrModuleSymbol(reference) is not IAssemblySymbol assemblySymbol ||
                !HandlerAnalysis.ShouldScanReferencedAssembly(assemblySymbol, compilation.Assembly))
            {
                continue;
            }

            foreach (var module in assemblySymbol.Modules)
            {
                CollectMessageTypesFromNamespace(
                    compilation,
                    module.GlobalNamespace,
                    kind,
                    markerMetadataNames,
                    builder,
                    processed);
            }
        }

        return builder.ToImmutable();
    }

    /// <summary>
    ///     Determines whether a message type has a main handler in the compilation.
    /// </summary>
    /// <param name="messageType">The message type symbol.</param>
    /// <param name="handlers">The discovered main handler registrations.</param>
    /// <param name="openGenericHandlers">The discovered open generic main handler type definitions.</param>
    /// <param name="compilation">The compilation being analyzed.</param>
    /// <returns><see langword="true" /> when a handler covers the message type; otherwise, <see langword="false" />.</returns>
    internal static bool HasMainHandler(
        INamedTypeSymbol messageType,
        ImmutableArray<HandlerRegistration> handlers,
        ImmutableArray<INamedTypeSymbol> openGenericHandlers,
        Compilation compilation)
    {
        foreach (var handler in handlers)
        {
            if (HandlerCoversMessageType(handler.MessageType, messageType, compilation))
            {
                return true;
            }
        }

        foreach (var openGenericHandler in openGenericHandlers)
        {
            if (OpenGenericHandlerCoversMessageType(openGenericHandler, messageType, compilation))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     Collects open generic main handler type definitions for the supplied message kind.
    /// </summary>
    /// <param name="compilation">The compilation being analyzed.</param>
    /// <param name="kind">The message kind to inspect.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The open generic main handler type definitions.</returns>
    internal static ImmutableArray<INamedTypeSymbol> CollectOpenGenericMainHandlers(
        Compilation compilation,
        MessageKind kind,
        CancellationToken cancellationToken = default)
    {
        var handlerMetadataNames = GetOpenGenericHandlerMetadataNames(kind);
        var builder = ImmutableArray.CreateBuilder<INamedTypeSymbol>();
        var processed = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);

        foreach (var syntaxTree in compilation.SyntaxTrees)
        {
            foreach (var typeDeclaration in syntaxTree.GetRoot(cancellationToken).DescendantNodes()
                         .OfType<TypeDeclarationSyntax>())
            {
                var model = compilation.GetSemanticModel(syntaxTree);
                var symbol = model.GetDeclaredSymbol(typeDeclaration, cancellationToken) as INamedTypeSymbol;

                if (symbol is not null)
                {
                    TryAddOpenGenericMainHandler(compilation, symbol, handlerMetadataNames, builder, processed);
                }
            }
        }

        foreach (var reference in compilation.References)
        {
            if (compilation.GetAssemblyOrModuleSymbol(reference) is not IAssemblySymbol assemblySymbol ||
                !HandlerAnalysis.ShouldScanReferencedAssembly(assemblySymbol, compilation.Assembly))
            {
                continue;
            }

            foreach (var module in assemblySymbol.Modules)
            {
                CollectOpenGenericMainHandlersFromNamespace(
                    compilation,
                    module.GlobalNamespace,
                    handlerMetadataNames,
                    builder,
                    processed);
            }
        }

        return builder.ToImmutable();
    }

    /// <summary>
    ///     Determines whether the type implements <c>IStreamQuery&lt;TResult&gt;</c>.
    /// </summary>
    /// <param name="type">The candidate message type symbol.</param>
    /// <param name="compilation">The compilation being analyzed.</param>
    /// <returns><see langword="true" /> when the type is a stream query; otherwise, <see langword="false" />.</returns>
    internal static bool IsStreamQueryType(INamedTypeSymbol type, Compilation compilation)
    {
        var streamQueryMarker = compilation.GetTypeByMetadataName("LiteBus.Queries.Abstractions.IStreamQuery`1");

        if (streamQueryMarker is null)
        {
            return false;
        }

        foreach (var candidate in type.AllInterfaces)
        {
            if (SymbolEqualityComparer.Default.Equals(candidate.OriginalDefinition, streamQueryMarker))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     Gets marker interface metadata names for the supplied message kind.
    /// </summary>
    /// <param name="kind">The message kind.</param>
    /// <returns>The marker interface metadata names.</returns>
    private static string[] GetMarkerMetadataNames(MessageKind kind)
    {
        return kind switch
        {
            MessageKind.Command => CommandMessageMetadataNames,
            MessageKind.Query => QueryMessageMetadataNames,
            MessageKind.StreamQuery => StreamQueryMessageMetadataNames,
            _ => QueryMessageMetadataNames
        };
    }

    /// <summary>
    ///     Gets open generic main handler metadata names for the supplied message kind.
    /// </summary>
    /// <param name="kind">The message kind.</param>
    /// <returns>The open generic handler metadata names.</returns>
    private static string[] GetOpenGenericHandlerMetadataNames(MessageKind kind)
    {
        return kind switch
        {
            MessageKind.Command => OpenGenericCommandHandlerMetadataNames,
            MessageKind.Query => OpenGenericQueryHandlerMetadataNames,
            MessageKind.StreamQuery => OpenGenericStreamQueryHandlerMetadataNames,
            _ => OpenGenericQueryHandlerMetadataNames
        };
    }

    /// <summary>
    ///     Adds a message type registration when the symbol matches the requested kind and has not already been processed.
    /// </summary>
    /// <param name="compilation">The compilation being analyzed.</param>
    /// <param name="symbol">The candidate type symbol.</param>
    /// <param name="kind">The message kind being collected.</param>
    /// <param name="markerMetadataNames">The marker interface metadata names.</param>
    /// <param name="builder">The registration builder.</param>
    /// <param name="processed">The set of message types already processed.</param>
    private static void TryAddMessageTypeRegistration(
        Compilation compilation,
        INamedTypeSymbol symbol,
        MessageKind kind,
        string[] markerMetadataNames,
        ImmutableArray<MessageTypeRegistration>.Builder builder,
        HashSet<INamedTypeSymbol> processed)
    {
        if (!processed.Add(symbol))
        {
            return;
        }

        if (!IsAnalyzableMessageType(symbol) ||
            !ImplementsAnyMarkerInterface(symbol, compilation, markerMetadataNames))
        {
            foreach (var nestedType in symbol.GetTypeMembers())
            {
                TryAddMessageTypeRegistration(
                    compilation,
                    nestedType,
                    kind,
                    markerMetadataNames,
                    builder,
                    processed);
            }

            return;
        }

        symbol = LiteBusSymbols.RetargetToCompilation(compilation, symbol);

        if (kind == MessageKind.Query && IsStreamQueryType(symbol, compilation))
        {
            foreach (var nestedType in symbol.GetTypeMembers())
            {
                TryAddMessageTypeRegistration(
                    compilation,
                    nestedType,
                    kind,
                    markerMetadataNames,
                    builder,
                    processed);
            }

            return;
        }

        var location = LiteBusSymbols.GetDiagnosticLocation(
            compilation,
            symbol.Locations.FirstOrDefault(locationCandidate => locationCandidate.IsInSource) ??
            symbol.Locations.FirstOrDefault() ??
            Location.None);
        builder.Add(new MessageTypeRegistration(symbol, location));

        foreach (var nestedType in symbol.GetTypeMembers())
        {
            TryAddMessageTypeRegistration(
                compilation,
                nestedType,
                kind,
                markerMetadataNames,
                builder,
                processed);
        }
    }

    /// <summary>
    ///     Walks a namespace and registers message types declared beneath it.
    /// </summary>
    /// <param name="compilation">The compilation being analyzed.</param>
    /// <param name="namespaceSymbol">The namespace to walk.</param>
    /// <param name="kind">The message kind being collected.</param>
    /// <param name="markerMetadataNames">The marker interface metadata names.</param>
    /// <param name="builder">The registration builder.</param>
    /// <param name="processed">The set of message types already processed.</param>
    private static void CollectMessageTypesFromNamespace(
        Compilation compilation,
        INamespaceSymbol namespaceSymbol,
        MessageKind kind,
        string[] markerMetadataNames,
        ImmutableArray<MessageTypeRegistration>.Builder builder,
        HashSet<INamedTypeSymbol> processed)
    {
        foreach (var member in namespaceSymbol.GetMembers())
        {
            if (member is INamespaceSymbol nestedNamespace)
            {
                CollectMessageTypesFromNamespace(
                    compilation,
                    nestedNamespace,
                    kind,
                    markerMetadataNames,
                    builder,
                    processed);
            }
            else if (member is INamedTypeSymbol namedType)
            {
                TryAddMessageTypeRegistration(
                    compilation,
                    namedType,
                    kind,
                    markerMetadataNames,
                    builder,
                    processed);
            }
        }
    }

    /// <summary>
    ///     Adds an open generic main handler when the symbol matches and has not already been processed.
    /// </summary>
    /// <param name="compilation">The compilation being analyzed.</param>
    /// <param name="symbol">The candidate handler type symbol.</param>
    /// <param name="handlerMetadataNames">The main handler interface metadata names.</param>
    /// <param name="builder">The handler builder.</param>
    /// <param name="processed">The set of handler types already processed.</param>
    private static void TryAddOpenGenericMainHandler(
        Compilation compilation,
        INamedTypeSymbol symbol,
        string[] handlerMetadataNames,
        ImmutableArray<INamedTypeSymbol>.Builder builder,
        HashSet<INamedTypeSymbol> processed)
    {
        if (!processed.Add(symbol))
        {
            return;
        }

        if (HandlerAnalysis.IsGenericTypeDefinition(symbol) &&
            HandlerAnalysis.UsesBareMessageTypeParameter(symbol) &&
            ImplementsAnyMainHandlerInterface(symbol, compilation, handlerMetadataNames))
        {
            builder.Add(LiteBusSymbols.RetargetToCompilation(compilation, symbol));
        }

        foreach (var nestedType in symbol.GetTypeMembers())
        {
            TryAddOpenGenericMainHandler(compilation, nestedType, handlerMetadataNames, builder, processed);
        }
    }

    /// <summary>
    ///     Walks a namespace and collects open generic main handlers declared beneath it.
    /// </summary>
    /// <param name="compilation">The compilation being analyzed.</param>
    /// <param name="namespaceSymbol">The namespace to walk.</param>
    /// <param name="handlerMetadataNames">The main handler interface metadata names.</param>
    /// <param name="builder">The handler builder.</param>
    /// <param name="processed">The set of handler types already processed.</param>
    private static void CollectOpenGenericMainHandlersFromNamespace(
        Compilation compilation,
        INamespaceSymbol namespaceSymbol,
        string[] handlerMetadataNames,
        ImmutableArray<INamedTypeSymbol>.Builder builder,
        HashSet<INamedTypeSymbol> processed)
    {
        foreach (var member in namespaceSymbol.GetMembers())
        {
            if (member is INamespaceSymbol nestedNamespace)
            {
                CollectOpenGenericMainHandlersFromNamespace(
                    compilation,
                    nestedNamespace,
                    handlerMetadataNames,
                    builder,
                    processed);
            }
            else if (member is INamedTypeSymbol namedType)
            {
                TryAddOpenGenericMainHandler(compilation, namedType, handlerMetadataNames, builder, processed);
            }
        }
    }

    /// <summary>
    ///     Determines whether the type symbol is a concrete message type analyzers should inspect.
    /// </summary>
    /// <param name="type">The candidate type symbol.</param>
    /// <returns><see langword="true" /> when the type should be analyzed; otherwise, <see langword="false" />.</returns>
    private static bool IsAnalyzableMessageType(INamedTypeSymbol type)
    {
        return type.TypeKind is TypeKind.Class or TypeKind.Struct &&
               !type.IsAbstract &&
               !HandlerAnalysis.IsGenericTypeDefinition(type);
    }

    /// <summary>
    ///     Determines whether the type implements one of the supplied marker interfaces.
    /// </summary>
    /// <param name="type">The candidate type symbol.</param>
    /// <param name="compilation">The compilation being analyzed.</param>
    /// <param name="markerMetadataNames">The marker interface metadata names.</param>
    /// <returns><see langword="true" /> when a marker interface is implemented; otherwise, <see langword="false" />.</returns>
    private static bool ImplementsAnyMarkerInterface(
        INamedTypeSymbol type,
        Compilation compilation,
        string[] markerMetadataNames)
    {
        foreach (var metadataName in markerMetadataNames)
        {
            var markerType = compilation.GetTypeByMetadataName(metadataName);

            if (markerType is not null && LiteBusSymbols.Implements(type, markerType))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     Determines whether the type implements one of the supplied main handler interfaces.
    /// </summary>
    /// <param name="type">The candidate handler type symbol.</param>
    /// <param name="compilation">The compilation being analyzed.</param>
    /// <param name="handlerMetadataNames">The main handler interface metadata names.</param>
    /// <returns><see langword="true" /> when a main handler interface is implemented; otherwise, <see langword="false" />.</returns>
    private static bool ImplementsAnyMainHandlerInterface(
        INamedTypeSymbol type,
        Compilation compilation,
        string[] handlerMetadataNames)
    {
        foreach (var metadataName in handlerMetadataNames)
        {
            if (LiteBusSymbols.ImplementsGenericInterface(type, compilation, metadataName))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     Determines whether a handler message type covers the supplied concrete message type.
    /// </summary>
    /// <param name="handlerMessageType">The handler's declared message type.</param>
    /// <param name="messageType">The concrete message type.</param>
    /// <param name="compilation">The compilation being analyzed.</param>
    /// <returns><see langword="true" /> when the handler covers the message type; otherwise, <see langword="false" />.</returns>
    private static bool HandlerCoversMessageType(
        ITypeSymbol handlerMessageType,
        INamedTypeSymbol messageType,
        Compilation compilation)
    {
        if (handlerMessageType.TypeKind == TypeKind.TypeParameter ||
            HandlerAnalysis.IsGenericTypeDefinition(handlerMessageType))
        {
            return false;
        }

        return IsAssignableTo(compilation, messageType, handlerMessageType);
    }

    /// <summary>
    ///     Determines whether an open generic handler would close for the supplied message type.
    /// </summary>
    /// <param name="openGenericHandler">The open generic handler type definition.</param>
    /// <param name="messageType">The concrete message type.</param>
    /// <param name="compilation">The compilation being analyzed.</param>
    /// <returns>
    ///     <see langword="true" /> when the handler would close for the message type; otherwise, <see langword="false" />
    ///     .
    /// </returns>
    private static bool OpenGenericHandlerCoversMessageType(
        INamedTypeSymbol openGenericHandler,
        INamedTypeSymbol messageType,
        Compilation compilation)
    {
        if (openGenericHandler.TypeParameters.Length != 1)
        {
            return false;
        }

        return SatisfiesGenericConstraints(openGenericHandler.TypeParameters[0], messageType, compilation);
    }

    /// <summary>
    ///     Determines whether a concrete type can be assigned to a target type.
    /// </summary>
    /// <param name="compilation">The compilation being analyzed.</param>
    /// <param name="source">The source type symbol.</param>
    /// <param name="target">The target type symbol.</param>
    /// <returns>
    ///     <see langword="true" /> when the source type is assignable to the target type; otherwise,
    ///     <see langword="false" />.
    /// </returns>
    private static bool IsAssignableTo(Compilation compilation, ITypeSymbol source, ITypeSymbol target)
    {
        return SymbolEqualityComparer.Default.Equals(source, target) ||
               compilation.ClassifyCommonConversion(source, target).IsImplicit;
    }

    /// <summary>
    ///     Determines whether a concrete type satisfies the generic parameter constraints.
    /// </summary>
    /// <param name="typeParameter">The generic type parameter symbol.</param>
    /// <param name="candidateType">The concrete candidate type symbol.</param>
    /// <param name="compilation">The compilation being analyzed.</param>
    /// <returns><see langword="true" /> when all constraints are satisfied; otherwise, <see langword="false" />.</returns>
    private static bool SatisfiesGenericConstraints(
        ITypeParameterSymbol typeParameter,
        INamedTypeSymbol candidateType,
        Compilation compilation)
    {
        foreach (var constraint in typeParameter.ConstraintTypes)
        {
            if (!IsAssignableTo(compilation, candidateType, constraint))
            {
                return false;
            }
        }

        if (typeParameter.HasReferenceTypeConstraint && candidateType.IsValueType)
        {
            return false;
        }

        if (typeParameter.HasValueTypeConstraint && !candidateType.IsValueType)
        {
            return false;
        }

        if (typeParameter.HasUnmanagedTypeConstraint && !candidateType.IsUnmanagedType)
        {
            return false;
        }

        if (typeParameter.HasConstructorConstraint &&
            !candidateType.IsValueType &&
            !candidateType.InstanceConstructors.Any(constructor =>
                constructor.Parameters.IsEmpty &&
                constructor.DeclaredAccessibility == Accessibility.Public))
        {
            return false;
        }

        return true;
    }
}
