using System;

namespace LiteBus.Messaging.Registry;

/// <summary>
///     One metadata value contributed by a message definition, resolved during registration.
/// </summary>
/// <param name="MessageType">The normalized message type the declaration describes.</param>
/// <param name="KeyType">The metadata key type, which is the declared value type.</param>
/// <param name="Value">The metadata value written to the message descriptor.</param>
/// <param name="DefinitionType">
///     The definition type that declared the value, or <see langword="null" /> when composition code declared it
///     directly. Used in diagnostics.
/// </param>
internal sealed record MessageDeclaration(Type MessageType, Type KeyType, object Value, Type? DefinitionType)
{
    /// <summary>
    ///     Gets the declaration source as it reads in a configuration error.
    /// </summary>
    /// <value>The definition type name, or a phrase naming composition code when there is no definition.</value>
    public string SourceName => DefinitionType?.Name ?? "a composition default";
}
