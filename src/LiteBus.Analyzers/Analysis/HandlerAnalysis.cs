using System;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace LiteBus.Analyzers.Analysis;

/// <summary>
///     Shared handler discovery helpers.
/// </summary>
internal static class HandlerAnalysis
{
    private static readonly (string MetadataName, string Pipeline)[] HandlerInterfaceMap =
    [
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
        ("LiteBus.Queries.Abstractions.IQueryErrorHandler`2", "query error-handler"),
        ("LiteBus.Commands.Abstractions.ICommandPostHandler`2", "command post-handler"),
        ("LiteBus.Queries.Abstractions.IQueryPostHandler`2", "query post-handler"),
        ("LiteBus.Queries.Abstractions.IStreamQueryPostHandler`2", "stream query post-handler"),
        ("LiteBus.Commands.Abstractions.ICommandGuard`1", "command guard"),
        ("LiteBus.Queries.Abstractions.IQueryGuard`1", "query guard"),
        ("LiteBus.Events.Abstractions.IEventGuard`1", "event guard"),
        ("LiteBus.Commands.Abstractions.ICommandValidator`1", "command validator"),
        ("LiteBus.Queries.Abstractions.IQueryValidator`1", "query validator"),
        ("LiteBus.Events.Abstractions.IEventValidator`1", "event validator"),
        ("LiteBus.Commands.Abstractions.ICommandRefusalMapper`2", "command refusal-mapper"),
        ("LiteBus.Queries.Abstractions.IQueryRefusalMapper`2", "query refusal-mapper"),
        ("LiteBus.Queries.Abstractions.IStreamQueryRefusalMapper`2", "stream query refusal-mapper"),
        ("LiteBus.Commands.Abstractions.ICommandShortcut`1", "command shortcut"),
        ("LiteBus.Commands.Abstractions.ICommandShortcut`2", "command shortcut"),
        ("LiteBus.Queries.Abstractions.IQueryShortcut`2", "query shortcut"),
        ("LiteBus.Queries.Abstractions.IStreamQueryShortcut`2", "stream query shortcut"),
        ("LiteBus.Events.Abstractions.IEventShortcut`1", "event shortcut"),
        ("LiteBus.Commands.Abstractions.ICommandCompletionHandler`1", "command completion-handler"),
        ("LiteBus.Commands.Abstractions.ICommandCompletionHandler`2", "command completion-handler"),
        ("LiteBus.Queries.Abstractions.IQueryCompletionHandler`1", "query completion-handler"),
        ("LiteBus.Queries.Abstractions.IQueryCompletionHandler`2", "query completion-handler"),
        ("LiteBus.Events.Abstractions.IEventCompletionHandler`1", "event completion-handler")
    ];

    /// <summary>
    ///     Gets the number of distinct handler interface metadata names in the handler interface map.
    /// </summary>
    internal static int DistinctHandlerInterfaceMetadataNameCount =>
        HandlerInterfaceMap.Select(entry => entry.MetadataName).Distinct(StringComparer.Ordinal).Count();

    /// <summary>
    ///     Determines whether any handler interface in the handler interface map resolves in the compilation.
    /// </summary>
    /// <param name="typeCache">The per-compilation type cache.</param>
    /// <returns><see langword="true" /> when at least one handler interface resolves; otherwise, <see langword="false" />.</returns>
    internal static bool AnyHandlerInterfaceResolves(LiteBusTypeCache typeCache)
    {
        foreach (var entry in HandlerInterfaceMap)
        {
            if (typeCache.GetType(entry.MetadataName) is not null)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     Determines whether any handler interface for the pipeline stage resolves in the compilation.
    /// </summary>
    /// <param name="typeCache">The per-compilation type cache.</param>
    /// <param name="pipeline">The pipeline stage name.</param>
    /// <returns><see langword="true" /> when at least one stage interface resolves; otherwise, <see langword="false" />.</returns>
    internal static bool AnyPipelineInterfaceResolves(LiteBusTypeCache typeCache, string pipeline)
    {
        foreach (var entry in HandlerInterfaceMap)
        {
            if (!string.Equals(entry.Pipeline, pipeline, StringComparison.Ordinal))
            {
                continue;
            }

            if (typeCache.GetType(entry.MetadataName) is not null)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     Determines whether handler registrations should be collected from a referenced assembly.
    /// </summary>
    /// <param name="assemblySymbol">The referenced assembly symbol.</param>
    /// <param name="compilationAssembly">The assembly under analysis.</param>
    /// <returns><see langword="true" /> when the referenced assembly may contain application handlers.</returns>
    internal static bool ShouldScanReferencedAssembly(IAssemblySymbol assemblySymbol, IAssemblySymbol compilationAssembly)
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
    /// <param name="typeCache">The per-compilation type cache.</param>
    /// <param name="handlerInterface">The implemented handler interface symbol.</param>
    /// <returns>The pipeline stage name, if matched.</returns>
    internal static string? GetPipeline(LiteBusTypeCache typeCache, INamedTypeSymbol handlerInterface)
    {
        var original = handlerInterface.OriginalDefinition;

        foreach (var entry in HandlerInterfaceMap)
        {
            var interfaceType = typeCache.GetType(entry.MetadataName);

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
        return IsHandlerType(handlerType, new LiteBusTypeCache(compilation));
    }

    /// <summary>
    ///     Determines whether the type implements any LiteBus handler interface, sharing one type cache for the pass.
    /// </summary>
    /// <param name="handlerType">The candidate handler type symbol.</param>
    /// <param name="typeCache">The per-compilation type cache.</param>
    /// <returns><see langword="true" /> when the type implements a handler interface.</returns>
    internal static bool IsHandlerType(INamedTypeSymbol handlerType, LiteBusTypeCache typeCache)
    {
        foreach (var handlerInterface in handlerType.AllInterfaces)
        {
            if (GetPipeline(typeCache, handlerInterface) is not null)
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

        return openDefinition.TypeParameters.Length switch
        {
            1 => false,

            // Two parameters are closable only when the handler implements a contract taking both, in which case the
            // second binds to the result type the message declares. A second parameter the handler invented, a
            // TContext or a TStore, has nothing to bind to.
            2 => !BindsBothParametersToATypedContract(openDefinition),
            _ => true
        };
    }

    /// <summary>
    ///     Determines whether an arity-2 handler implements a contract taking both of its type parameters, in order.
    /// </summary>
    /// <param name="openDefinition">The open generic handler type definition.</param>
    /// <returns><see langword="true" /> when the second parameter binds to a result type rather than to nothing.</returns>
    /// <remarks>
    ///     The interface list is the transitive closure, so matching a two-argument handler contract covers both the
    ///     messaging-level shape and the axis shape that derives from it. The name is not checked against a list: any
    ///     handler interface taking the handler's two parameters in order is closable, because the registry closes it by
    ///     position.
    /// </remarks>
    private static bool BindsBothParametersToATypedContract(INamedTypeSymbol openDefinition)
    {
        var parameters = openDefinition.TypeParameters;

        foreach (var handlerInterface in openDefinition.AllInterfaces)
        {
            if (handlerInterface.TypeArguments.Length != 2)
            {
                continue;
            }

            if (SymbolEqualityComparer.Default.Equals(handlerInterface.TypeArguments[0], parameters[0]) &&
                SymbolEqualityComparer.Default.Equals(handlerInterface.TypeArguments[1], parameters[1]))
            {
                return true;
            }
        }

        return false;
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
