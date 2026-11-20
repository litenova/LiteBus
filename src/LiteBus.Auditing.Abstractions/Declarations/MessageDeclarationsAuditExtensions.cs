using System;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Auditing.Abstractions;

/// <summary>
///     Declares a message's audit position on the generic message declaration collection.
/// </summary>
/// <remarks>
///     Audit is a feature layered on mediation, so its declarations are written through the generic
///     <see cref="IMessageDeclarations.Declare{TValue}" /> and <see cref="IMessageDeclarations.Exempt{TValue}" /> members
///     rather than members of the core collection.
/// </remarks>
public static class MessageDeclarationsAuditExtensions
{
    /// <summary>
    ///     Declares that the message produces an audit record.
    /// </summary>
    /// <param name="declarations">The declaration collection of one message.</param>
    /// <param name="action">The use-case identity written to the record, such as <c>orders.place-order</c>.</param>
    /// <param name="category">The category used to group the record for review and retention.</param>
    /// <param name="targetKind">The kind of resource the message acts on, such as <c>order</c>.</param>
    /// <param name="reasonRequired">
    ///     Whether the handler must supply a reason. A mediation that succeeds without one raises
    ///     <see cref="AuditReasonMissingException" /> before the commit, so the work is rolled back rather than recorded
    ///     without its justification.
    /// </param>
    /// <returns>The collection, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="declarations" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException"><paramref name="action" /> is null, empty, or whitespace.</exception>
    public static IMessageDeclarations Audited(
        this IMessageDeclarations declarations,
        string action,
        string? category = null,
        string? targetKind = null,
        bool reasonRequired = false)
    {
        ArgumentNullException.ThrowIfNull(declarations);
        ArgumentException.ThrowIfNullOrWhiteSpace(action);

        return declarations.Declare<AuditDeclaration>(
            AuditDeclaration.Audited(action, category, targetKind, reasonRequired));
    }

    /// <summary>
    ///     Declares that the message is deliberately not audited, and records why.
    /// </summary>
    /// <param name="declarations">The declaration collection of one message.</param>
    /// <param name="rationale">The recorded reason the message produces no audit record.</param>
    /// <returns>The collection, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="declarations" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException"><paramref name="rationale" /> is null, empty, or whitespace.</exception>
    /// <remarks>
    ///     Records both the exempt <see cref="AuditDeclaration" />, which the record writer reads, and an exemption from
    ///     declaring one, so the position satisfies <c>RequireDeclaration&lt;AuditDeclaration&gt;</c>, as
    ///     <see cref="AuditExemptAttribute" /> does.
    /// </remarks>
    public static IMessageDeclarations NotAudited(this IMessageDeclarations declarations, string rationale)
    {
        ArgumentNullException.ThrowIfNull(declarations);
        ArgumentException.ThrowIfNullOrWhiteSpace(rationale);

        return declarations
            .Declare<AuditDeclaration>(AuditDeclaration.Exempt(rationale))
            .Exempt<AuditDeclaration>(rationale);
    }
}
