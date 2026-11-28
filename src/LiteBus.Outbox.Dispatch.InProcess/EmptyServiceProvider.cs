using System;

namespace LiteBus.Outbox.Dispatch.InProcess;

/// <summary>
///     A service provider that resolves nothing, used only to evaluate the default reaction retry policy when the
///     dispatcher was constructed without a dispatch scope factory.
/// </summary>
/// <remarks>
///     The default policy reads no services, so it evaluates the same over this provider as over a dispatch scope.
///     A replacement policy that needs services requires composition through <c>AddLiteBus</c>, which always
///     registers a scope factory.
/// </remarks>
internal sealed class EmptyServiceProvider : IServiceProvider
{
    /// <summary>
    ///     Gets the shared instance.
    /// </summary>
    public static EmptyServiceProvider Instance { get; } = new();

    /// <summary>
    ///     Initializes a new instance of the <see cref="EmptyServiceProvider" /> class.
    /// </summary>
    private EmptyServiceProvider()
    {
    }

    /// <inheritdoc />
    public object? GetService(Type serviceType)
    {
        return null;
    }
}
