using System;
using LiteBus.Storage.PostgreSql;

namespace LiteBus.Commands.Recurring.Storage.PostgreSql;

/// <summary>
///     Options for the PostgreSQL recurring lease table, schema bootstrap, and lease timing.
/// </summary>
/// <remarks>
///     <para>
///         <see cref="LeaseDuration" /> bounds how long a crashed holder blocks the loop: renewals stop with the process,
///         so the row becomes takeable once <c>expires_at</c> passes on the database clock. <see cref="RenewInterval" />
///         controls how often a live holder pushes that deadline forward. Keep the renewal interval well below the lease
///         duration (the default is one third), or a slow renewal lets another instance take the lease while the pass is
///         still running.
///     </para>
///     <para>
///         A short duration frees a dead holder faster but narrows the margin a live holder has against a stalled
///         renewal; a long duration tolerates stalls but keeps a dead holder blocking longer. Size the duration from how
///         quickly a replacement pass must run after a crash, then leave the renewal interval at its default.
///     </para>
/// </remarks>
public sealed record PostgreSqlRecurringLeaseOptions : PostgreSqlSchemaStoreOptions, IPostgreSqlStoreTableOptions
{
    /// <summary>
    ///     Gets the PostgreSQL schema that contains the recurring leases table.
    /// </summary>
    public string SchemaName { get; init; } = "public";

    /// <summary>
    ///     Gets the recurring leases table name.
    /// </summary>
    public string TableName { get; init; } = "litebus_recurring_leases";

    /// <summary>
    ///     Gets a value indicating whether schema creation runs during host startup.
    /// </summary>
    public bool EnsureSchemaCreationOnStartup { get; init; } = true;

    /// <summary>
    ///     Gets a value indicating whether schema validation runs during host startup.
    /// </summary>
    public bool ValidateSchemaCreationOnStartup { get; init; }

    /// <summary>
    ///     Gets how long one acquisition holds the lease before it must be renewed.
    /// </summary>
    /// <value>
    ///     The default is 30 seconds. Must be positive. Measured on the database clock from acquisition and from each
    ///     renewal, so host clock skew between replicas never shortens or extends it.
    /// </value>
    public TimeSpan LeaseDuration { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    ///     Gets how often a held lease is renewed while its handle is alive.
    /// </summary>
    /// <value>
    ///     The default is <see langword="null" />, which renews every third of <see cref="LeaseDuration" />. Set an
    ///     explicit positive interval to pace renewals differently; keep it well below <see cref="LeaseDuration" /> so a
    ///     live holder never lets its own lease lapse.
    /// </value>
    public TimeSpan? RenewInterval { get; init; }

    /// <summary>
    ///     Gets the renewal interval the lease loop actually uses.
    /// </summary>
    /// <value>
    ///     <see cref="RenewInterval" /> when set; otherwise every third of <see cref="LeaseDuration" />.
    /// </value>
    public TimeSpan EffectiveRenewInterval => RenewInterval ?? TimeSpan.FromTicks(LeaseDuration.Ticks / 3);

    /// <summary>
    ///     Validates the lease timing values before the lease is used.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     <see cref="LeaseDuration" /> or the effective renewal interval is zero or negative.
    /// </exception>
    public void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(LeaseDuration.Ticks);

        if (RenewInterval.HasValue)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(RenewInterval.Value.Ticks);
        }
        else
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(EffectiveRenewInterval.Ticks, nameof(LeaseDuration));
        }
    }
}
