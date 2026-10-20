using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;

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
    ///     Determines whether the type implements <c>IStreamQuery&lt;TResult&gt;</c>.
    /// </summary>
    /// <param name="type">The candidate message type symbol.</param>
    /// <param name="compilation">The compilation being analyzed.</param>
    /// <returns><see langword="true" /> when the type is a stream query; otherwise, <see langword="false" />.</returns>
    internal static bool IsStreamQueryType(INamedTypeSymbol type, Compilation compilation)
    {
        return IsStreamQueryType(type, new LiteBusTypeCache(compilation));
    }

    /// <summary>
    ///     Determines whether the type implements <c>IStreamQuery&lt;TResult&gt;</c>, sharing one type cache for the pass.
    /// </summary>
    /// <param name="type">The candidate message type symbol.</param>
    /// <param name="typeCache">The per-compilation type cache.</param>
    /// <returns><see langword="true" /> when the type is a stream query; otherwise, <see langword="false" />.</returns>
    internal static bool IsStreamQueryType(INamedTypeSymbol type, LiteBusTypeCache typeCache)
    {
        var streamQueryMarker = typeCache.GetType("LiteBus.Queries.Abstractions.IStreamQuery`1");

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
    internal static string[] GetMarkerMetadataNames(MessageKind kind)
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
    internal static string[] GetOpenGenericHandlerMetadataNames(MessageKind kind)
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
    ///     Determines whether any marker interface for the message kind resolves in the compilation.
    /// </summary>
    /// <param name="typeCache">The per-compilation type cache.</param>
    /// <param name="kind">The message kind.</param>
    /// <returns><see langword="true" /> when at least one marker interface resolves; otherwise, <see langword="false" />.</returns>
    internal static bool AnyMarkerInterfaceResolves(LiteBusTypeCache typeCache, MessageKind kind)
    {
        foreach (var metadataName in GetMarkerMetadataNames(kind))
        {
            if (typeCache.GetType(metadataName) is not null)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     Determines whether the type symbol is a concrete message type analyzers should inspect.
    /// </summary>
    /// <param name="type">The candidate type symbol.</param>
    /// <returns><see langword="true" /> when the type should be analyzed; otherwise, <see langword="false" />.</returns>
    internal static bool IsAnalyzableMessageType(INamedTypeSymbol type)
    {
        return type.TypeKind is TypeKind.Class or TypeKind.Struct &&
               !type.IsAbstract &&
               !HandlerAnalysis.IsGenericTypeDefinition(type);
    }

    /// <summary>
    ///     Determines whether the type implements one of the supplied marker interfaces.
    /// </summary>
    /// <param name="type">The candidate type symbol.</param>
    /// <param name="typeCache">The per-compilation type cache.</param>
    /// <param name="markerMetadataNames">The marker interface metadata names.</param>
    /// <returns><see langword="true" /> when a marker interface is implemented; otherwise, <see langword="false" />.</returns>
    internal static bool ImplementsAnyMarkerInterface(
        INamedTypeSymbol type,
        LiteBusTypeCache typeCache,
        string[] markerMetadataNames)
    {
        foreach (var metadataName in markerMetadataNames)
        {
            var markerType = typeCache.GetType(metadataName);

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
    /// <param name="typeCache">The per-compilation type cache.</param>
    /// <param name="handlerMetadataNames">The main handler interface metadata names.</param>
    /// <returns><see langword="true" /> when a main handler interface is implemented; otherwise, <see langword="false" />.</returns>
    internal static bool ImplementsAnyMainHandlerInterface(
        INamedTypeSymbol type,
        LiteBusTypeCache typeCache,
        string[] handlerMetadataNames)
    {
        foreach (var metadataName in handlerMetadataNames)
        {
            var handlerType = typeCache.GetType(metadataName);

            if (handlerType is not null && LiteBusSymbols.Implements(type, handlerType))
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
    /// <remarks>
    ///     An arity-2 handler closes as <c>[messageType, resultType]</c>, mirroring the registry, so it covers only
    ///     messages that declare exactly one result type through <c>IProducesResult&lt;TMessageResult&gt;</c>. A void
    ///     message has no result to bind the second parameter to and stays uncovered.
    /// </remarks>
    private static bool OpenGenericHandlerCoversMessageType(
        INamedTypeSymbol openGenericHandler,
        INamedTypeSymbol messageType,
        Compilation compilation)
    {
        if (openGenericHandler.TypeParameters.Length == 1)
        {
            return SatisfiesGenericConstraints(openGenericHandler.TypeParameters[0], messageType, compilation);
        }

        if (openGenericHandler.TypeParameters.Length != 2 ||
            !HandlerAnalysis.BindsBothParametersToATypedContract(openGenericHandler))
        {
            return false;
        }

        if (!TryGetDeclaredResultType(messageType, compilation, out var resultType) || resultType is null)
        {
            return false;
        }

        var parameters = openGenericHandler.TypeParameters;
        ImmutableArray<ITypeSymbol> arguments = [messageType, resultType];

        return SatisfiesBoundConstraints(parameters[0], messageType, parameters, arguments, compilation) &&
               SatisfiesBoundConstraints(parameters[1], resultType, parameters, arguments, compilation);
    }

    /// <summary>
    ///     Gets the result type a message declares through <c>IProducesResult&lt;TMessageResult&gt;</c>.
    /// </summary>
    /// <param name="messageType">The concrete message type.</param>
    /// <param name="compilation">The compilation being analyzed.</param>
    /// <param name="resultType">The declared result type, when the message declares exactly one.</param>
    /// <returns><see langword="true" /> when the message declares exactly one result type; otherwise, <see langword="false" />.</returns>
    /// <remarks>
    ///     A message declaring two result types is a modelling error with no correct arity-2 closing, so it is
    ///     answered as no result, the same silence the registry produces when it skips the handler.
    /// </remarks>
    private static bool TryGetDeclaredResultType(
        INamedTypeSymbol messageType,
        Compilation compilation,
        out ITypeSymbol? resultType)
    {
        resultType = null;

        var producer = compilation.GetTypeByMetadataName("LiteBus.Messaging.Abstractions.IProducesResult`1");

        if (producer is null)
        {
            return false;
        }

        foreach (var candidate in messageType.AllInterfaces)
        {
            if (!SymbolEqualityComparer.Default.Equals(candidate.OriginalDefinition, producer))
            {
                continue;
            }

            if (resultType is not null)
            {
                resultType = null;

                return false;
            }

            resultType = candidate.TypeArguments[0];
        }

        return resultType is not null;
    }

    /// <summary>
    ///     Determines whether a concrete type satisfies the generic parameter constraints after substitution.
    /// </summary>
    /// <param name="typeParameter">The generic type parameter symbol.</param>
    /// <param name="candidateType">The concrete candidate type bound to the parameter.</param>
    /// <param name="parameters">The handler type parameters being closed.</param>
    /// <param name="arguments">The type arguments bound to <paramref name="parameters" />, in order.</param>
    /// <param name="compilation">The compilation being analyzed.</param>
    /// <returns><see langword="true" /> when all constraints are satisfied; otherwise, <see langword="false" />.</returns>
    /// <remarks>
    ///     Constraint types name the handler's own type parameters (for example <c>ICommand&lt;TResult&gt;</c>), so
    ///     they are substituted with the bound arguments before checking, mirroring how the CLR checks substituted
    ///     constraints when the registry closes the handler.
    /// </remarks>
    private static bool SatisfiesBoundConstraints(
        ITypeParameterSymbol typeParameter,
        ITypeSymbol candidateType,
        ImmutableArray<ITypeParameterSymbol> parameters,
        ImmutableArray<ITypeSymbol> arguments,
        Compilation compilation)
    {
        foreach (var constraint in typeParameter.ConstraintTypes)
        {
            var substituted = SubstituteHandlerArguments(constraint, parameters, arguments);

            if (!IsAssignableTo(compilation, candidateType, substituted))
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
            !HasPublicParameterlessConstructor(candidateType))
        {
            return false;
        }

        return true;
    }

    /// <summary>
    ///     Substitutes the bound type arguments into a constraint type that names the handler's type parameters.
    /// </summary>
    /// <param name="type">The constraint type to substitute into.</param>
    /// <param name="parameters">The handler type parameters being closed.</param>
    /// <param name="arguments">The type arguments bound to <paramref name="parameters" />, in order.</param>
    /// <returns>The constraint type with handler parameters replaced by their bound arguments.</returns>
    /// <remarks>
    ///     Only type parameters and constructed generic types are substituted. Any other shape is returned
    ///     unchanged, which keeps the coverage answer conservative for constraint forms the registry would still
    ///     have to close.
    /// </remarks>
    private static ITypeSymbol SubstituteHandlerArguments(
        ITypeSymbol type,
        ImmutableArray<ITypeParameterSymbol> parameters,
        ImmutableArray<ITypeSymbol> arguments)
    {
        for (var index = 0; index < parameters.Length; index++)
        {
            if (SymbolEqualityComparer.Default.Equals(type, parameters[index]))
            {
                return arguments[index];
            }
        }

        if (type is INamedTypeSymbol named &&
            named.IsGenericType &&
            !SymbolEqualityComparer.Default.Equals(named, named.ConstructedFrom))
        {
            var substituted = named.TypeArguments.Select(argument =>
                SubstituteHandlerArguments(argument, parameters, arguments)).ToArray();

            for (var index = 0; index < substituted.Length; index++)
            {
                if (!SymbolEqualityComparer.Default.Equals(substituted[index], named.TypeArguments[index]))
                {
                    return named.ConstructedFrom.Construct(substituted);
                }
            }
        }

        return type;
    }

    /// <summary>
    ///     Determines whether the type exposes an accessible parameterless instance constructor.
    /// </summary>
    /// <param name="candidateType">The candidate type symbol.</param>
    /// <returns><see langword="true" /> when a public parameterless constructor exists; otherwise, <see langword="false" />.</returns>
    private static bool HasPublicParameterlessConstructor(ITypeSymbol candidateType)
    {
        return candidateType is INamedTypeSymbol named &&
               named.InstanceConstructors.Any(constructor =>
                   constructor.Parameters.IsEmpty &&
                   constructor.DeclaredAccessibility == Accessibility.Public);
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
