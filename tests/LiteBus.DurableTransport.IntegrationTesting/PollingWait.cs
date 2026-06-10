namespace LiteBus.DurableTransport.IntegrationTesting;

/// <summary>
///     Polls asynchronous conditions until they succeed or a timeout elapses.
/// </summary>
public static class PollingWait
{
    /// <summary>
    ///     Waits until the supplied condition becomes true or the timeout elapses.
    /// </summary>
    /// <param name="condition">The condition polled until it returns <see langword="true" />.</param>
    /// <param name="timeout">The maximum time to wait.</param>
    /// <returns>A task that completes when the condition becomes true.</returns>
    /// <exception cref="TimeoutException">Thrown when the condition does not become true before the timeout.</exception>
    public static async Task UntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(condition);

        var deadline = Environment.TickCount64 + (long)timeout.TotalMilliseconds;

        while (!condition() && Environment.TickCount64 < deadline)
        {
            await Task.Delay(25).ConfigureAwait(false);
        }

        if (!condition())
        {
            throw new TimeoutException($"Condition was not met within {timeout}.");
        }
    }

    /// <summary>
    ///     Waits until the supplied asynchronous condition becomes true or the timeout elapses.
    /// </summary>
    /// <param name="condition">The condition polled until it returns <see langword="true" />.</param>
    /// <param name="timeout">The maximum time to wait.</param>
    /// <returns>A task that completes when the condition becomes true.</returns>
    /// <exception cref="TimeoutException">Thrown when the condition does not become true before the timeout.</exception>
    public static async Task UntilAsync(Func<Task<bool>> condition, TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(condition);

        var deadline = Environment.TickCount64 + (long)timeout.TotalMilliseconds;

        while (Environment.TickCount64 < deadline)
        {
            if (await condition().ConfigureAwait(false))
            {
                return;
            }

            await Task.Delay(25).ConfigureAwait(false);
        }

        if (!await condition().ConfigureAwait(false))
        {
            throw new TimeoutException($"Condition was not met within {timeout}.");
        }
    }
}
