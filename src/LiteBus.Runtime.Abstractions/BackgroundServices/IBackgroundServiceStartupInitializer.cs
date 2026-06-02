namespace LiteBus.Runtime.Abstractions;

/// <summary>
///     Marks a background service that completes startup work before long-running host loops begin.
/// </summary>
/// <remarks>
///     Implementations such as PostgreSQL schema initializers run ensure and validate once during host
///     startup. The generic host bridge runs all <see cref="IBackgroundServiceStartupInitializer" /> instances
///     sequentially before other <see cref="IBackgroundService" /> implementations start
///     <see cref="IBackgroundService.ExecuteAsync(System.Threading.CancellationToken)" />.
///     Register storage modules before inbox, outbox, or ingress modules so schema initializers appear first
///     in the background service manifest when multiple startup initializers are present.
/// </remarks>
public interface IBackgroundServiceStartupInitializer : IBackgroundService;
