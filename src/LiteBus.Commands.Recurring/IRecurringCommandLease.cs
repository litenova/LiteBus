using System;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Commands;

namespace LiteBus.Commands.Recurring;

/// <summary>
///     Acquires a named single-flight lease so a recurring command loop runs on at most one host instance at a time.
/// </summary>
/// <remarks>
///     <para>
///         The guarantee is per name while the handle is held: at most one holder across the deployment owns a given
///         lease name at any moment. Disposing the returned handle releases the lease. An implementation whose backing
///         store can promise it also releases the lease automatically when the holder's process dies, so a crashed
///         replica cannot block the loop forever.
///     </para>
///     <para>
///         Register an implementation through a child module on <see cref="CommandModuleBuilder" />, so composition can
///         see it: the loop's composition validation fails when <see cref="RecurringCommandHostOptions.RequireLease" />
///         is set and no implementation is registered. <c>LiteBus.Commands.Recurring.Storage.PostgreSql</c> ships one
///         backed by a renewable PostgreSQL lease row.
///     </para>
/// </remarks>
public interface IRecurringCommandLease
{
    /// <summary>
    ///     Tries to acquire the named lease for one pass of a recurring command loop.
    /// </summary>
    /// <param name="leaseName">The lease name; loops default it to the command type's full name.</param>
    /// <param name="cancellationToken">A token that cancels the acquisition attempt.</param>
    /// <returns>
    ///     A handle that releases the lease when disposed asynchronously, or <see langword="null" /> when another
    ///     holder owns the lease.
    /// </returns>
    /// <exception cref="ArgumentException"><paramref name="leaseName" /> is null, empty, or whitespace.</exception>
    Task<IAsyncDisposable?> TryAcquireAsync(string leaseName, CancellationToken cancellationToken);
}
