using System.Threading.Tasks;
using LiteBus.Commands.Abstractions;
using LiteBus.Messaging.Abstractions;
using Xunit;

namespace LiteBus.Analyzers.Tests;

/// <summary>
///     Tests for the <see cref="ExplicitMessageContractRegistrationAnalyzer" /> rule.
/// </summary>
public sealed class ExplicitMessageContractRegistrationAnalyzerTests
{
    /// <summary>
    ///     Verifies attributed durable messages without explicit registration produce LB1017.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task AttributedMessageWithoutExplicitRegistration_ProducesDiagnostic()
    {
        const string source = """
                              using LiteBus.Commands.Abstractions;
                              using LiteBus.Messaging.Abstractions;

                              [MessageContract("payments.process-payment", 1)]
                              public sealed record {|#0:ProcessPaymentCommand|}(int PaymentId) : ICommand;
                              """;

        return AnalyzerTest.VerifyDiagnosticAsync<ExplicitMessageContractRegistrationAnalyzer>(
            source,
            DiagnosticDescriptors.ExplicitMessageContractRegistration,
            0,
            "ProcessPaymentCommand");
    }

    /// <summary>
    ///     Verifies explicit <c>Contracts.Register&lt;T&gt;</c> satisfies the explicit registration rule.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task AttributedMessageWithExplicitRegister_ProducesNoDiagnostic()
    {
        const string source = """
                              using LiteBus.Commands.Abstractions;
                              using LiteBus.Messaging.Abstractions;

                              [MessageContract("payments.process-payment", 1)]
                              public sealed record ProcessPaymentCommand(int PaymentId) : ICommand;

                              public static class ModuleConfiguration
                              {
                                  public static void Configure(ContractsRegistry contracts)
                                  {
                                      contracts.Register<ProcessPaymentCommand>("payments.process-payment", 1);
                                  }
                              }

                              public sealed class ContractsRegistry
                              {
                                  public void Register<T>(string name, int version)
                                  {
                                  }
                              }
                              """;

        return AnalyzerTest.VerifyNoDiagnosticsAsync<ExplicitMessageContractRegistrationAnalyzer>(source);
    }

    /// <summary>
    ///     Verifies <c>RegisterFromAssembly</c> satisfies the explicit registration rule for attributed types.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task AttributedMessageCoveredByRegisterFromAssembly_ProducesNoDiagnostic()
    {
        const string source = """
                              using System.Reflection;
                              using LiteBus.Commands.Abstractions;
                              using LiteBus.Messaging.Abstractions;

                              [MessageContract("payments.process-payment", 1)]
                              public sealed record ProcessPaymentCommand(int PaymentId) : ICommand;

                              public static class ModuleConfiguration
                              {
                                  public static void Configure(ContractsRegistry contracts)
                                  {
                                      contracts.RegisterFromAssembly(typeof(ProcessPaymentCommand).Assembly);
                                  }
                              }

                              public sealed class ContractsRegistry
                              {
                                  public void RegisterFromAssembly(Assembly assembly)
                                  {
                                  }
                              }
                              """;

        return AnalyzerTest.VerifyNoDiagnosticsAsync<ExplicitMessageContractRegistrationAnalyzer>(source);
    }
}
