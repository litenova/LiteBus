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
    /// <param name="location">The diagnostic location.</param>
    internal HandlerRegistration(
        INamedTypeSymbol handlerType,
        ITypeSymbol messageType,
        string pipeline,
        Location location)
    {
        HandlerType = handlerType;
        MessageType = messageType;
        Pipeline = pipeline;
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
    ///     Gets the diagnostic location.
    /// </summary>
    internal Location Location { get; }
}