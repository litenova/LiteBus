namespace LiteBus.Messaging.Registry;

/// <summary>
///     Identifies where a metadata value came from, which decides precedence when two sources declare the same value
///     type for the same message.
/// </summary>
internal enum MetadataSourceKind
{
    /// <summary>
    ///     The value was declared by an attribute on the message type.
    /// </summary>
    Attribute = 0,

    /// <summary>
    ///     The value was declared by a message definition, which wins over an attribute on the same message type.
    /// </summary>
    Definition = 1
}
