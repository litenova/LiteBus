namespace LiteBus.DurableTransport.IntegrationTesting;

/// <summary>
///     Executes broker integration setup and maps Docker failures to skippable outcomes.
/// </summary>
public static class DockerTestGate
{
    /// <summary>
    ///     Message shown when Docker-backed integration tests cannot start a container.
    /// </summary>
    public const string DockerRequiredMessage =
        "Broker integration tests require Docker. Start Docker Desktop (or the Docker daemon) and run the tests again.";

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
            await initialize().ConfigureAwait(false);
        }
        catch (Exception exception) when (IsDockerUnavailable(exception))
        {
            throw new InvalidOperationException(DockerRequiredMessage, exception);
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
            if (current.Message.Contains("Docker", StringComparison.OrdinalIgnoreCase)
                || current.Message.Contains("pipe", StringComparison.OrdinalIgnoreCase)
                || current.Message.Contains("container", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
