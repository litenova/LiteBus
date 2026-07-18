namespace LiteBus.Testing;

/// <summary>
///     Base class for LiteBus tests that participate in shared test infrastructure disposal.
/// </summary>
public abstract class LiteBusTestBase : IAsyncDisposable
{
    /// <inheritdoc />
    public virtual ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }
}