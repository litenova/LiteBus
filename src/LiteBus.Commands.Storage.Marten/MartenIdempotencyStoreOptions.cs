using System;

namespace LiteBus.Commands.Storage.Marten;

/// <summary>
///     Configures the Marten idempotency store claim timeout.
/// </summary>
/// <remarks>
///     <para>
///         Adjusted through the <c>configure</c> action on
///         <see cref="CommandModuleBuilderMartenExtensions.UseMartenIdempotencyStore(CommandModuleBuilder, Action{MartenIdempotencyStoreOptions})" />.
///         The properties are settable rather than init-only so that action can assign them, the same shape the
///         LiteBus host options use.
///     </para>
/// </remarks>
public sealed class MartenIdempotencyStoreOptions
{
    /// <summary>
    ///     Gets or sets how long an unsettled claim blocks its key before another claimant may take it over.
    /// </summary>
    /// <value>
    ///     The default is five minutes. Must be positive. Keep it longer than the longest mediation that claims
    ///     through the store: a claimant that is still applying when its claim expires can lose the key to a
    ///     takeover, and its late completion then marks the new claimant's work applied.
    /// </value>
    public TimeSpan ClaimTimeout { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    ///     Validates the configured values before the store is built.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><see cref="ClaimTimeout" /> is zero or negative.</exception>
    public void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(ClaimTimeout, TimeSpan.Zero);
    }
}
