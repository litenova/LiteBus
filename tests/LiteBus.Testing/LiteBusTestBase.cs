using LiteBus.Runtime.Abstractions;

namespace LiteBus.Testing;

/// <summary>
///     Base class for LiteBus tests that builds an isolated <see cref="InboxOutboxTestHost" /> per test class instance.
/// </summary>
public abstract class LiteBusTestBase : IAsyncDisposable
{
    /// <summary>
    ///     Gets the isolated LiteBus test host created for the current test class.
    /// </summary>
    protected InboxOutboxTestHost Host { get; private set; } = null!;

    /// <summary>
    ///     Creates the default in-memory inbox and outbox test host.
    /// </summary>
    /// <param name="configureModules">An optional LiteBus module configuration callback.</param>
    /// <param name="timeProvider">An optional fake clock for deterministic timestamps.</param>
    protected virtual void InitializeHost(
        Action<IModuleRegistry>? configureModules = null,
        TimeProvider? timeProvider = null)
    {
        Host = InboxOutboxTestHost.Create(configureModules, timeProvider: timeProvider);
    }

    /// <inheritdoc />
    public virtual async ValueTask DisposeAsync()
    {
        if (Host is not null)
        {
            await Host.DisposeAsync().ConfigureAwait(false);
        }

        GC.SuppressFinalize(this);
    }
}
