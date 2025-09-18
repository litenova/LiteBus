using LiteBus.Messaging.Registry;

namespace LiteBus.Testing;

/// <summary>
/// Base class for LiteBus tests that ensures proper test isolation by clearing the message registry.
/// </summary>
public abstract class LiteBusTestBase : IDisposable
{
    /// <summary>
    /// Initializes a new instance of the test base class and ensures a clean registry state.
    /// </summary>
    protected LiteBusTestBase()
    {
        // Clear registry before each test to ensure isolation
        MessageRegistryAccessor.Instance.Clear();
    }

    /// <summary>
    /// Performs cleanup after test execution to prevent state pollution.
    /// </summary>
    public virtual void Dispose()
    {
        // Clear registry after each test to prevent pollution of subsequent tests
        MessageRegistryAccessor.Instance.Clear();
        GC.SuppressFinalize(this);
    }
}