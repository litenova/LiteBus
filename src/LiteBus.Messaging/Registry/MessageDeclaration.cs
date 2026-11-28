using System;

namespace LiteBus.Messaging.Registry;

/// <summary>
///     One metadata value contributed by a message definition, a declaration source, or composition code, resolved
///     during registration.
/// </summary>
/// <param name="MessageType">The normalized message type the declaration describes.</param>
/// <param name="KeyType">The metadata key type, which is the declared value type.</param>
/// <param name="Value">The metadata value written to the message descriptor.</param>
/// <param name="DefinitionType">
///     The definition or catalog source type that declared the value, or <see langword="null" /> when composition
///     code declared it directly. Used in diagnostics.
/// </param>
/// <param name="Kind">Where the declaration came from, which decides precedence against other sources.</param>
/// <param name="Replace">
///     <see langword="true" /> when the declaration deliberately overwrites the recorded value for the same message
///     and value type instead of conflicting with it.
/// </param>
internal sealed record MessageDeclaration(
    Type MessageType,
    Type KeyType,
    object Value,
    Type? DefinitionType,
    MetadataSourceKind Kind = MetadataSourceKind.Definition,
    bool Replace = false)
{
    /// <summary>
    ///     Gets the declaration source as it reads in a configuration error.
    /// </summary>
    /// <value>The definition or source type name, or a phrase naming composition code when there is none.</value>
    public string SourceName => DefinitionType?.Name ?? "a composition default";
}
