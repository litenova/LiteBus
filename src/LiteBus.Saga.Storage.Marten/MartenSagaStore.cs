using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions;
using LiteBus.Saga.Abstractions;
using JasperFx;
using Marten;
using Marten.Exceptions;

namespace LiteBus.Saga.Storage.Marten;

/// <summary>
///     Marten-backed saga store with optimistic concurrency on the loaded document version.
/// </summary>
/// <remarks>
///     <para>
///         Every call opens its own short session from <see cref="IDocumentStore" /> and commits it before
///         returning, so one singleton instance serves every mediation. The document identity is the escaped
///         composite of tenant, saga definition, and correlation, so the tenant participates in the key exactly
///         as it does in the relational saga stores.
///     </para>
///     <para>
///         A new row is inserted with <c>Insert</c>, which fails when the identity already exists, and an existing
///         row is loaded and updated in place with <c>Update</c>, which fails when the row was removed. The
///         document carries <c>UseOptimisticConcurrency</c>, so a concurrent writer that commits first makes the
///         loser fail its commit. Each of those Marten failures is mapped to <see cref="SagaConcurrencyException" />,
///         and a stale expected version or a completed row is rejected before anything is stored.
///     </para>
/// </remarks>
public sealed class MartenSagaStore : ISagaStore
{
    /// <summary>
    ///     The time provider used to stamp create and update timestamps.
    /// </summary>
    private readonly TimeProvider _clock;

    /// <summary>
    ///     The Marten document store each call opens its short session from.
    /// </summary>
    private readonly IDocumentStore _documentStore;

    /// <summary>
    ///     The serializer used to convert saga state objects to JSON.
    /// </summary>
    private readonly IMessageSerializer _serializer;

    /// <summary>
    ///     Initializes a new instance of the <see cref="MartenSagaStore" /> class.
    /// </summary>
    /// <param name="documentStore">The application-owned Marten document store.</param>
    /// <param name="serializer">The serializer used to convert saga state objects to JSON.</param>
    /// <param name="options">The saga store options.</param>
    /// <param name="clock">The time provider used to stamp create and update timestamps.</param>
    public MartenSagaStore(
        IDocumentStore documentStore,
        IMessageSerializer serializer,
        MartenSagaStoreOptions? options = null,
        TimeProvider? clock = null)
    {
        ArgumentNullException.ThrowIfNull(documentStore);
        ArgumentNullException.ThrowIfNull(serializer);

        // The options carry no settings yet; the parameter exists so later settings plug in without
        // changing the registration shape.
        _ = options;
        _documentStore = documentStore;
        _serializer = serializer;
        _clock = clock ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public async Task<SagaInstance<TState>?> LoadAsync<TState>(
        SagaCorrelation correlation,
        CancellationToken cancellationToken = default)
        where TState : class, new()
    {
        ArgumentNullException.ThrowIfNull(correlation);

        var session = _documentStore.QuerySession();

        await using (session.ConfigureAwait(false))
        {
            var document = await session
                .LoadAsync<MartenSagaDocument>(BuildDocumentId(correlation), cancellationToken)
                .ConfigureAwait(false);

            if (document is null)
            {
                return null;
            }

            var state = await _serializer
                .DeserializeAsync(typeof(TState), document.StateJson, cancellationToken)
                .ConfigureAwait(false);

            if (state is not TState typedState)
            {
                throw new MessageSerializationException(
                    typeof(TState),
                    "deserialized",
                    new InvalidCastException(
                        $"Saga state payload deserialized to '{state?.GetType().FullName ?? "null"}' but '{typeof(TState).FullName}' was requested."));
            }

            return new SagaInstance<TState>
            {
                Correlation = correlation,
                State = typedState,
                Version = document.Version,
                IsCompleted = document.IsCompleted,
                LastAppliedMessageId = document.LastAppliedMessageId
            };
        }
    }

    /// <inheritdoc />
    public async Task SaveAsync<TState>(
        SagaSaveItem<TState> item,
        CancellationToken cancellationToken = default)
        where TState : class, new()
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(item.State);
        ArgumentOutOfRangeException.ThrowIfNegative(item.ExpectedVersion);

        var now = _clock.GetUtcNow();
        var stateJson = await _serializer.SerializeAsync(item.State, cancellationToken).ConfigureAwait(false);
        var session = _documentStore.LightweightSession(IsolationLevel.ReadCommitted);

        await using (session.ConfigureAwait(false))
        {
            var identity = BuildDocumentId(item.Correlation);

            if (item.ExpectedVersion == 0)
            {
                var existing = await session
                    .LoadAsync<MartenSagaDocument>(identity, cancellationToken)
                    .ConfigureAwait(false);

                if (existing is not null)
                {
                    throw new SagaConcurrencyException(item.Correlation);
                }

                session.Insert(new MartenSagaDocument
                {
                    Id = identity,
                    CorrelationId = item.Correlation.CorrelationId,
                    SagaDefinitionId = item.Correlation.SagaDefinitionId,
                    TenantId = NormalizeTenantId(item.Correlation.TenantId),
                    StateJson = stateJson,
                    Version = 1,
                    IsCompleted = false,
                    LastAppliedMessageId = item.AppliedMessageId,
                    CreatedAt = now,
                    UpdatedAt = now
                });

                try
                {
                    await session.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception) when (IsConcurrencyConflict(exception))
                {
                    throw new SagaConcurrencyException(item.Correlation);
                }

                return;
            }

            var current = await session
                .LoadAsync<MartenSagaDocument>(identity, cancellationToken)
                .ConfigureAwait(false);

            if (current is null ||
                current.Version != item.ExpectedVersion ||
                current.IsCompleted)
            {
                throw new SagaConcurrencyException(item.Correlation);
            }

            // The loaded document is updated in place, never replaced: the update asserts the version captured
            // at load, which is the check that serializes concurrent saves.
            current.StateJson = stateJson;
            current.Version += 1;
            current.UpdatedAt = now;

            if (item.AppliedMessageId is not null)
            {
                current.LastAppliedMessageId = item.AppliedMessageId;
            }

            session.Update(current);

            try
            {
                await session.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (IsConcurrencyConflict(exception))
            {
                throw new SagaConcurrencyException(item.Correlation);
            }
        }
    }

    /// <inheritdoc />
    public async Task CompleteAsync(SagaCompleteItem item, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentOutOfRangeException.ThrowIfNegative(item.ExpectedVersion);

        var now = _clock.GetUtcNow();
        var session = _documentStore.LightweightSession(IsolationLevel.ReadCommitted);

        await using (session.ConfigureAwait(false))
        {
            var current = await session
                .LoadAsync<MartenSagaDocument>(BuildDocumentId(item.Correlation), cancellationToken)
                .ConfigureAwait(false);

            if (current is null ||
                current.Version != item.ExpectedVersion ||
                current.IsCompleted)
            {
                throw new SagaConcurrencyException(item.Correlation);
            }

            // The loaded document is updated in place, never replaced: the update asserts the version captured
            // at load, which is the check that serializes concurrent completions.
            current.IsCompleted = true;
            current.Version += 1;
            current.LastAppliedMessageId = item.AppliedMessageId;
            current.UpdatedAt = now;

            session.Update(current);

            try
            {
                await session.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (IsConcurrencyConflict(exception))
            {
                throw new SagaConcurrencyException(item.Correlation);
            }
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SagaInstanceSummary>> QueryAsync(
        SagaQueryFilter filter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(filter.Take, 0);

        var session = _documentStore.QuerySession();

        await using (session.ConfigureAwait(false))
        {
            var query = ApplyQueryFilters(session.Query<MartenSagaDocument>(), filter);

            var documents = await query
                .OrderByDescending(document => document.UpdatedAt)
                .Take(filter.Take)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            return documents.Select(ToSummary).ToList();
        }
    }

    /// <inheritdoc />
    public async Task<int> PurgeAsync(SagaPurgeFilter filter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        if (filter.Take is not null)
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(filter.Take.Value, 0);
        }

        if (filter is
            {
                SagaDefinitionId: null,
                CorrelationId: null,
                TenantId: null,
                IsCompleted: null,
                CompletedBefore: null,
                Take: null
            })
        {
            throw new ArgumentException(
                "Saga purge requires at least one predicate or a Take limit; refusing to delete every saga row.",
                nameof(filter));
        }

        var session = _documentStore.LightweightSession(IsolationLevel.ReadCommitted);

        await using (session.ConfigureAwait(false))
        {
            var query = ApplyPurgeFilters(session.Query<MartenSagaDocument>(), filter)
                .OrderBy(document => document.UpdatedAt);

            var candidates = filter.Take is null
                ? query
                : query.Take(filter.Take.Value);

            var documents = await candidates.ToListAsync(cancellationToken).ConfigureAwait(false);

            foreach (var document in documents)
            {
                session.Delete<MartenSagaDocument>(document.Id);
            }

            await session.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            return documents.Count;
        }
    }

    /// <summary>
    ///     Applies the optional query filter predicates to one Marten saga document query.
    /// </summary>
    /// <param name="query">The saga document query.</param>
    /// <param name="filter">The optional query predicates.</param>
    /// <returns>The query with every supplied predicate applied.</returns>
    private static IQueryable<MartenSagaDocument> ApplyQueryFilters(
        IQueryable<MartenSagaDocument> query,
        SagaQueryFilter filter)
    {
        var sagaDefinitionId = filter.SagaDefinitionId;

        if (sagaDefinitionId is not null)
        {
            query = query.Where(document => document.SagaDefinitionId == sagaDefinitionId);
        }

        var correlationId = filter.CorrelationId;

        if (correlationId is not null)
        {
            query = query.Where(document => document.CorrelationId == correlationId);
        }

        var tenantId = NormalizeTenantFilter(filter.TenantId);

        if (tenantId is not null)
        {
            query = query.Where(document => document.TenantId == tenantId);
        }

        var isCompleted = filter.IsCompleted;

        if (isCompleted is not null)
        {
            var completed = isCompleted.Value;
            query = query.Where(document => document.IsCompleted == completed);
        }

        return query;
    }

    /// <summary>
    ///     Applies the optional purge filter predicates to one Marten saga document query.
    /// </summary>
    /// <param name="query">The saga document query.</param>
    /// <param name="filter">The optional purge predicates.</param>
    /// <returns>The query with every supplied predicate applied.</returns>
    private static IQueryable<MartenSagaDocument> ApplyPurgeFilters(
        IQueryable<MartenSagaDocument> query,
        SagaPurgeFilter filter)
    {
        var sagaDefinitionId = filter.SagaDefinitionId;

        if (sagaDefinitionId is not null)
        {
            query = query.Where(document => document.SagaDefinitionId == sagaDefinitionId);
        }

        var correlationId = filter.CorrelationId;

        if (correlationId is not null)
        {
            query = query.Where(document => document.CorrelationId == correlationId);
        }

        var tenantId = NormalizeTenantFilter(filter.TenantId);

        if (tenantId is not null)
        {
            query = query.Where(document => document.TenantId == tenantId);
        }

        var isCompleted = filter.IsCompleted;

        if (isCompleted is not null)
        {
            var completed = isCompleted.Value;
            query = query.Where(document => document.IsCompleted == completed);
        }

        var completedBefore = filter.CompletedBefore;

        if (completedBefore is not null)
        {
            var cutoff = completedBefore.Value;
            query = query.Where(document => document.IsCompleted && document.UpdatedAt < cutoff);
        }

        return query;
    }

    /// <summary>
    ///     Maps one Marten saga document to a query summary.
    /// </summary>
    /// <param name="document">The saga document.</param>
    /// <returns>The query summary.</returns>
    private static SagaInstanceSummary ToSummary(MartenSagaDocument document)
    {
        return new SagaInstanceSummary
        {
            Correlation = new SagaCorrelation
            {
                CorrelationId = document.CorrelationId,
                SagaDefinitionId = document.SagaDefinitionId,
                TenantId = string.IsNullOrEmpty(document.TenantId) ? null : document.TenantId
            },
            Version = document.Version,
            IsCompleted = document.IsCompleted,
            CreatedAt = document.CreatedAt,
            UpdatedAt = document.UpdatedAt
        };
    }

    /// <summary>
    ///     Decides whether a failure a save session raised is a lost saga write race worth mapping.
    /// </summary>
    /// <param name="exception">The failure a save session raised, whatever wrapper Marten put around it.</param>
    /// <returns><see langword="true" /> when the chain reports a Marten concurrency or existence conflict.</returns>
    private static bool IsConcurrencyConflict(Exception exception)
    {
        foreach (var current in EnumerateExceptions(exception))
        {
            if (current is ConcurrencyException or
                ConcurrentUpdateException or
                DocumentAlreadyExistsException)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     Enumerates an exception, its inner exceptions, and every exception an <see cref="AggregateException" />
    ///     carries, depth first.
    /// </summary>
    /// <param name="exception">The exception to start from.</param>
    /// <returns>Every exception reachable from <paramref name="exception" />.</returns>
    /// <remarks>
    ///     A batched commit can report several failures in one <see cref="AggregateException" />, and its
    ///     <see cref="Exception.InnerException" /> is only the first of them, so walking that chain alone can miss
    ///     the storage error that matters.
    /// </remarks>
    private static IEnumerable<Exception> EnumerateExceptions(Exception exception)
    {
        var pending = new Stack<Exception>();
        pending.Push(exception);

        while (pending.Count > 0)
        {
            var current = pending.Pop();
            yield return current;

            if (current is AggregateException aggregate)
            {
                for (var index = aggregate.InnerExceptions.Count - 1; index >= 0; index--)
                {
                    pending.Push(aggregate.InnerExceptions[index]);
                }
            }
            else if (current.InnerException is { } inner)
            {
                pending.Push(inner);
            }
        }
    }

    /// <summary>
    ///     Builds the escaped composite document identity for one saga correlation.
    /// </summary>
    /// <param name="correlation">The saga correlation.</param>
    /// <returns>The document identity stored as <see cref="MartenSagaDocument.Id" />.</returns>
    private static string BuildDocumentId(SagaCorrelation correlation)
    {
        return string.Concat(
            Uri.EscapeDataString(NormalizeTenantId(correlation.TenantId)),
            "/",
            Uri.EscapeDataString(correlation.SagaDefinitionId),
            "/",
            Uri.EscapeDataString(correlation.CorrelationId));
    }

    /// <summary>
    ///     Preserves an omitted tenant filter while normalizing an explicit unscoped tenant.
    /// </summary>
    /// <param name="tenantId">The optional tenant filter.</param>
    /// <returns>
    ///     <see langword="null" /> when the query should include all tenants; otherwise, the normalized tenant identifier.
    /// </returns>
    private static string? NormalizeTenantFilter(string? tenantId)
    {
        return tenantId is null ? null : NormalizeTenantId(tenantId);
    }

    /// <summary>
    ///     Normalizes tenant identifiers for primary-key storage.
    /// </summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <returns>The normalized tenant identifier stored in saga rows.</returns>
    private static string NormalizeTenantId(string? tenantId)
    {
        return string.IsNullOrWhiteSpace(tenantId) ? string.Empty : tenantId;
    }
}
