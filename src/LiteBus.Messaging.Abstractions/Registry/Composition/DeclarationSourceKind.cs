namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Identifies which declaration source supplied an effective value.
/// </summary>
/// <remarks>
///     <para>
///         Every effective declaration value carries one of these, so a composition check and the exported snapshot can
///         report where each value came from rather than only what it is. Precedence between sources is unchanged:
///         a definition or a catalog source wins over an attribute, an attribute over a default, and a convention
///         runs last, with exemptions respected throughout.
///     </para>
/// </remarks>
public enum DeclarationSourceKind
{
    /// <summary>
    ///     The value was stated by a declaring attribute on the message.
    /// </summary>
    Attribute = 0,

    /// <summary>
    ///     The value was stated by a definition class beside the message.
    /// </summary>
    Definition = 1,

    /// <summary>
    ///     The value was stated by a family default covering the message scope.
    /// </summary>
    Default = 2,

    /// <summary>
    ///     The value was derived by a convention function of the message type.
    /// </summary>
    Convention = 3,

    /// <summary>
    ///     The value is a recorded exemption carrying a rationale.
    /// </summary>
    Exemption = 4,

    /// <summary>
    ///     The value was stated by a multi-message declaration source covering the message.
    /// </summary>
    Source = 5,
}
