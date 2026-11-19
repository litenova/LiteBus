using System.Threading;
using LiteBus.Auditing.Abstractions;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions.Auditing;

namespace LiteBus.Auditing.Engine;

/// <summary>
///     Verifies one audit chain segment against its checkpoints.
/// </summary>
internal interface IAuditVerifier
{
    /// <summary>
    ///     Verifies one segment, streaming records by pages and comparing against every configured source.
    /// </summary>
    /// <param name="request">The segment and the thresholds to verify it with.</param>
    /// <param name="cancellationToken">The token used to cancel the verification.</param>
    /// <returns>Every break found, the head, and one comparison per source.</returns>
    ValueTask<AuditChainVerification> VerifyAsync(
        AuditChainVerificationRequest request,
        CancellationToken cancellationToken);
}
