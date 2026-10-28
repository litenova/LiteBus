using System;
using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Transport.AwsSqs;

/// <summary>
///     Extends SQS message visibility while a delivery handler runs.
/// </summary>
internal static class SqsVisibilityHeartbeat
{
    /// <summary>
    ///     Computes the visibility extension interval for the configured visibility timeout.
    /// </summary>
    /// <param name="visibilityTimeoutSeconds">The SQS visibility timeout in seconds.</param>
    /// <returns>Half the visibility timeout, or <see cref="TimeSpan.Zero" /> when the timeout is not positive.</returns>
    internal static TimeSpan ComputeExtensionInterval(int visibilityTimeoutSeconds)
    {
        if (visibilityTimeoutSeconds <= 0)
        {
            return TimeSpan.Zero;
        }

        return TimeSpan.FromSeconds(visibilityTimeoutSeconds / 2.0);
    }

    /// <summary>
    ///     Runs handler work while periodically extending message visibility.
    /// </summary>
    /// <remarks>
    ///     Extension failures end the loop without failing the operation: an expired receipt handle already makes
    ///     further extensions meaningless, and the handler result still decides acknowledgement.
    /// </remarks>
    /// <typeparam name="T">The handler result type.</typeparam>
    /// <param name="extendVisibilityAsync">The visibility extension invoked once per interval.</param>
    /// <param name="extensionInterval">The delay between extensions. A non-positive interval disables extension.</param>
    /// <param name="timeProvider">The time source used for extension delays.</param>
    /// <param name="operation">The handler work executed while extensions continue.</param>
    /// <param name="cancellationToken">The token used to cancel the operation and stop extensions.</param>
    /// <returns>The value returned by <paramref name="operation" />.</returns>
    internal static async Task<T> RunWithVisibilityExtensionAsync<T>(
        Func<CancellationToken, Task> extendVisibilityAsync,
        TimeSpan extensionInterval,
        TimeProvider timeProvider,
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(extendVisibilityAsync);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(operation);

        if (extensionInterval <= TimeSpan.Zero)
        {
            return await operation(cancellationToken).ConfigureAwait(false);
        }

        using var heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var heartbeatTask = ExtendLoopAsync(extendVisibilityAsync, extensionInterval, timeProvider, heartbeatCts.Token);

        try
        {
            return await operation(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await heartbeatCts.CancelAsync().ConfigureAwait(false);
            await heartbeatTask.ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Issues one visibility extension per interval until cancellation is requested.
    /// </summary>
    /// <remarks>
    ///     A failed extension ends the loop without throwing. SQS rejects extensions on expired receipt handles,
    ///     so retrying cannot recover visibility and must not fail the in-flight handler.
    /// </remarks>
    /// <param name="extendVisibilityAsync">The visibility extension invoked once per interval.</param>
    /// <param name="extensionInterval">The delay between extensions.</param>
    /// <param name="timeProvider">The time source used for extension delays.</param>
    /// <param name="cancellationToken">The token that stops the extension loop.</param>
    /// <returns>A task that completes when the loop stops.</returns>
    private static async Task ExtendLoopAsync(
        Func<CancellationToken, Task> extendVisibilityAsync,
        TimeSpan extensionInterval,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(extensionInterval, timeProvider, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            try
            {
                await extendVisibilityAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
#pragma warning disable CA1031 // Visibility extension is best-effort; an expired receipt must not fail the handler.
            catch (Exception)
#pragma warning restore CA1031
            {
                return;
            }
        }
    }
}
