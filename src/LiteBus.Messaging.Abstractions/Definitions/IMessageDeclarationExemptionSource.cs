namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Marks an attribute as recording that a message deliberately declares nothing for one metadata type.
/// </summary>
/// <remarks>
///     <para>
///         This is the one mechanism for "the message states nothing here, and here is why". Every exemption an
///         attribute records is aggregated into the single <see cref="DeclarationExemptions" /> value on the message,
///         whichever attribute recorded it, so a composition check, a catalogue, or a review reads them all from one
///         place rather than knowing which attribute each feature happens to use.
///     </para>
///     <para>
///         It is separate from <see cref="IMessageDeclarationSource" /> because the two aggregate differently. A
///         declaration maps one attribute to one metadata value and the last one wins; several exemptions have to
///         collapse into one set, so the registry collects them instead of letting them overwrite each other. An
///         attribute may implement both: <see cref="AuditExemptAttribute" /> does, because an audit-exempt message
///         both takes a position on <see cref="AuditDeclaration" /> and records an exemption from declaring one.
///     </para>
/// </remarks>
public interface IMessageDeclarationExemptionSource
{
    /// <summary>
    ///     Creates the exemption this attribute records.
    /// </summary>
    /// <returns>The metadata value type the message is exempt from declaring, and the recorded reason.</returns>
    DeclarationExemption CreateExemption();
}
