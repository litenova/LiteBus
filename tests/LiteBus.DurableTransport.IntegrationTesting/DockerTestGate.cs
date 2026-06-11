namespace LiteBus.DurableTransport.IntegrationTesting;

/// <summary>
///     Executes broker integration setup and maps Docker failures to skippable outcomes.
/// </summary>
public static class DockerTestGate
{
    /// <summary>
    ///     Environment variable set by CI when broker transport jobs must fail instead of skipping tests.
    /// </summary>
    public const string StrictTransportEnvironmentVariable = "LITEBUS_CI_STRICT_TRANSPORT";

    /// <summary>
    ///     Message shown when Docker-backed integration tests cannot start a container.
    /// </summary>
    public const string DockerRequiredMessage =
        "Broker integration tests require Docker. Start Docker Desktop (or the Docker daemon) and run the tests again.";

    /// <summary>
    ///     Gets a value indicating whether broker tests must fail when Docker or the emulator is unavailable.
    /// </summary>
    public static bool IsStrictTransportMode => string.Equals(
        Environment.GetEnvironmentVariable(StrictTransportEnvironmentVariable),
        "true",
        StringComparison.OrdinalIgnoreCase);

    /// <summary>
    ///     Runs broker initialization and wraps Docker failures in <see cref="InvalidOperationException" />.
    /// </summary>
    /// <param name="initialize">The broker initialization delegate.</param>
    /// <returns>A task that completes when initialization succeeds.</returns>
    public static async Task RunAsync(Func<Task> initialize)
    {
        ArgumentNullException.ThrowIfNull(initialize);

        try
        {
            await initialize();
        }
        catch (Exception exception) when (IsDockerUnavailable(exception))
        {
            throw new InvalidOperationException(DockerRequiredMessage, exception);
        }
    }

    /// <summary>
    ///     Ensures a broker fixture started successfully or fails when CI strict transport mode is enabled.
    /// </summary>
    /// <param name="isAvailable">Whether the broker fixture initialized successfully.</param>
    /// <param name="brokerName">The broker label included in strict-mode failure messages.</param>
    /// <exception cref="InvalidOperationException">
    ///     Thrown when <paramref name="isAvailable" /> is <see langword="false" /> and strict transport mode is enabled.
    /// </exception>
    public static void EnsureBrokerAvailable(bool isAvailable, string brokerName)
    {
        if (!isAvailable && IsStrictTransportMode)
        {
            throw new InvalidOperationException(
                $"{brokerName} is unavailable while {StrictTransportEnvironmentVariable} is enabled.");
        }
    }

    /// <summary>
    ///     Determines whether the exception chain indicates Docker is unavailable.
    /// </summary>
    /// <param name="exception">The exception thrown while starting a container.</param>
    /// <returns><see langword="true" /> when Docker appears unavailable.</returns>
    private static bool IsDockerUnavailable(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current.Message.Contains("Docker", StringComparison.OrdinalIgnoreCase) ||
                current.Message.Contains("pipe", StringComparison.OrdinalIgnoreCase) ||
                current.Message.Contains("container", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}