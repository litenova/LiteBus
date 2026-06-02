using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Runtime.Abstractions;

/// <summary>
///     Defines work that runs when the application host starts and stops when the host shuts down.
/// </summary>
/// <remarks>
///     Implementations may complete quickly or run until cancellation (for example processor or ingress loops).
///     One-shot startup work should implement <see cref="IBackgroundServiceStartupInitializer" /> instead.
///     Register implementations through <see cref="IModuleConfiguration.RegisterBackgroundService" />.
/// </remarks>
public interface IBackgroundService
{
    /// <summary>
    ///     Runs until <paramref name="stoppingToken" /> is canceled.
    /// </summary>
    /// <param name="stoppingToken">The token used to stop the work.</param>
    /// <returns>A task that completes when the work stops.</returns>
    Task ExecuteAsync(CancellationToken stoppingToken);
}
