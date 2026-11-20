using System;
using System.Collections.Generic;
using System.Linq;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Auditing.Abstractions;

/// <summary>
///     Reads audit declarations from the message catalog a composition check receives.
/// </summary>
public static class MessageCatalogAuditExtensions
{
    /// <summary>
    ///     Gets the audit declaration a message states, when it states one.
    /// </summary>
    /// <param name="entry">The catalog entry of one message.</param>
    /// <returns>
    ///     The audited declaration, or <see langword="null" /> when the message declares no audit position or declares
    ///     that it is exempt. An exempt message is deliberately absent, because a catalogue of audited actions is what
    ///     this exists to build.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="entry" /> is <see langword="null" />.</exception>
    public static AuditedDeclaration? GetAudit(this MessageCatalogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return entry.Metadata.TryGet<AuditDeclaration>(out var declaration) ? declaration as AuditedDeclaration : null;
    }

    /// <summary>
    ///     Gets every audited message and the action it declares.
    /// </summary>
    /// <param name="catalog">The message catalog to read.</param>
    /// <returns>One entry per message declaring an <see cref="AuditedDeclaration" />.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="catalog" /> is <see langword="null" />.</exception>
    /// <remarks>
    ///     The audit catalogue is the compliance artifact teams most often maintain by hand and keep wrong, and it is a
    ///     pure function of the declarations. This is the traversal to build it from.
    /// </remarks>
    public static IEnumerable<MessageCatalogEntry> Audited(this IMessageCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        return catalog.Where(static entry => entry.GetAudit() is not null);
    }
}
