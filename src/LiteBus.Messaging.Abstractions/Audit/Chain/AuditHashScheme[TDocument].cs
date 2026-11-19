using System;

namespace LiteBus.Messaging.Abstractions.Auditing;

/// <summary>
///     A named combination of canonical form, link rule and hasher that an audit chain hashes documents with.
/// </summary>
/// <typeparam name="TDocument">The stored audit document type.</typeparam>
/// <remarks>
///     Each document stores the name of the scheme it was written with. Verification picks the scheme per document, so
///     an application can change its algorithm, key or canonical form from a date onward without rewriting the
///     documents already written.
/// </remarks>
public sealed class AuditHashScheme<TDocument>
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="AuditHashScheme{TDocument}" /> class.
    /// </summary>
    /// <param name="name">The stable scheme name stored on every document, such as <c>sha256-v1</c>.</param>
    /// <param name="canonicalizer">The canonical form of a document.</param>
    /// <param name="linkRule">The rule that includes the predecessor in the hashed bytes.</param>
    /// <param name="hasher">The hash algorithm and key.</param>
    public AuditHashScheme(
        string name,
        IAuditCanonicalizer<TDocument> canonicalizer,
        IAuditLinkRule linkRule,
        IAuditHasher hasher)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(canonicalizer);
        ArgumentNullException.ThrowIfNull(linkRule);
        ArgumentNullException.ThrowIfNull(hasher);

        Name = name;
        Canonicalizer = canonicalizer;
        LinkRule = linkRule;
        Hasher = hasher;
    }

    /// <summary>
    ///     Gets the stable scheme name stored on every document.
    /// </summary>
    public string Name { get; }

    /// <summary>
    ///     Gets the canonical form of a document.
    /// </summary>
    public IAuditCanonicalizer<TDocument> Canonicalizer { get; }

    /// <summary>
    ///     Gets the rule that includes the predecessor in the hashed bytes.
    /// </summary>
    public IAuditLinkRule LinkRule { get; }

    /// <summary>
    ///     Gets the hash algorithm and key.
    /// </summary>
    public IAuditHasher Hasher { get; }
}
