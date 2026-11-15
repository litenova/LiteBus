using System;
using System.Threading;

namespace LiteBus.Commands.Batching;

/// <summary>
///     Tracks, per asynchronous flow, whether a command batch is running, so a batch started from inside another
///     batch is refused.
/// </summary>
/// <remarks>
///     Each batch commits on its own. A batch started from a handler that runs inside another batch would commit
///     independently of the outer one and silently break its atomicity, so the batch mediator refuses it with an
///     <see cref="InvalidOperationException" />.
/// </remarks>
internal static class CommandBatchNesting
{
    /// <summary>
    ///     The marker for the current asynchronous flow; set while a batch runs.
    /// </summary>
    private static readonly AsyncLocal<bool> Running = new();

    /// <summary>
    ///     Gets a value indicating whether a batch is running in the current asynchronous flow.
    /// </summary>
    public static bool IsRunning => Running.Value;

    /// <summary>
    ///     Marks a batch as running in the current asynchronous flow until the returned scope is disposed.
    /// </summary>
    /// <returns>The scope that clears the marker.</returns>
    /// <exception cref="InvalidOperationException">A batch is already running in the current asynchronous flow.</exception>
    public static IDisposable Enter()
    {
        if (Running.Value)
        {
            throw new InvalidOperationException(
                "A command batch cannot start inside another command batch: each batch commits on its own, so a nested "
                + "batch would commit independently of the batch that contains it. Send the commands as part of the "
                + "outer batch instead.");
        }

        Running.Value = true;
        return new Scope();
    }

    /// <summary>
    ///     Clears the running marker when disposed.
    /// </summary>
    private sealed class Scope : IDisposable
    {
        /// <summary>
        ///     Whether the marker was already cleared.
        /// </summary>
        private bool _disposed;

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Running.Value = false;
        }
    }
}
