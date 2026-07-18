using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Runtime.Abstractions;

/// <summary>
///     Defines one-shot work that runs during host startup before long-running background services begin.
/// </summary>
/// <remarks>
///     Implementations such as PostgreSQL schema initializers run ensure and validate once during host
///     startup. The generic host bridge runs all <see cref="IStartupTask" /> instances sequentially before
///     <see cref="IBackgroundService" /> loops start
///     <see cref="IBackgroundService.ExecuteAsync(System.Threading.CancellationToken)" />.
///     Register implementations through <see cref="IModuleConfiguration.RegisterStartupTask" />.
///     Register storage modules before inbox, outbox, or ingress modules when multiple startup tasks are
///     present so schema work appears first in the startup task manifest.
/// </remarks>
public interface IStartupTask
{
    /// <summary>
    ///     Runs startup work until it completes or <paramref name="cancellationToken" /> is canceled.
    /// </summary>
    /// <param name="cancellationToken">The token used to cancel startup work.</param>
    /// <returns>A task that completes when startup work finishes.</returns>
    Task RunAsync(CancellationToken cancellationToken);
}