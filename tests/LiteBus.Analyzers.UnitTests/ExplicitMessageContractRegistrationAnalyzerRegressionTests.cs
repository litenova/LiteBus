namespace LiteBus.Analyzers.UnitTests;

/// <summary>
///     Regression tests for the <see cref="ExplicitMessageContractRegistrationAnalyzer" /> rule.
/// </summary>
public sealed class ExplicitMessageContractRegistrationAnalyzerRegressionTests
{
    /// <summary>
    ///     Verifies an unrelated <c>Register</c> method does not satisfy the explicit registration rule.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task AttributedMessageWithUnrelatedRegister_ProducesDiagnostic()
    {
        const string source = """
                              using LiteBus.Commands.Abstractions;
                              using LiteBus.Messaging.Abstractions;

                              [MessageContract("payments.process-payment", 1)]
                              public sealed record {|#0:ProcessPaymentCommand|}(int PaymentId) : ICommand;

                              public static class ModuleConfiguration
                              {
                                  public static void Configure(UnrelatedRegistry registry)
                                  {
                                      registry.Register<ProcessPaymentCommand>("payments.process-payment", 1);
                                  }
                              }

                              public sealed class UnrelatedRegistry
                              {
                                  public void Register<T>(string name, int version)
                                  {
                                  }
                              }
                              """;

        return AnalyzerTest.VerifyDiagnosticAsync<ExplicitMessageContractRegistrationAnalyzer>(
            source,
            DiagnosticDescriptors.ExplicitMessageContractRegistration,
            0,
            "ProcessPaymentCommand");
    }

    /// <summary>
    ///     Verifies that <c>Register</c> on a same-shaped handler registry with an assembly scan does not satisfy the explicit registration rule.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task AttributedMessageRegisteredOnlyThroughScanShapedRegistry_ProducesDiagnostic()
    {
        const string source = """
                              using LiteBus.Commands.Abstractions;
                              using LiteBus.Messaging.Abstractions;

                              [MessageContract("payments.process-payment", 1)]
                              public sealed record {|#0:ProcessPaymentCommand|}(int PaymentId) : ICommand;

                              public static class HandlerConfiguration
                              {
                                  public static void Configure(HandlerRegistry registry)
                                  {
                                      registry.Register<ProcessPaymentCommand>();
                                  }
                              }

                              public sealed class HandlerRegistry
                              {
                                  public void Register<T>()
                                  {
                                  }

                                  public void RegisterFromAssembly(System.Reflection.Assembly assembly)
                                  {
                                  }
                              }
                              """;

        return AnalyzerTest.VerifyDiagnosticAsync<ExplicitMessageContractRegistrationAnalyzer>(
            source,
            DiagnosticDescriptors.ExplicitMessageContractRegistration,
            0,
            "ProcessPaymentCommand");
    }
}
