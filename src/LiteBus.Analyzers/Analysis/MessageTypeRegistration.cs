using Microsoft.CodeAnalysis;

namespace LiteBus.Analyzers.Analysis;

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
