using LiteBus.Runtime.Abstractions.Diagnostics;
using LiteBus.Runtime.Abstractions.Exceptions;
using LiteBus.Runtime.Extensions.Microsoft.DependencyInjection;
using LiteBus.Runtime.Modules;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Runtime.UnitTests;

public sealed class ModuleConfigurationDiagnosticNameConflictTests
{
    [Fact]
    public void RegisterDiagnosticCheck_TwoTypesWithSameName_ShouldThrowModuleCompositionException()
    {
        var configuration = new ModuleConfiguration(new MicrosoftDependencyRegistryAdapter(new ServiceCollection()));
        configuration.RegisterDiagnosticCheck(typeof(FirstDiagnosticCheck), "litebus.shared");

        var act = () => configuration.RegisterDiagnosticCheck(typeof(SecondDiagnosticCheck), "litebus.shared");

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
