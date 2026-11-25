namespace LiteBus.Messaging.Registry;

/// <summary>
///     Identifies where a metadata value came from, which decides precedence when two sources declare the same value
///     type for the same message.
/// </summary>
/// <remarks>
///     Ordinals encode precedence: a definition or a catalog source wins over an attribute, an attribute over a
///     default, and a default over a convention. <see cref="MessageMetadata" /> compares them with greater-than when
///     two declarations cover the same message type, so renumbering changes which source wins. A source and a
///     definition for the same message and value type never reach precedence: the registry reports the conflict
///     first, so the higher ordinal only decides against attributes, defaults, and conventions.
/// </remarks>
internal enum MetadataSourceKind
{
    /// <summary>
    ///     The value was derived by a convention function of the message type.
    /// </summary>
    Convention = 0,

    /// <summary>
    ///     The value was stated by a family default covering the message scope.
    /// </summary>
    Default = 1,

    /// <summary>
    ///     The value was declared by an attribute on the message type.
    /// </summary>
    Attribute = 2,

    /// <summary>
    ///     The value was declared by a message definition, which wins over an attribute on the same message type.
    /// </summary>
    Definition = 3,

    /// <summary>
    ///     The value was declared by a multi-message declaration source, which stands where a definition stands.
    /// </summary>
    Source = 4
}
