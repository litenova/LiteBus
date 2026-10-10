namespace LiteBus.Storage.IntegrationTests;

/// <summary>
///     Classifies container start failures so a fixture reports a missing Docker runtime only when the runtime itself
///     is unavailable, and lets every other setup failure surface as itself.
/// </summary>
public static class DockerAvailability
{
    /// <summary>
    ///     Determines whether the exception chain comes from an unavailable container runtime.
    /// </summary>
    /// <param name="exception">The exception thrown while starting a container, or <see langword="null" />.</param>
    /// <returns><see langword="true" /> when a container-runtime exception type appears in the chain.</returns>
    /// <remarks>
    ///     Matches exception types, not message text, so a database or broker failure that mentions a pipe or a
    ///     container is not mistaken for a missing Docker daemon.
    /// </remarks>
    public static bool IsDockerUnavailable(Exception? exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current.GetType().FullName is "DotNet.Testcontainers.Builders.DockerUnavailableException"
                or "DotNet.Testcontainers.Guard.ArgumentException"
                or "Docker.DotNet.DockerApiException")
            {
                return true;
            }
        }

        return false;
    }
}
