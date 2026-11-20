using System;
using LiteBus.Auditing.Abstractions;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Auditing.Engine;
using LiteBus.Runtime.Abstractions;
using LiteBus.Runtime.Abstractions.Exceptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LiteBus.Auditing.Checkpoints;

/// <summary>
///     Cuts signed checkpoints, publishes pending ones, and cuts the checkpoints the policy reports due.
/// </summary>
/// <typeparam name="TDocument">The stored audit document type.</typeparam>
/// <remarks>
///     <para>
///         A cut builds a checkpoint from the current head, signs it with every configured
///         <see cref="ICheckpointSigner" />, stores it as <see cref="CheckpointPublishState.Pending" />, and advances
///         the head past the covered sequence. Publication never runs here: <see cref="PublishPendingAsync" />
///         publishes from the background retrier, so a publisher failure is recorded on the checkpoint and retried
///         instead of failing the cut or the append that triggered it.
///     </para>
///     <para>
///         The conflict retry loop covers the read, sign, and store only. A concurrent writer that moves the head
///         between the read and the head advance fails the advance with <see cref="AuditHeadConflictException" />;
///         the stored checkpoint remains as a valid prefix anchor and the next pass cuts from the newer head.
///         Publishers run outside that loop, so a publisher that throws a storage exception never spins the cut.
///     </para>
/// </remarks>
internal sealed class CheckpointLifecycle<TDocument> : ICheckpointLifecycle
    where TDocument : class, IAuditChainDocument
{
    /// <summary>
    ///     The store heads, documents, and checkpoints are read from and written to. Held for the life of the lifecycle.
    /// </summary>
    private readonly IAuditChainStore<TDocument> _store;

    /// <summary>
    ///     The chain configuration naming the policy, signers, and publishers. Held for the life of the lifecycle.
    /// </summary>
    private readonly AuditHashChainOptions _chainOptions;

    /// <summary>
    ///     Resolves pieces the builder named as types, or <see langword="null" /> when every piece was given as an
    ///     instance. Held for the life of the lifecycle.
    /// </summary>
    private readonly IServiceProvider? _serviceProvider;

    /// <summary>
    ///     The clock that stamps cut checkpoints and policy evaluations. Held for the life of the lifecycle.
    /// </summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>
    ///     The logger publish and cut failures are reported to. Held for the life of the lifecycle.
    /// </summary>
    private readonly ILogger<CheckpointLifecycle<TDocument>> _logger;

    /// <summary>
    ///     Initializes a new instance of the <see cref="CheckpointLifecycle{TDocument}" /> class.
    /// </summary>
    /// <param name="store">The store heads, documents, and checkpoints are read from and written to.</param>
    /// <param name="chainOptions">The chain configuration naming the policy, signers, and publishers.</param>
    /// <param name="serviceProvider">
    ///     Resolves pieces the builder named as types, or <see langword="null" /> when every piece was given as an
    ///     instance.
    /// </param>
    /// <param name="timeProvider">The clock that stamps cut checkpoints, or <see langword="null" /> for the system clock.</param>
    /// <param name="logger">The logger publish and cut failures are reported to, or <see langword="null" /> for none.</param>
    public CheckpointLifecycle(
        IAuditChainStore<TDocument> store,
        AuditHashChainOptions chainOptions,
        IServiceProvider? serviceProvider = null,
        TimeProvider? timeProvider = null,
        ILogger<CheckpointLifecycle<TDocument>>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(chainOptions);

        _store = store;
        _chainOptions = chainOptions;
        _serviceProvider = serviceProvider;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = logger ?? NullLogger<CheckpointLifecycle<TDocument>>.Instance;
    }

    /// <inheritdoc />
    public async ValueTask<AuditCheckpoint> CutAsync(
        string segment,
        string? label,
        AuditCheckpointKind kind,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(segment);

        if (label is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(label);

            if (label.Contains('\r') || label.Contains('\n'))
            {
                throw new ArgumentException("The checkpoint label must not contain a line break.", nameof(label));
            }
        }

        var signers = ResolveSigners();

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var head = await _store.ReadHeadAsync(segment, cancellationToken).ConfigureAwait(false);

            if (head is null || head.LastSequence == 0)
            {
                throw new InvalidOperationException(
                    $"The audit segment '{segment}' holds no records, so no checkpoint can be cut.");
            }

            if (head.Expired)
            {
                throw new InvalidOperationException(
                    $"The audit segment '{segment}' has expired, so no checkpoint can be cut.");
            }

            if (string.IsNullOrWhiteSpace(head.LastHash))
            {
                throw new InvalidOperationException(
                    $"The audit segment '{segment}' holds records but no head hash, so no checkpoint can be cut.");
            }

            var schemeName = await ReadCoveredSchemeAsync(segment, head, cancellationToken).ConfigureAwait(false);
            var unsigned = new AuditCheckpoint(
                kind == AuditCheckpointKind.Final ? FinalCheckpointId.For(segment) : Guid.NewGuid(),
                segment,
                head.LastSequence,
                head.Count,
                head.LastHash,
                schemeName,
                kind,
                _timeProvider.GetUtcNow())
            {
                Label = label,
                PublishState = CheckpointPublishState.Pending,
            };

            List<AuditCheckpointSignature> signatures = [];

            foreach (var signer in signers)
            {
                var signature = await signer.SignAsync(unsigned, cancellationToken).ConfigureAwait(false);

                signatures.Add(signature);
            }

            var checkpoint = unsigned with
            {
                Signatures = signatures,
                Signature = signatures.Count == 0 ? null : signatures[0],
            };

            await _store.StoreCheckpointAsync(checkpoint, cancellationToken).ConfigureAwait(false);

            var updated = head with
            {
                LastCheckpointSequence = checkpoint.Sequence,
                LastCheckpointAt = checkpoint.CreatedAt,
                Version = head.Version + 1,
            };

            try
            {
                await _store
                    .AppendAsync(segment, head, [], updated, cancellationToken)
                    .ConfigureAwait(false);

                return checkpoint;
            }
            catch (AuditHeadConflictException)
            {
                AuditingLogMessages.CheckpointCutRetried(_logger, segment, checkpoint.Id);
            }
        }
    }

    /// <inheritdoc />
    public async ValueTask<int> PublishPendingAsync(CancellationToken cancellationToken)
    {
        var publishers = ResolvePublishers();
        var published = 0;

        await foreach (var checkpoint in _store.ListUnpublishedAsync(cancellationToken).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (publishers.Count == 0)
            {
                await _store.MarkPublishedAsync(checkpoint.Id, null, cancellationToken).ConfigureAwait(false);
                published++;
                continue;
            }

#pragma warning disable CA1031 // A publisher is an external anchor call that can fail in any way; the error is recorded on the checkpoint and the pass continues with the next one.
            try
            {
                foreach (var publisher in publishers)
                {
                    await publisher.PublishAsync(checkpoint, cancellationToken).ConfigureAwait(false);
                }

                await _store.MarkPublishedAsync(checkpoint.Id, null, cancellationToken).ConfigureAwait(false);
                published++;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                await _store
                    .MarkPublishedAsync(checkpoint.Id, exception.Message, cancellationToken)
                    .ConfigureAwait(false);

                AuditingLogMessages.CheckpointPublishFailed(
                    _logger,
                    checkpoint.Id,
                    checkpoint.Segment,
                    checkpoint.Sequence,
                    exception);
            }
#pragma warning restore CA1031
        }

        return published;
    }

    /// <inheritdoc />
    public async ValueTask CutDueAsync(CancellationToken cancellationToken)
    {
        var policy = ResolvePolicy();

        // A policy reads ports such as the tenant metadata provider when it decides, so each pass runs in its own
        // dispatch scope and hands those services to the policy through the candidate.
        using var scope = _serviceProvider?.GetService(typeof(IMessageDispatchScopeFactory)) is IMessageDispatchScopeFactory factory
            ? factory.CreateScope()
            : null;
        var services = scope?.ServiceProvider ?? _serviceProvider;
        var now = _timeProvider.GetUtcNow();
        var policyLabel = (policy as ILabeledCheckpointPolicy)?.Label;

        await foreach (var head in _store.ListHeadsAsync(new AuditSegmentFilter(), cancellationToken).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (head.Expired || head.Count <= 0)
            {
                continue;
            }

            var candidate = new AuditCheckpointCandidate(
                head.Info.Key,
                head.LastSequence,
                head.Count,
                head.LastCheckpointSequence,
                head.LastCheckpointAt,
                now,
                head.Info,
                services);

#pragma warning disable CA1031 // One segment whose policy evaluation or cut fails must not skip the remaining segments; the error is logged with the segment and the scan continues.
            try
            {
                if (await policy.IsDueAsync(candidate, cancellationToken).ConfigureAwait(false))
                {
                    await CutAsync(head.Info.Key, policyLabel, AuditCheckpointKind.Regular, cancellationToken)
                        .ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                AuditingLogMessages.DueCheckpointCutFailed(_logger, head.Info.Key, exception);
            }
#pragma warning restore CA1031
        }
    }

    /// <summary>
    ///     Reads the hash scheme the covered document was written with, falling back to the configured write scheme.
    /// </summary>
    /// <param name="segment">The segment the checkpoint covers.</param>
    /// <param name="head">The head the checkpoint covers.</param>
    /// <param name="cancellationToken">The token used to cancel the read.</param>
    /// <returns>The scheme name stored on the checkpoint.</returns>
    /// <remarks>
    ///     The checkpoint carries the scheme of the document it covers, not the scheme new records are written with,
    ///     so a segment that changed its algorithm partway through still anchors each prefix with the right name.
    /// </remarks>
    private async ValueTask<string> ReadCoveredSchemeAsync(
        string segment,
        AuditSegmentHead head,
        CancellationToken cancellationToken)
    {
        await foreach (var document in _store
            .ReadSegmentAsync(segment, head.LastSequence, head.LastSequence, cancellationToken)
            .ConfigureAwait(false))
        {
            if (!string.IsNullOrWhiteSpace(document?.HashScheme))
            {
                return document.HashScheme!;
            }
        }

        return _chainOptions.SchemeName;
    }

    /// <summary>
    ///     Resolves the checkpoint policy from the configured instance or type, or the record-count default.
    /// </summary>
    /// <returns>The policy the chain evaluates.</returns>
    private ICheckpointPolicy ResolvePolicy()
    {
        if (_chainOptions.CheckpointPolicy is not null)
        {
            return _chainOptions.CheckpointPolicy;
        }

        if (_chainOptions.CheckpointPolicyType is null)
        {
            return CheckpointPolicies.EveryRecords(100);
        }

        return ResolveService<ICheckpointPolicy>(
            _chainOptions.CheckpointPolicyType,
            "checkpoint policy");
    }

    /// <summary>
    ///     Resolves every configured checkpoint signer in registration order.
    /// </summary>
    /// <returns>The signers that sign every cut checkpoint.</returns>
    private List<ICheckpointSigner> ResolveSigners()
    {
        if (_chainOptions.SignerTypes.Count == 0)
        {
            return [];
        }

        List<ICheckpointSigner> signers = new(_chainOptions.SignerTypes.Count);

        foreach (var signerType in _chainOptions.SignerTypes)
        {
            signers.Add(ResolveService<ICheckpointSigner>(signerType, "checkpoint signer"));
        }

        return signers;
    }

    /// <summary>
    ///     Resolves every configured checkpoint publisher in registration order.
    /// </summary>
    /// <returns>The publishers that receive every cut checkpoint.</returns>
    private List<ICheckpointPublisher> ResolvePublishers()
    {
        if (_chainOptions.PublisherTypes.Count == 0)
        {
            return [];
        }

        List<ICheckpointPublisher> publishers = new(_chainOptions.PublisherTypes.Count);

        foreach (var publisherType in _chainOptions.PublisherTypes)
        {
            publishers.Add(ResolveService<ICheckpointPublisher>(publisherType, "checkpoint publisher"));
        }

        return publishers;
    }

    /// <summary>
    ///     Resolves a chain piece the builder named as a type from the service provider.
    /// </summary>
    /// <typeparam name="TService">The piece contract.</typeparam>
    /// <param name="implementationType">The implementation type the builder named.</param>
    /// <param name="pieceName">The piece name used in the error when no provider is available.</param>
    /// <returns>The resolved piece.</returns>
    private TService ResolveService<TService>(Type implementationType, string pieceName)
        where TService : class
    {
        if (_serviceProvider is null)
        {
            throw new InvalidOperationException(
                $"The {pieceName} was registered as the type '{implementationType.FullName}' but no service provider " +
                "is available. Register the piece as an instance or pass an IServiceProvider to the chain.");
        }

        var service = _serviceProvider.GetService(implementationType);

        if (service is null)
        {
            throw new LiteBusDependencyResolutionException(implementationType);
        }

        return (TService) service;
    }
}
