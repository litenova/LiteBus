using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Inbox;
using LiteBus.Inbox.Storage.InMemory;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Runtime.Abstractions.Diagnostics;
using LiteBus.Runtime.Abstractions.Hosting;
using LiteBus.Runtime.Extensions.Microsoft.DependencyInjection;
using LiteBus.Runtime.Modules;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Runtime.UnitTests;

public sealed class ModuleConfigurationDiagnosticCheckTests
{
    [Fact]
    public void RegisterDiagnosticCheck_SameTypeTwice_ShouldIgnoreSecondRegistration()
    {
        var configuration = new ModuleConfiguration(new MicrosoftDependencyRegistryAdapter(new ServiceCollection()));

        configuration.RegisterDiagnosticCheck(typeof(SampleDiagnosticCheck), "litebus.sample");
        configuration.RegisterDiagnosticCheck(typeof(SampleDiagnosticCheck), "litebus.sample.duplicate");

        configuration.DiagnosticChecks.Should().HaveCount(1);
        configuration.DiagnosticChecks[0].Name.Should().Be("litebus.sample");
    }

    [Fact]
    public void AddLiteBus_ShouldRegisterLiteBusHostManifestWithDiagnosticChecks()
    {
        var services = new ServiceCollection();

        services.AddLiteBus(registry =>
        {
            registry.AddMessageModule(_ => { });
                registry.AddInboxModule(inbox =>
                {
                    inbox.UseInMemoryStorage();
                    inbox.AddDiagnosticCheck<SampleDiagnosticCheck>("litebus.sample");
                });
        });

        var manifest = services.BuildServiceProvider().GetRequiredService<LiteBusHostManifest>();

        manifest.DiagnosticChecks.Should().ContainSingle();
        manifest.DiagnosticChecks[0].ImplementationType.Should().Be(typeof(SampleDiagnosticCheck));
        manifest.DiagnosticChecks[0].Name.Should().Be("litebus.sample");
    }

    private sealed class SampleDiagnosticCheck : IDiagnosticCheck
    {
        public string Name => "litebus.sample";

        public Task<DiagnosticResult> CheckAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new DiagnosticResult(DiagnosticStatus.Healthy, "Sample probe succeeded."));
        }
    }
}
