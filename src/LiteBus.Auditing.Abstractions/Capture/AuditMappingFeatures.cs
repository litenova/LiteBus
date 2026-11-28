using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace LiteBus.Auditing.Abstractions;

/// <summary>
///     The store-specific values an audit store offers an <see cref="IAuditRecordMapper{TDocument}" /> while it maps one
///     record, keyed by type.
/// </summary>
/// <remarks>
///     <para>
///         The audit stores put their write session here before they call
///         <see cref="IAuditRecordMapper{TDocument}.MapAsync" />, so anything the mapper writes through it commits or
///         rolls back with the record. The Marten store offers its <c>IDocumentSession</c>, the Entity Framework Core
///         store its <c>DbContext</c>, and the PostgreSQL store its open <c>NpgsqlConnection</c> and
///         <c>NpgsqlTransaction</c>.
///     </para>
///     <para>
///         On the success path the session is the business session, so the mapper's writes commit with the handler's
///         change. On the out-of-band path, which records denials, failures and cancellations after the business
///         change rolls back, the session is the short session the record is written in, so the mapper's writes
///         survive that rollback together with the record.
///     </para>
/// </remarks>
public sealed class AuditMappingFeatures
{
    /// <summary>
    ///     The features, keyed by the type they were set under.
    /// </summary>
    private readonly Dictionary<Type, object> _features = [];

    /// <summary>
    ///     Sets a feature under its type, replacing any feature of the same type.
    /// </summary>
    /// <typeparam name="TFeature">The type the feature is looked up by.</typeparam>
    /// <param name="feature">The feature.</param>
    /// <returns>This collection, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="feature" /> is <see langword="null" />.</exception>
    public AuditMappingFeatures Set<TFeature>(TFeature feature)
        where TFeature : class
    {
        ArgumentNullException.ThrowIfNull(feature);

        _features[typeof(TFeature)] = feature;
        return this;
    }

    /// <summary>
    ///     Gets the feature set under <typeparamref name="TFeature" />, when the store offers one.
    /// </summary>
    /// <typeparam name="TFeature">The type the feature was set under.</typeparam>
    /// <param name="feature">The feature, or <see langword="null" /> when the store offers none.</param>
    /// <returns><see langword="true" /> when the store offers the feature.</returns>
    public bool TryGet<TFeature>([NotNullWhen(true)] out TFeature? feature)
        where TFeature : class
    {
        if (_features.TryGetValue(typeof(TFeature), out var value))
        {
            feature = (TFeature) value;
            return true;
        }

        feature = null;
        return false;
    }

    /// <summary>
    ///     Gets the feature set under <typeparamref name="TFeature" />.
    /// </summary>
    /// <typeparam name="TFeature">The type the feature was set under.</typeparam>
    /// <returns>The feature.</returns>
    /// <exception cref="InvalidOperationException">The store offers no feature of that type.</exception>
    public TFeature Get<TFeature>()
        where TFeature : class
    {
        return TryGet<TFeature>(out var feature)
            ? feature
            : throw new InvalidOperationException(
                $"The audit store offers no '{typeof(TFeature).FullName}' to the record mapper. The Marten store offers "
                + "IDocumentSession, the Entity Framework Core store DbContext, and the PostgreSQL store NpgsqlConnection "
                + "and NpgsqlTransaction.");
    }
}
