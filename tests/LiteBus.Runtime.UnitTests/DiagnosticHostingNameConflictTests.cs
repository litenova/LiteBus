using Autofac;
using LiteBus.Runtime.Abstractions.Diagnostics;
using LiteBus.Runtime.Abstractions.Exceptions;
using LiteBus.Runtime.Extensions.Autofac.Hosting;
using LiteBus.Runtime.Extensions.Microsoft.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Runtime.UnitTests;

public sealed class DiagnosticHostingNameConflictTests
{
    [Fact]
    public void MicrosoftRegisterDiagnosticChecks_TwoTypesWithSameName_ShouldThrow()
    {
        var services = new ServiceCollection();

        var act = () => services.RegisterDiagnosticChecks(
        [
            new DiagnosticCheckDescriptor(typeof(FirstDiagnosticCheck), "litebus.shared"),
            new DiagnosticCheckDescriptor(typeof(SecondDiagnosticCheck), "litebus.shared")
        ]);

        act.Should().Throw<ModuleCompositionException>().WithMessage("*litebus.shared*");
    }

    [Fact]
    public void AutofacRegisterDiagnosticChecks_TwoTypesWithSameName_ShouldThrow()
    {
        var builder = new ContainerBuilder();

        var act = () => builder.RegisterDiagnosticChecks(
        [
            new DiagnosticCheckDescriptor(typeof(FirstDiagnosticCheck), "litebus.shared"),
            new DiagnosticCheckDescriptor(typeof(SecondDiagnosticCheck), "litebus.shared")
        ]);

        act.Should().Throw<ModuleCompositionException>().WithMessage("*litebus.shared*");
    }

    private sealed class FirstDiagnosticCheck : IDiagnosticCheck
    {
        public string Name => "litebus.shared";

        public Task<DiagnosticResult> CheckAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new DiagnosticResult(DiagnosticStatus.Healthy, "First probe succeeded."));
        }
    }

    private sealed class SecondDiagnosticCheck : IDiagnosticCheck
    {
        public string Name => "litebus.shared";

        public Task<DiagnosticResult> CheckAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new DiagnosticResult(DiagnosticStatus.Healthy, "Second probe succeeded."));
        }
    }
}
