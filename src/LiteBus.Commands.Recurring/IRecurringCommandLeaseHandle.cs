using System;
using System.Threading;

namespace LiteBus.Commands.Recurring;

/// <summary>
///     Holds one acquired recurring command lease for the lifetime of the pass that acquired it.
/// </summary>
/// <remarks>
///     <para>
///         Disposing the handle releases the lease. <see cref="LeaseLost" /> is cancelled when the lease is lost while
///         held: another holder took the row or renewals failed. A loop that holds the lease ends its busy period when
///         the token fires instead of running further passes undefended.
///     </para>
/// </remarks>
public interface IRecurringCommandLeaseHandle : IAsyncDisposable
{
    /// <summary>
    ///     Gets a token that is cancelled when the lease is lost while the handle is held.
    /// </summary>
    /// <value>
    ///     A token that fires when another holder takes the lease or a renewal fails. A clean dispose never cancels
    ///     it; releasing the lease is not losing it.
    /// </value>
    CancellationToken LeaseLost { get; }
}
