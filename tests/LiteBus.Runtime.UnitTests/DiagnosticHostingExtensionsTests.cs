using Autofac;
using LiteBus.Runtime.Abstractions.Diagnostics;
using LiteBus.Runtime.Abstractions.Exceptions;
using LiteBus.Runtime.Extensions.Autofac.Hosting;
using LiteBus.Runtime.Extensions.Microsoft.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Runtime.UnitTests;

public sealed class DiagnosticHostingExtensionsTests
{
    [Fact]
    public void MicrosoftRegisterDiagnosticChecks_SameTypeWithDifferentName_ShouldThrow()
    {
        var services = new ServiceCollection();

        var act = () => services.RegisterDiagnosticChecks(
        [
            new DiagnosticCheckDescriptor(typeof(SampleDiagnosticCheck), "litebus.sample"),
            new DiagnosticCheckDescriptor(typeof(SampleDiagnosticCheck), "litebus.other")
        ]);

        act.Should().Throw<ModuleCompositionException>().WithMessage("*litebus.sample*litebus.other*");
    }

    [Fact]
    public void MicrosoftRegisterDiagnosticChecks_SameTypeAndNameTwice_ShouldRegisterOnce()
    {
        var services = new ServiceCollection();

        services.RegisterDiagnosticChecks(
        [
            new DiagnosticCheckDescriptor(typeof(SampleDiagnosticCheck), "litebus.sample"),
            new DiagnosticCheckDescriptor(typeof(SampleDiagnosticCheck), "litebus.sample")
        ]);

        services.Count(descriptor => descriptor.ServiceType == typeof(SampleDiagnosticCheck)).Should().Be(1);
    }

    [Fact]
    public void AutofacRegisterDiagnosticChecks_SameTypeWithDifferentName_ShouldThrow()
    {
        var builder = new ContainerBuilder();

        var act = () => builder.RegisterDiagnosticChecks(
        [
            new DiagnosticCheckDescriptor(typeof(SampleDiagnosticCheck), "litebus.sample"),
            new DiagnosticCheckDescriptor(typeof(SampleDiagnosticCheck), "litebus.other")
        ]);

        act.Should().Throw<ModuleCompositionException>().WithMessage("*litebus.sample*litebus.other*");
    }

    [Fact]
    public void AutofacRegisterDiagnosticChecks_SameTypeAndNameTwice_ShouldRegisterOnce()
    {
        var builder = new ContainerBuilder();

        builder.RegisterDiagnosticChecks(
        [
            new DiagnosticCheckDescriptor(typeof(SampleDiagnosticCheck), "litebus.sample"),
            new DiagnosticCheckDescriptor(typeof(SampleDiagnosticCheck), "litebus.sample")
        ]);

        using var container = builder.Build();

        container.Resolve<SampleDiagnosticCheck>().Should().NotBeNull();
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
