using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Storage.InMemory;
using LiteBus.Messaging;
using LiteBus.Runtime.Abstractions;
using LiteBus.Runtime.Abstractions.Hosting;
using LiteBus.Runtime.Abstractions.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Outbox.UnitTests;

/// <summary>
///     Verifies a consumer-owned outbox probe is recorded once in the manifest and registered once in the container.
/// </summary>
public sealed class OutboxDiagnosticCheckRegistrationTests
{
    [Fact]
    public async Task AddDiagnosticCheck_ShouldAppearOnceInManifestAndContainer()
    {
        var services = new ServiceCollection().AddLiteBus(registry =>
        {
            registry.AddMessaging(_ =>
            {
            });

            registry.AddOutbox(outbox =>
            {
                outbox.UseInMemoryStorage();
                outbox.AddDiagnosticCheck<ConsumerProbe>(ConsumerProbe.CheckName);
            });
        });

        services.Count(descriptor => descriptor.ServiceType == typeof(ConsumerProbe)).Should().Be(1);

        await using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<LiteBusHostManifest>().DiagnosticChecks
            .Count(descriptor => descriptor.ImplementationType == typeof(ConsumerProbe))
            .Should().Be(1);
        provider.GetRequiredService<ConsumerProbe>().Should().NotBeNull();
    }

    private sealed class ConsumerProbe : IDiagnosticCheck
    {
        public const string CheckName = "outbox.consumer.probe";

        public string Name => CheckName;

        public Task<DiagnosticResult> CheckAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new DiagnosticResult(DiagnosticStatus.Healthy, "ok"));
        }
    }
}
