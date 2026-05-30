using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace LiteBus.Analyzers.Analysis;

/// <summary>
///     Identifies LiteBus command and query message types declared in a compilation.
/// </summary>
internal enum MessageKind
{
    /// <summary>
    ///     Command messages implementing <see cref="LiteBus.Commands.Abstractions.ICommand" />.
    /// </summary>
    Command,

    /// <summary>
    ///     Query messages implementing <see cref="LiteBus.Queries.Abstractions.IQuery" />.
    /// </summary>
    Query,
}

/// <summary>
///     Shared helpers for discovering LiteBus message types and handler coverage.
/// </summary>
internal static class MessageAnalysis
{
    /// <summary>
    ///     Metadata names for command message marker interfaces.
    /// </summary>
    private static readonly string[] CommandMessageMetadataNames =
    {
        "LiteBus.Commands.Abstractions.ICommand",
        "LiteBus.Commands.Abstractions.ICommand`1",
    };

    /// <summary>
    ///     Metadata names for query message marker interfaces.
    /// </summary>
    private static readonly string[] QueryMessageMetadataNames =
    {
        "LiteBus.Queries.Abstractions.IQuery",
        "LiteBus.Queries.Abstractions.IQuery`1",
    };

    /// <summary>
    ///     Metadata names for open generic main command handler interfaces.
    /// </summary>
    private static readonly string[] OpenGenericCommandHandlerMetadataNames =
    {
        "LiteBus.Commands.Abstractions.ICommandHandler`1",
        "LiteBus.Commands.Abstractions.ICommandHandler`2",
    };

    /// <summary>
    ///     Metadata names for open generic main query handler interfaces.
    /// </summary>
    private static readonly string[] OpenGenericQueryHandlerMetadataNames =
    {
        "LiteBus.Queries.Abstractions.IQueryHandler`2",
    };

    /// <summary>
    ///     Collects concrete command or query message types declared in the compilation.
    /// </summary>
    /// <param name="compilation">The compilation being analyzed.</param>
    /// <param name="kind">The message kind to collect.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The discovered message type symbols.</returns>
    internal static ImmutableArray<MessageTypeRegistration> CollectMessageTypes(
        Compilation compilation,
        MessageKind kind,
        System.Threading.CancellationToken cancellationToken = default)
    {
        var markerMetadataNames = kind == MessageKind.Command
            ? CommandMessageMetadataNames
            : QueryMessageMetadataNames;

        var builder = ImmutableArray.CreateBuilder<MessageTypeRegistration>();

        foreach (var syntaxTree in compilation.SyntaxTrees)
        {
            foreach (var typeDeclaration in syntaxTree.GetRoot(cancellationToken).DescendantNodes()
                         .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.TypeDeclarationSyntax>())
            {
                var model = compilation.GetSemanticModel(syntaxTree);
                var symbol = model.GetDeclaredSymbol(typeDeclaration, cancellationToken) as INamedTypeSymbol;

                if (symbol is null || !IsAnalyzableMessageType(symbol))
                {
                    continue;
                }

                if (!ImplementsAnyMarkerInterface(symbol, compilation, markerMetadataNames))
                {
                    continue;
                }

                var location = symbol.Locations.FirstOrDefault() ?? Location.None;
                builder.Add(new MessageTypeRegistration(symbol, location));
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
        System.Threading.CancellationToken cancellationToken = default)
    {
        var handlerMetadataNames = kind == MessageKind.Command
            ? OpenGenericCommandHandlerMetadataNames
            : OpenGenericQueryHandlerMetadataNames;

        var builder = ImmutableArray.CreateBuilder<INamedTypeSymbol>();

        foreach (var syntaxTree in compilation.SyntaxTrees)
        {
            foreach (var typeDeclaration in syntaxTree.GetRoot(cancellationToken).DescendantNodes()
                         .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.TypeDeclarationSyntax>())
            {
                var model = compilation.GetSemanticModel(syntaxTree);
                var symbol = model.GetDeclaredSymbol(typeDeclaration, cancellationToken) as INamedTypeSymbol;

                if (symbol is null ||
                    !HandlerAnalysis.IsGenericTypeDefinition(symbol) ||
                    !HandlerAnalysis.UsesBareMessageTypeParameter(symbol))
                {
                    continue;
                }

                if (!ImplementsAnyMainHandlerInterface(symbol, compilation, handlerMetadataNames))
                {
                    continue;
                }

                builder.Add(symbol);
            }
        }

        return builder.ToImmutable();
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
    /// <returns><see langword="true" /> when the handler would close for the message type; otherwise, <see langword="false" />.</returns>
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
    /// <returns><see langword="true" /> when the source type is assignable to the target type; otherwise, <see langword="false" />.</returns>
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

/// <summary>
///     Describes one discovered LiteBus message type declared in a compilation.
/// </summary>
internal sealed class MessageTypeRegistration
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="MessageTypeRegistration" /> class.
    /// </summary>
    /// <param name="messageType">The message type symbol.</param>
    /// <param name="location">The diagnostic location.</param>
    internal MessageTypeRegistration(INamedTypeSymbol messageType, Location location)
    {
        MessageType = messageType;
        Location = location;
    }

    /// <summary>
    ///     Gets the message type symbol.
    /// </summary>
    internal INamedTypeSymbol MessageType { get; }

    /// <summary>
    ///     Gets the diagnostic location.
    /// </summary>
    internal Location Location { get; }
}
