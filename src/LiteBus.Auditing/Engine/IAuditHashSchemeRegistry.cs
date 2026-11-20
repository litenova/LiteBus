using LiteBus.Auditing.Abstractions;

namespace LiteBus.Auditing.Engine;

/// <summary>
///     Resolves the hash scheme new records are written with and older records are verified with, by scheme name.
/// </summary>
internal interface IAuditHashSchemeRegistry
{
    /// <summary>
    ///     Resolves the named hash scheme for a document type.
    /// </summary>
    /// <typeparam name="TDocument">The stored audit document type.</typeparam>
    /// <param name="schemeName">The stable scheme name documents store.</param>
    /// <returns>The scheme: the canonical form, link rule, and hasher the name selects.</returns>
    AuditHashScheme<TDocument> Resolve<TDocument>(string schemeName)
        where TDocument : class, IAuditChainDocument;
}
