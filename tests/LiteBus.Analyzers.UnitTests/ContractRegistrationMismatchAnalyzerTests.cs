namespace LiteBus.Analyzers.UnitTests;

/// <summary>
///     Tests for the <see cref="ContractRegistrationMismatchAnalyzer" /> rule.
/// </summary>
public sealed class ContractRegistrationMismatchAnalyzerTests
{
    /// <summary>
    ///     Verifies a registration version that differs from <c>[MessageContract]</c> produces LB1030.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task VersionMismatch_ProducesDiagnostic()
    {
        const string source = """
                              using LiteBus.Commands.Abstractions;
                              using LiteBus.Messaging.Abstractions;

                              [MessageContract("payments.process-payment", 2)]
                              public sealed record ProcessPaymentCommand(int PaymentId) : ICommand;

                              public static class ModuleConfiguration
                              {
                                  public static void Configure(IContractWriter contracts)
                                  {
                                      {|#0:contracts.Register<ProcessPaymentCommand>("payments.process-payment", 1)|};
                                  }
                              }
                              """;

        return AnalyzerTest.VerifyDiagnosticAsync<ContractRegistrationMismatchAnalyzer>(
            source,
            DiagnosticDescriptors.ContractRegistrationMismatch,
            0,
            "ProcessPaymentCommand",
            "payments.process-payment",
            1,
            "payments.process-payment",
            2);
    }

    /// <summary>
    ///     Verifies a registration name that differs from <c>[MessageContract]</c> produces LB1030.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task NameMismatch_ProducesDiagnostic()
    {
        const string source = """
                              using LiteBus.Commands.Abstractions;
                              using LiteBus.Messaging.Abstractions;

                              [MessageContract("payments.process-payment", 1)]
                              public sealed record ProcessPaymentCommand(int PaymentId) : ICommand;

                              public static class ModuleConfiguration
                              {
                                  public static void Configure(IContractWriter contracts)
                                  {
                                      {|#0:contracts.Register<ProcessPaymentCommand>("payments.other-name", 1)|};
                                  }
                              }
                              """;

        return AnalyzerTest.VerifyDiagnosticAsync<ContractRegistrationMismatchAnalyzer>(
            source,
            DiagnosticDescriptors.ContractRegistrationMismatch,
            0,
            "ProcessPaymentCommand",
            "payments.other-name",
            1,
            "payments.process-payment",
            1);
    }

    /// <summary>
    ///     Verifies an omitted version argument is compared with the default version of the attribute.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public async Task OmittedVersion_ComparesWithDefaultVersion()
    {
        const string matching = """
                                using LiteBus.Commands.Abstractions;
                                using LiteBus.Messaging.Abstractions;

                                [MessageContract("payments.process-payment")]
                                public sealed record ProcessPaymentCommand(int PaymentId) : ICommand;

                                public static class ModuleConfiguration
                                {
                                    public static void Configure(IContractWriter contracts)
                                    {
                                        contracts.Register<ProcessPaymentCommand>("payments.process-payment");
                                    }
                                }
                                """;

        await AnalyzerTest.VerifyNoDiagnosticsAsync<ContractRegistrationMismatchAnalyzer>(matching)
            .ConfigureAwait(false);

        const string mismatching = """
                                   using LiteBus.Commands.Abstractions;
                                   using LiteBus.Messaging.Abstractions;

                                   [MessageContract("payments.process-payment", 2)]
                                   public sealed record ProcessPaymentCommand(int PaymentId) : ICommand;

                                   public static class ModuleConfiguration
                                   {
                                       public static void Configure(IContractWriter contracts)
                                       {
                                           {|#0:contracts.Register<ProcessPaymentCommand>("payments.process-payment")|};
                                       }
                                   }
                                   """;

        await AnalyzerTest.VerifyDiagnosticAsync<ContractRegistrationMismatchAnalyzer>(
                mismatching,
                DiagnosticDescriptors.ContractRegistrationMismatch,
                0,
                "ProcessPaymentCommand",
                "payments.process-payment",
                1,
                "payments.process-payment",
                2)
            .ConfigureAwait(false);
    }

    /// <summary>
    ///     Verifies a registration that matches the attribute produces no diagnostic.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task MatchingRegistration_ProducesNoDiagnostic()
    {
        const string source = """
                              using LiteBus.Commands.Abstractions;
                              using LiteBus.Messaging.Abstractions;

                              [MessageContract("payments.process-payment", 1)]
                              public sealed record ProcessPaymentCommand(int PaymentId) : ICommand;

                              public static class ModuleConfiguration
                              {
                                  public static void Configure(IContractWriter contracts)
                                  {
                                      contracts.Register<ProcessPaymentCommand>("payments.process-payment", 1);
                                  }
                              }
                              """;

        return AnalyzerTest.VerifyNoDiagnosticsAsync<ContractRegistrationMismatchAnalyzer>(source);
    }

    /// <summary>
    ///     Verifies a type without <c>[MessageContract]</c> produces no diagnostic.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task TypeWithoutAttribute_ProducesNoDiagnostic()
    {
        const string source = """
                              using LiteBus.Commands.Abstractions;
                              using LiteBus.Messaging.Abstractions;

                              public sealed record ProcessPaymentCommand(int PaymentId) : ICommand;

                              public static class ModuleConfiguration
                              {
                                  public static void Configure(IContractWriter contracts)
                                  {
                                      contracts.Register<ProcessPaymentCommand>("payments.process-payment", 2);
                                  }
                              }
                              """;

        return AnalyzerTest.VerifyNoDiagnosticsAsync<ContractRegistrationMismatchAnalyzer>(source);
    }

    /// <summary>
    ///     Verifies a non-constant version argument produces no diagnostic.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task NonConstantVersion_ProducesNoDiagnostic()
    {
        const string source = """
                              using LiteBus.Commands.Abstractions;
                              using LiteBus.Messaging.Abstractions;

                              [MessageContract("payments.process-payment", 1)]
                              public sealed record ProcessPaymentCommand(int PaymentId) : ICommand;

                              public static class ModuleConfiguration
                              {
                                  public static void Configure(IContractWriter contracts, int version)
                                  {
                                      contracts.Register<ProcessPaymentCommand>("payments.process-payment", version);
                                  }
                              }
                              """;

        return AnalyzerTest.VerifyNoDiagnosticsAsync<ContractRegistrationMismatchAnalyzer>(source);
    }
}
