using LiteBus.Extensions.Diagnostics.HealthChecks;
using LiteBus.Runtime.Abstractions.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Samples.Documentation;

/// <summary>
///     Provides compiled source for the health-check registration snippet used by the diagnostics guide.
/// </summary>
internal static class HealthCheckRegistrationSnippet
{
    /// <summary>
    ///     Registers LiteBus diagnostic probes as an ASP.NET Core health check.
    /// </summary>
    /// <param name="services">The application service collection.</param>
    internal static void Register(IServiceCollection services)
    {
        // <docs-snippet health-check-registration>
        services.AddHealthChecks().AddLiteBus(options =>
        {
            options.FailHealthWhenNoProbes = true;
            options.DiagnosticChecks = new DiagnosticCheckRunOptions
            {
                Timeout = TimeSpan.FromSeconds(3),
                MaxParallelism = 4,
                RunTimeout = TimeSpan.FromSeconds(10)
            };
        });
        // </docs-snippet>
    }
}
