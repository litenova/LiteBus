using LiteBus.Commands;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Messaging.Pipeline;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Mediator.UnitTests.Registry;

/// <summary>
///     Verifies that each composition assigns the process-wide mediation telemetry options from its own builder.
/// </summary>
[Collection("Sequential")]
public sealed class MessageModuleTelemetryTests : LiteBusTestBase
{
    [Fact]
    public void A_composition_without_telemetry_does_not_inherit_the_previous_composition_settings()
    {
        Compose(new MediationTelemetryOptions
        {
            Spans = false,
            Metrics = false,
            StageSpans = true,
            StageMetrics = true
        });

        MediationTelemetry.Options.Should().Be(new MediationTelemetryOptions
        {
            Spans = false,
            Metrics = false,
            StageSpans = true,
            StageMetrics = true
        });

        Compose(null);

        MediationTelemetry.Options.Should().Be(new MediationTelemetryOptions());
    }

    /// <summary>
    ///     Composes messaging with the requested telemetry configuration.
    /// </summary>
    /// <param name="telemetry">The telemetry configuration, or null to leave the builder unconfigured.</param>
    private static void Compose(MediationTelemetryOptions? telemetry)
    {
        var services = new ServiceCollection();

        services.AddLiteBus(registry =>
        {
            registry.AddMessaging(messaging =>
            {
                if (telemetry is not null)
                {
                    messaging.UseTelemetry(telemetry);
                }
            });

            registry.AddCommands(_ => { });
        });
    }
}
