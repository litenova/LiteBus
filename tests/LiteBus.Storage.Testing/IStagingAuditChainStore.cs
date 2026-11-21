using System.Threading;
using System.Threading.Tasks;
using LiteBus.Auditing.Abstractions;

namespace LiteBus.Storage.Testing;

/// <summary>
///     An audit chain store under contract test, together with the staging its provider performs.
/// </summary>
/// <typeparam name="TDocument">The stored audit document type.</typeparam>
/// <remarks>
///     Staging is not part of <see cref="IAuditChainStore{TDocument}" />: each store's audit trail stages on the
///     business session of its mediation. The contract tests still need staged documents, so each provider's
///     fixture stages the way its trail does and commits, through this interface.
/// </remarks>
public interface IStagingAuditChainStore<TDocument> : IAuditChainStore<TDocument>
    where TDocument : class, IAuditChainDocument
{
    /// <summary>
    ///     Stages an unlinked document and commits it, the way the provider's audit trail stages a successful
    ///     mediation's record.
    /// </summary>
    /// <param name="segment">The segment the document belongs to.</param>
    /// <param name="document">The document without a chain position.</param>
    /// <param name="cancellationToken">The token used to cancel the staging.</param>
    /// <returns>A task representing the asynchronous staging.</returns>
    ValueTask StageAsync(string segment, TDocument document, CancellationToken cancellationToken);
}
