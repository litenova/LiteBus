using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Auditing.Engine;
using LiteBus.Messaging.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LiteBus.Auditing.Linking;

/// <summary>
///     Links the audit records one mediation staged after its unit of work commits.
/// </summary>
/// <remarks>
///     <para>
///         Registered as a scoped <see cref="IUnitOfWorkCommitListener" /> when the effective capture stages
///         with the business change for linking after commit. The command and query unit-of-work completion
///         handlers call it after <see cref="IUnitOfWork.CommitAsync" /> returns, and a command batch calls
///         it once after its single commit.
///     </para>
///     <para>
///         A link failure is logged with its segment and left for the link sweep: the data is already
///         committed, so the mediation outcome never changes.
///     </para>
/// </remarks>
internal sealed class AuditLinkCommitListener : IUnitOfWorkCommitListener
{
    /// <summary>
    ///     The holder with the segments staged in this mediation scope.
    /// </summary>
    private readonly AuditStagedSegments _staged;

    /// <summary>
    ///     The linker that assigns chain positions to staged records.
    /// </summary>
    private readonly IAuditLinker _linker;

    /// <summary>
    ///     The logger that records link failures.
    /// </summary>
    private readonly ILogger<AuditLinkCommitListener> _logger;

    /// <summary>
    ///     Initializes a new instance of the <see cref="AuditLinkCommitListener" /> class.
    /// </summary>
    /// <param name="staged">The holder with the segments staged in this mediation scope.</param>
    /// <param name="linker">The linker that assigns chain positions to staged records.</param>
    /// <param name="logger">The logger that records link failures, or <see langword="null" /> to discard them.</param>
    public AuditLinkCommitListener(
        AuditStagedSegments staged,
        IAuditLinker linker,
        ILogger<AuditLinkCommitListener>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(staged);
        ArgumentNullException.ThrowIfNull(linker);

        _staged = staged;
        _linker = linker;
        _logger = logger ?? NullLogger<AuditLinkCommitListener>.Instance;
    }

    /// <inheritdoc />
    [SuppressMessage(
        "Design",
        "CA1031",
        Justification = "The unit of work already committed, so a link failure is logged with its segment and left for the link sweep instead of failing the mediation.")]
    public async Task OnCommittedAsync(CancellationToken cancellationToken = default)
    {
        var segments = _staged.Drain();

        if (segments.Count == 0)
        {
            return;
        }

        foreach (var segment in segments)
        {
            try
            {
                await _linker.LinkPendingAsync(segment, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                AuditingLogMessages.AfterCommitLinkFailed(_logger, segment, exception);
            }
        }
    }
}
