using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace LiteBus.Analyzers.Analysis;

/// <summary>
///     Describes one discovered LiteBus handler registration candidate.
/// </summary>
internal sealed class HandlerRegistration
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="HandlerRegistration" /> class.
    /// </summary>
    /// <param name="handlerType">The handler type symbol.</param>
    /// <param name="messageType">The handled message type symbol.</param>
    /// <param name="pipeline">The pipeline stage name.</param>
    /// <param name="priority">The handler priority value.</param>
    /// <param name="tags">The handler routing tags.</param>
    /// <param name="location">The diagnostic location.</param>
    internal HandlerRegistration(
        INamedTypeSymbol handlerType,
        ITypeSymbol messageType,
        string pipeline,
        int priority,
        ImmutableArray<string> tags,
        Location location)
    {
        HandlerType = handlerType;
        MessageType = messageType;
        Pipeline = pipeline;
        Priority = priority;
        Tags = tags;
        Location = location;
    }

    /// <summary>
    ///     Gets the handler type symbol.
    /// </summary>
    internal INamedTypeSymbol HandlerType { get; }

    /// <summary>
    ///     Gets the handled message type symbol.
    /// </summary>
    internal ITypeSymbol MessageType { get; }

    /// <summary>
    ///     Gets the pipeline stage name.
    /// </summary>
    internal string Pipeline { get; }

    /// <summary>
    ///     Gets the handler priority value.
    /// </summary>
    internal int Priority { get; }

    /// <summary>
    ///     Gets the handler routing tags.
    /// </summary>
    internal ImmutableArray<string> Tags { get; }

    /// <summary>
    ///     Gets the diagnostic location.
    /// </summary>
    internal Location Location { get; }
}

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
        ("LiteBus.Commands.Abstractions.ICommandPreHandler`1", "command pre-handler"),
        ("LiteBus.Events.Abstractions.IEventPreHandler`1", "event pre-handler"),
        ("LiteBus.Queries.Abstractions.IQueryPreHandler`1", "query pre-handler"),
        ("LiteBus.Commands.Abstractions.ICommandPostHandler`1", "command post-handler"),
        ("LiteBus.Events.Abstractions.IEventPostHandler`1", "event post-handler"),
        ("LiteBus.Queries.Abstractions.IQueryPostHandler`1", "query post-handler"),
        ("LiteBus.Commands.Abstractions.ICommandErrorHandler`1", "command error-handler"),
        ("LiteBus.Events.Abstractions.IEventErrorHandler`1", "event error-handler"),
        ("LiteBus.Queries.Abstractions.IQueryErrorHandler`1", "query error-handler"),
    };

    /// <summary>
    ///     Collects handler registrations declared in the compilation.
    /// </summary>
    /// <param name="compilation">The compilation being analyzed.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>All discovered handler registrations.</returns>
    internal static ImmutableArray<HandlerRegistration> CollectHandlerRegistrations(
        Compilation compilation,
        System.Threading.CancellationToken cancellationToken = default)
    {
        var builder = ImmutableArray.CreateBuilder<HandlerRegistration>();

        foreach (var syntaxTree in compilation.SyntaxTrees)
        {
            foreach (var typeDeclaration in syntaxTree.GetRoot(cancellationToken).DescendantNodes()
                         .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.TypeDeclarationSyntax>())
            {
                var model = compilation.GetSemanticModel(syntaxTree);
                var symbol = model.GetDeclaredSymbol(typeDeclaration, cancellationToken) as INamedTypeSymbol;

                if (symbol is null || symbol.TypeKind == TypeKind.Interface)
                {
                    continue;
                }

                var priority = GetPriority(symbol);
                var tags = GetTags(symbol);
                var location = symbol.Locations.FirstOrDefault() ?? Location.None;

                foreach (var handlerInterface in symbol.AllInterfaces)
                {
                    var pipeline = GetPipeline(compilation, handlerInterface);

                    if (pipeline is null)
                    {
                        continue;
                    }

                    var messageType = handlerInterface.TypeArguments[0];
                    builder.Add(new HandlerRegistration(symbol, messageType, pipeline, priority, tags, location));
                }
            }
        }

        return builder.ToImmutable();
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
    ///     Determines whether two handler tag sets overlap for routing purposes.
    /// </summary>
    /// <param name="left">The first tag set.</param>
    /// <param name="right">The second tag set.</param>
    /// <returns><see langword="true" /> when routing overlaps; otherwise, <see langword="false" />.</returns>
    internal static bool TagsOverlap(ImmutableArray<string> left, ImmutableArray<string> right)
    {
        if (left.IsEmpty && right.IsEmpty)
        {
            return true;
        }

        if (left.IsEmpty || right.IsEmpty)
        {
            return false;
        }

        return left.Intersect(right, StringComparer.Ordinal).Any();
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
    ///     Gets the priority declared on the handler type, defaulting to zero.
    /// </summary>
    /// <param name="handlerType">The handler type symbol.</param>
    /// <returns>The declared priority value.</returns>
    internal static int GetPriority(INamedTypeSymbol handlerType)
    {
        foreach (var attribute in handlerType.GetAttributes())
        {
            if (attribute.AttributeClass?.Name != "HandlerPriorityAttribute" ||
                attribute.AttributeClass.ContainingNamespace?.ToDisplayString() != "LiteBus.Messaging.Abstractions")
            {
                continue;
            }

            if (attribute.ConstructorArguments.Length > 0 &&
                attribute.ConstructorArguments[0].Value is int priority)
            {
                return priority;
            }
        }

        return 0;
    }

    /// <summary>
    ///     Gets routing tags declared on the handler type.
    /// </summary>
    /// <param name="handlerType">The handler type symbol.</param>
    /// <returns>The declared routing tags.</returns>
    internal static ImmutableArray<string> GetTags(INamedTypeSymbol handlerType)
    {
        var tags = new HashSet<string>(StringComparer.Ordinal);

        foreach (var attribute in handlerType.GetAttributes())
        {
            if (attribute.AttributeClass is null ||
                attribute.AttributeClass.ContainingNamespace?.ToDisplayString() != "LiteBus.Messaging.Abstractions")
            {
                continue;
            }

            if (attribute.AttributeClass.Name == "HandlerTagAttribute" &&
                attribute.ConstructorArguments.Length > 0 &&
                attribute.ConstructorArguments[0].Value is string singleTag)
            {
                tags.Add(singleTag);
            }

            if (attribute.AttributeClass.Name == "HandlerTagsAttribute" &&
                attribute.ConstructorArguments.Length > 0 &&
                attribute.ConstructorArguments[0].Values is { Length: > 0 } values)
            {
                foreach (var value in values)
                {
                    if (value.Value is string tag)
                    {
                        tags.Add(tag);
                    }
                }
            }
        }

        return tags.OrderBy(tag => tag, StringComparer.Ordinal).ToImmutableArray();
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
