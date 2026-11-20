using System;
using LiteBus.Auditing.Abstractions;

namespace LiteBus.Auditing.Hashing;

/// <summary>
///     An application-defined record hash scheme: the name stored on each record, the hasher, and the link rule.
/// </summary>
/// <param name="Name">The scheme name stored on each record hashed with it.</param>
/// <param name="Hasher">The hasher that turns framed bytes into the record hash.</param>
/// <param name="LinkRule">
///     The rule that binds a record to its position, or <see langword="null" /> for the framed rule the shipped
///     <c>sha256-v2</c> scheme uses.
/// </param>
/// <remarks>
///     The canonicalizer is not part of the definition: every scheme of a document type reads the document through
///     the <see cref="IAuditCanonicalizer{TDocument}" /> registered for it, so a scheme changes how bytes are hashed
///     and bound, never which fields are covered.
/// </remarks>
public sealed record AuditHashSchemeDefinition(string Name, IAuditHasher Hasher, IAuditLinkRule? LinkRule)
{
    /// <summary>
    ///     Gets a value indicating whether this definition registers the same scheme as another, which makes a repeated
    ///     registration a no-op rather than a conflict.
    /// </summary>
    /// <param name="other">The other definition.</param>
    /// <returns><see langword="true" /> when both name the same hasher and link rule instances.</returns>
    public bool IsSameAs(AuditHashSchemeDefinition other)
    {
        ArgumentNullException.ThrowIfNull(other);

        return string.Equals(Name, other.Name, StringComparison.Ordinal)
               && ReferenceEquals(Hasher, other.Hasher)
               && ReferenceEquals(LinkRule, other.LinkRule);
    }
}
