using System;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     One registered message and the metadata it declares.
/// </summary>
/// <param name="MessageType">The registered message type.</param>
/// <param name="Metadata">Every declaration resolved for the message, keyed by value type.</param>
/// <remarks>
///     The pair a composition check reads. It carries the resolved metadata rather than the declaration sources, so a
///     check sees what the pipeline will see, including a value inherited from a base type or a marker interface.
/// </remarks>
public sealed record MessageCatalogEntry(Type MessageType, IMessageMetadata Metadata)
{
    /// <summary>
    ///     Gets the audit declaration the message states, when it states one.
    /// </summary>
    /// <value>
    ///     The audited declaration, or <see langword="null" /> when the message declares no audit position or declares
    ///     that it is exempt. An exempt message is deliberately absent, because a catalogue of audited actions is what
    ///     this exists to build.
    /// </value>
    public AuditedDeclaration? Audit =>
        Metadata.TryGet<AuditDeclaration>(out var declaration) ? declaration as AuditedDeclaration : null;
}
