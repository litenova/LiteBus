namespace LiteBus.Analyzers.UnitTests;

/// <summary>
///     Tests for the <see cref="UnattributedContractAnalyzer" /> rule.
/// </summary>
public sealed class UnattributedContractAnalyzerTests
{
    /// <summary>
    ///     Verifies that an unattributed command in an assembly that names a registering assembly produces LB1029.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task UnattributedCommandInRegisteredAssembly_ProducesDiagnostic()
    {
        const string source = """
                              using LiteBus.Commands.Abstractions;
                              using LiteBus.Messaging.Abstractions;

                              [assembly: MessageContractsRegisteredIn("LedgerModule")]

                              public sealed record {|#0:ProcessPaymentCommand|}(int PaymentId) : ICommand;
                              """;

        return AnalyzerTest.VerifyDiagnosticAsync<UnattributedContractAnalyzer>(
            source,
            DiagnosticDescriptors.UnattributedContractInRegisteredAssembly,
            0,
            "ProcessPaymentCommand",
            "LedgerModule");
    }

    /// <summary>
    ///     Verifies that an attributed command in an assembly that names a registering assembly produces no diagnostic.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task AttributedCommandInRegisteredAssembly_ProducesNoDiagnostic()
    {
        const string source = """
                              using LiteBus.Commands.Abstractions;
                              using LiteBus.Messaging.Abstractions;

                              [assembly: MessageContractsRegisteredIn("LedgerModule")]

                              [MessageContract("payments.process-payment", 1)]
                              public sealed record ProcessPaymentCommand(int PaymentId) : ICommand;
                              """;

        return AnalyzerTest.VerifyNoDiagnosticsAsync<UnattributedContractAnalyzer>(source);
    }

    /// <summary>
    ///     Verifies that an unattributed event in an assembly that names a registering assembly produces LB1029.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task UnattributedEventInRegisteredAssembly_ProducesDiagnostic()
    {
        const string source = """
                              using LiteBus.Events.Abstractions;
                              using LiteBus.Messaging.Abstractions;

                              [assembly: MessageContractsRegisteredIn("LedgerModule")]

                              public sealed record {|#0:OrderShipped|}(int OrderId) : IEvent;
                              """;

        return AnalyzerTest.VerifyDiagnosticAsync<UnattributedContractAnalyzer>(
            source,
            DiagnosticDescriptors.UnattributedContractInRegisteredAssembly,
            0,
            "OrderShipped",
            "LedgerModule");
    }

    /// <summary>
    ///     Verifies that an unattributed command in an assembly without a registering assembly attribute produces no diagnostic.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task UnattributedCommandWithoutRegisteredAssembly_ProducesNoDiagnostic()
    {
        const string source = """
                              using LiteBus.Commands.Abstractions;

                              public sealed record ProcessPaymentCommand(int PaymentId) : ICommand;
                              """;

        return AnalyzerTest.VerifyNoDiagnosticsAsync<UnattributedContractAnalyzer>(source);
    }

    /// <summary>
    ///     Verifies that an unattributed query in an assembly that names a registering assembly produces no diagnostic, because queries are not durable.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task UnattributedQueryInRegisteredAssembly_ProducesNoDiagnostic()
    {
        const string source = """
                              using LiteBus.Messaging.Abstractions;
                              using LiteBus.Queries.Abstractions;

                              [assembly: MessageContractsRegisteredIn("LedgerModule")]

                              public sealed record GetUserQuery(int UserId) : IQuery<string>;
                              """;

        return AnalyzerTest.VerifyNoDiagnosticsAsync<UnattributedContractAnalyzer>(source);
    }

    /// <summary>
    ///     Verifies that a type that is not a message in an assembly that names a registering assembly produces no diagnostic.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task NonMessageTypeInRegisteredAssembly_ProducesNoDiagnostic()
    {
        const string source = """
                              using LiteBus.Messaging.Abstractions;

                              [assembly: MessageContractsRegisteredIn("LedgerModule")]

                              public sealed record DailyReport(int Day);
                              """;

        return AnalyzerTest.VerifyNoDiagnosticsAsync<UnattributedContractAnalyzer>(source);
    }

    /// <summary>
    ///     Verifies that an unattributed command in an assembly that names two registering assemblies reports both names.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task UnattributedCommandNamingTwoAssemblies_ProducesDiagnosticWithBothNames()
    {
        const string source = """
                              using LiteBus.Commands.Abstractions;
                              using LiteBus.Messaging.Abstractions;

                              [assembly: MessageContractsRegisteredIn("LedgerModule")]
                              [assembly: MessageContractsRegisteredIn("BillingModule")]

                              public sealed record {|#0:ProcessPaymentCommand|}(int PaymentId) : ICommand;
                              """;

        return AnalyzerTest.VerifyDiagnosticAsync<UnattributedContractAnalyzer>(
            source,
            DiagnosticDescriptors.UnattributedContractInRegisteredAssembly,
            0,
            "ProcessPaymentCommand",
            "BillingModule, LedgerModule");
    }

    /// <summary>
    ///     Verifies that an open generic command in an assembly that names a registering assembly produces no diagnostic.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task OpenGenericCommandInRegisteredAssembly_ProducesNoDiagnostic()
    {
        const string source = """
                              using LiteBus.Commands.Abstractions;
                              using LiteBus.Messaging.Abstractions;

                              [assembly: MessageContractsRegisteredIn("LedgerModule")]

                              public sealed record Wrapper<T>(T Value) : ICommand;
                              """;

        return AnalyzerTest.VerifyNoDiagnosticsAsync<UnattributedContractAnalyzer>(source);
    }

    /// <summary>
    ///     Verifies that an abstract command in an assembly that names a registering assembly produces LB1029.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task AbstractCommandInRegisteredAssembly_ProducesDiagnostic()
    {
        const string source = """
                              using LiteBus.Commands.Abstractions;
                              using LiteBus.Messaging.Abstractions;

                              [assembly: MessageContractsRegisteredIn("LedgerModule")]

                              public abstract record {|#0:BaseCommand|} : ICommand;
                              """;

        return AnalyzerTest.VerifyDiagnosticAsync<UnattributedContractAnalyzer>(
            source,
            DiagnosticDescriptors.UnattributedContractInRegisteredAssembly,
            0,
            "BaseCommand",
            "LedgerModule");
    }

    /// <summary>
    ///     Verifies that a partial unattributed command with two declarations reports once at the first declaration.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public async Task PartialUnattributedCommandAcrossTwoFiles_ReportsOnceAtFirstDeclaration()
    {
        const string file0 = """
                             using LiteBus.Commands.Abstractions;

                             public sealed partial record {|#0:ProcessPaymentCommand|}(int PaymentId) : ICommand;
                             """;

        const string file1 = """
                             using LiteBus.Messaging.Abstractions;

                             [assembly: MessageContractsRegisteredIn("LedgerModule")]

                             public sealed partial record ProcessPaymentCommand;
                             """;

        await AnalyzerTest.VerifyMultiFileAsync<UnattributedContractAnalyzer>(
            [file0, file1],
            (DiagnosticDescriptors.UnattributedContractInRegisteredAssembly, 0, ["ProcessPaymentCommand", "LedgerModule"])).ConfigureAwait(false);
    }
}
