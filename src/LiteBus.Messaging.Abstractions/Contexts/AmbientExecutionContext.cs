#nullable enable
using System.Threading;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
/// Provides a static class for managing the ambient execution context.
/// </summary>
public static class AmbientExecutionContext
{
    private static readonly AsyncLocal<IExecutionContext?> ExecutionContextLocal = new();

    /// <summary>
    /// Gets or sets the current execution context associated with the ambient scope.
    /// </summary>
    public static IExecutionContext? Current
    {
        get => ExecutionContextLocal.Value;
        set => ExecutionContextLocal.Value = value;
    }
}