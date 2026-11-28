using LiteBus.Commands.Abstractions;
using LiteBus.Events.Abstractions;
using LiteBus.Inbox.Abstractions;
using LiteBus.Messaging.Abstractions;
using LiteBus.Outbox.Abstractions;
using LiteBus.Queries.Abstractions;
using LiteBus.Transport.Abstractions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;

namespace LiteBus.Analyzers.UnitTests;

/// <summary>
///     Tests for cross-assembly durable contract registration in
///     <see cref="MissingMessageContractRegistrationAnalyzer" /> and
///     <see cref="ExplicitMessageContractRegistrationAnalyzer" />.
/// </summary>
public sealed class CrossAssemblyContractRegistrationTests
{
    /// <summary>
    ///     Verifies an attributed message in an assembly that names another registering assembly produces no LB1017.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task AttributedMessageInAssemblyNamingAnotherAssembly_ProducesNoDiagnostic()
    {
        const string source = """
                              using LiteBus.Commands.Abstractions;
                              using LiteBus.Messaging.Abstractions;

                              [assembly: MessageContractsRegisteredIn("LedgerModule")]

                              [MessageContract("payments.process-payment", 1)]
                              public sealed record ProcessPaymentCommand(int PaymentId) : ICommand;
                              """;

        return AnalyzerTest.VerifyNoDiagnosticsAsync<ExplicitMessageContractRegistrationAnalyzer>(source);
    }

    /// <summary>
    ///     Verifies an attributed message in an assembly that names the analyzed compilation still produces LB1017.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task AttributedMessageInAssemblyNamingCurrentCompilation_ProducesDiagnostic()
    {
        const string source = """
                              using LiteBus.Commands.Abstractions;
                              using LiteBus.Messaging.Abstractions;

                              [assembly: MessageContractsRegisteredIn("TestProject")]

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
    ///     Verifies an unregistered referenced contract that names the analyzed compilation produces an unlocated LB1017.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task UnregisteredReferencedContractNamingCurrentCompilation_ProducesUnlocatedDiagnostic()
    {
        const string contractsSource = """
                                       using LiteBus.Commands.Abstractions;
                                       using LiteBus.Messaging.Abstractions;

                                       [assembly: MessageContractsRegisteredIn("TestProject")]

                                       [MessageContract("payments.process-payment", 1)]
                                       public sealed record ProcessPaymentCommand(int PaymentId) : ICommand;
                                       """;

        const string source = """
                              public sealed class Placeholder
                              {
                              }
                              """;

        var contractsReference = CompileContracts("ContractsAssembly", contractsSource);

        return AnalyzerTest.VerifyDiagnosticWithReferencesAsync<ExplicitMessageContractRegistrationAnalyzer>(
            source,
            DiagnosticDescriptors.ExplicitMessageContractRegistration,
            ["ProcessPaymentCommand"],
            contractsReference);
    }

    /// <summary>
    ///     Verifies explicit registration satisfies LB1017 for a referenced contract that names the analyzed compilation.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task ReferencedContractNamingCurrentCompilationWithExplicitRegister_ProducesNoDiagnostic()
    {
        const string contractsSource = """
                                       using LiteBus.Commands.Abstractions;
                                       using LiteBus.Messaging.Abstractions;

                                       [assembly: MessageContractsRegisteredIn("TestProject")]

                                       [MessageContract("payments.process-payment", 1)]
                                       public sealed record ProcessPaymentCommand(int PaymentId) : ICommand;
                                       """;

        const string source = """
                              using LiteBus.Messaging.Abstractions;

                              public static class ModuleConfiguration
                              {
                                  public static void Configure(MessageContractBuilder contracts)
                                  {
                                      contracts.Register<ProcessPaymentCommand>("payments.process-payment", 1);
                                  }
                              }
                              """;

        var contractsReference = CompileContracts("ContractsAssembly", contractsSource);

        return AnalyzerTest.VerifyNoDiagnosticsWithReferencesAsync<ExplicitMessageContractRegistrationAnalyzer>(
            source,
            contractsReference);
    }

    /// <summary>
    ///     Verifies an assembly scan satisfies LB1017 for a referenced contract that names the analyzed compilation.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task ReferencedContractNamingCurrentCompilationWithAssemblyScan_ProducesNoDiagnostic()
    {
        const string contractsSource = """
                                       using LiteBus.Commands.Abstractions;
                                       using LiteBus.Messaging.Abstractions;

                                       [assembly: MessageContractsRegisteredIn("TestProject")]

                                       [MessageContract("payments.process-payment", 1)]
                                       public sealed record ProcessPaymentCommand(int PaymentId) : ICommand;
                                       """;

        const string source = """
                              public static class ModuleConfiguration
                              {
                                  public static void Configure(ContractsRegistry contracts)
                                  {
                                      contracts.RegisterFromAssembly(typeof(ProcessPaymentCommand).Assembly);
                                  }
                              }

                              public sealed class ContractsRegistry
                              {
                                  public void RegisterFromAssembly(System.Reflection.Assembly assembly)
                                  {
                                  }
                              }
                              """;

        var contractsReference = CompileContracts("ContractsAssembly", contractsSource);

        return AnalyzerTest.VerifyNoDiagnosticsWithReferencesAsync<ExplicitMessageContractRegistrationAnalyzer>(
            source,
            contractsReference);
    }

    /// <summary>
    ///     Verifies a referenced contract that names another assembly produces no LB1017 in the analyzed compilation.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task ReferencedContractNamingAnotherAssembly_ProducesNoDiagnostic()
    {
        const string contractsSource = """
                                       using LiteBus.Commands.Abstractions;
                                       using LiteBus.Messaging.Abstractions;

                                       [assembly: MessageContractsRegisteredIn("LedgerModule")]

                                       [MessageContract("payments.process-payment", 1)]
                                       public sealed record ProcessPaymentCommand(int PaymentId) : ICommand;
                                       """;

        const string source = """
                              public sealed class Placeholder
                              {
                              }
                              """;

        var contractsReference = CompileContracts("ContractsAssembly", contractsSource);

        return AnalyzerTest.VerifyNoDiagnosticsWithReferencesAsync<ExplicitMessageContractRegistrationAnalyzer>(
            source,
            contractsReference);
    }

    /// <summary>
    ///     Verifies a referenced contract without a registering assembly attribute produces no LB1017 in the analyzed compilation.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task ReferencedContractWithoutRegisteringAssemblyAttribute_ProducesNoDiagnostic()
    {
        const string contractsSource = """
                                       using LiteBus.Commands.Abstractions;
                                       using LiteBus.Messaging.Abstractions;

                                       [MessageContract("payments.process-payment", 1)]
                                       public sealed record ProcessPaymentCommand(int PaymentId) : ICommand;
                                       """;

        const string source = """
                              public sealed class Placeholder
                              {
                              }
                              """;

        var contractsReference = CompileContracts("ContractsAssembly", contractsSource);

        return AnalyzerTest.VerifyNoDiagnosticsWithReferencesAsync<ExplicitMessageContractRegistrationAnalyzer>(
            source,
            contractsReference);
    }

    /// <summary>
    ///     Verifies an attributed type that is not a durable message produces no LB1017 in the naming compilation.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task NonDurableReferencedContractNamingCurrentCompilation_ProducesNoDiagnostic()
    {
        const string contractsSource = """
                                       using LiteBus.Messaging.Abstractions;

                                       [assembly: MessageContractsRegisteredIn("TestProject")]

                                       [MessageContract("reports.daily", 1)]
                                       public sealed record DailyReport(int Day);
                                       """;

        const string source = """
                              public sealed class Placeholder
                              {
                              }
                              """;

        var contractsReference = CompileContracts("ContractsAssembly", contractsSource);

        return AnalyzerTest.VerifyNoDiagnosticsWithReferencesAsync<ExplicitMessageContractRegistrationAnalyzer>(
            source,
            contractsReference);
    }

    /// <summary>
    ///     Verifies every unregistered referenced contract that names the analyzed compilation produces an unlocated LB1017.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task TwoUnregisteredReferencedContractsNamingCurrentCompilation_ProduceTwoUnlocatedDiagnostics()
    {
        const string contractsSource = """
                                       using LiteBus.Commands.Abstractions;
                                       using LiteBus.Messaging.Abstractions;

                                       [assembly: MessageContractsRegisteredIn("TestProject")]

                                       [MessageContract("payments.process-payment", 1)]
                                       public sealed record ProcessPaymentCommand(int PaymentId) : ICommand;

                                       [MessageContract("payments.cancel-payment", 1)]
                                       public sealed record CancelPaymentCommand(int PaymentId) : ICommand;
                                       """;

        const string source = """
                              public sealed class Placeholder
                              {
                              }
                              """;

        var contractsReference = CompileContracts("ContractsAssembly", contractsSource);

        return VerifyWithReferencesAsync<ExplicitMessageContractRegistrationAnalyzer>(
            source,
            [contractsReference],
            (DiagnosticDescriptors.ExplicitMessageContractRegistration, null, ["CancelPaymentCommand"]),
            (DiagnosticDescriptors.ExplicitMessageContractRegistration, null, ["ProcessPaymentCommand"]));
    }

    /// <summary>
    ///     Verifies a handler of a message registered in another assembly produces no LB1007.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task HandlerOfMessageRegisteredInAnotherAssembly_ProducesNoDiagnostic()
    {
        const string contractsSource = """
                                       using LiteBus.Commands.Abstractions;
                                       using LiteBus.Messaging.Abstractions;

                                       [assembly: MessageContractsRegisteredIn("LedgerModule")]

                                       public sealed record ProcessPaymentCommand(int PaymentId) : ICommand;
                                       """;

        const string source = """
                              using System.Threading;
                              using System.Threading.Tasks;
                              using LiteBus.Commands.Abstractions;

                              public sealed class ProcessPaymentCommandHandler : ICommandHandler<ProcessPaymentCommand>
                              {
                                  public Task HandleAsync(ProcessPaymentCommand command, CancellationToken cancellationToken = default)
                                      => Task.CompletedTask;
                              }
                              """;

        var contractsReference = CompileContracts("ContractsAssembly", contractsSource);

        return AnalyzerTest.VerifyNoDiagnosticsWithReferencesAsync<MissingMessageContractRegistrationAnalyzer>(
            source,
            contractsReference);
    }

    /// <summary>
    ///     Verifies a handler of a referenced message without a registering assembly attribute still produces LB1007.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task HandlerOfMessageWithoutRegisteringAssemblyAttribute_ProducesDiagnostic()
    {
        const string contractsSource = """
                                       using LiteBus.Commands.Abstractions;

                                       public sealed record ProcessPaymentCommand(int PaymentId) : ICommand;
                                       """;

        const string source = """
                              using System.Threading;
                              using System.Threading.Tasks;
                              using LiteBus.Commands.Abstractions;

                              public sealed class {|#0:ProcessPaymentCommandHandler|} : ICommandHandler<ProcessPaymentCommand>
                              {
                                  public Task HandleAsync(ProcessPaymentCommand command, CancellationToken cancellationToken = default)
                                      => Task.CompletedTask;
                              }
                              """;

        var contractsReference = CompileContracts("ContractsAssembly", contractsSource);

        return VerifyWithReferencesAsync<MissingMessageContractRegistrationAnalyzer>(
            source,
            [contractsReference],
            (DiagnosticDescriptors.MissingMessageContractRegistration, 0, ["ProcessPaymentCommand", "ProcessPaymentCommandHandler", "ProcessPaymentCommand"]));
    }

    /// <summary>
    ///     Verifies a handler in the named registering compilation without registration still produces LB1007.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task HandlerOfMessageRegisteredInCurrentCompilationWithoutRegistration_ProducesDiagnostic()
    {
        const string contractsSource = """
                                       using LiteBus.Commands.Abstractions;
                                       using LiteBus.Messaging.Abstractions;

                                       [assembly: MessageContractsRegisteredIn("TestProject")]

                                       public sealed record ProcessPaymentCommand(int PaymentId) : ICommand;
                                       """;

        const string source = """
                              using System.Threading;
                              using System.Threading.Tasks;
                              using LiteBus.Commands.Abstractions;

                              public sealed class {|#0:ProcessPaymentCommandHandler|} : ICommandHandler<ProcessPaymentCommand>
                              {
                                  public Task HandleAsync(ProcessPaymentCommand command, CancellationToken cancellationToken = default)
                                      => Task.CompletedTask;
                              }
                              """;

        var contractsReference = CompileContracts("ContractsAssembly", contractsSource);

        return VerifyWithReferencesAsync<MissingMessageContractRegistrationAnalyzer>(
            source,
            [contractsReference],
            (DiagnosticDescriptors.MissingMessageContractRegistration, 0, ["ProcessPaymentCommand", "ProcessPaymentCommandHandler", "ProcessPaymentCommand"]));
    }

    /// <summary>
    ///     Verifies explicit registration satisfies LB1007 for a handler in the named registering compilation.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task HandlerOfMessageRegisteredInCurrentCompilationWithExplicitRegister_ProducesNoDiagnostic()
    {
        const string contractsSource = """
                                       using LiteBus.Commands.Abstractions;
                                       using LiteBus.Messaging.Abstractions;

                                       [assembly: MessageContractsRegisteredIn("TestProject")]

                                       public sealed record ProcessPaymentCommand(int PaymentId) : ICommand;
                                       """;

        const string source = """
                              using System.Threading;
                              using System.Threading.Tasks;
                              using LiteBus.Commands.Abstractions;
                              using LiteBus.Messaging.Abstractions;

                              public sealed class ProcessPaymentCommandHandler : ICommandHandler<ProcessPaymentCommand>
                              {
                                  public Task HandleAsync(ProcessPaymentCommand command, CancellationToken cancellationToken = default)
                                      => Task.CompletedTask;
                              }

                              public static class ModuleConfiguration
                              {
                                  public static void Configure(MessageContractBuilder contracts)
                                  {
                                      contracts.Register<ProcessPaymentCommand>("payments.process-payment", 1);
                                  }
                              }
                              """;

        var contractsReference = CompileContracts("ContractsAssembly", contractsSource);

        return AnalyzerTest.VerifyNoDiagnosticsWithReferencesAsync<MissingMessageContractRegistrationAnalyzer>(
            source,
            contractsReference);
    }

    /// <summary>
    ///     Compiles contracts source into a referenced assembly for cross-assembly analyzer tests.
    /// </summary>
    /// <param name="assemblyName">The emitted assembly name.</param>
    /// <param name="source">The contracts source to compile.</param>
    /// <returns>The metadata reference for the emitted assembly.</returns>
    private static MetadataReference CompileContracts(string assemblyName, string source)
    {
        var commandsReference = MetadataReference.CreateFromFile(typeof(ICommand).Assembly.Location);
        var messagingReference = MetadataReference.CreateFromFile(typeof(MessageContractAttribute).Assembly.Location);

        return AnalyzerTest.CompileToMetadataReference(assemblyName, source, commandsReference, messagingReference);
    }

    /// <summary>
    ///     Verifies analyzer diagnostics for source compiled with additional metadata references.
    /// </summary>
    /// <typeparam name="TAnalyzer">The analyzer type under test.</typeparam>
    /// <param name="source">The source under test.</param>
    /// <param name="references">Additional metadata references for the compilation.</param>
    /// <param name="expectedDiagnostics">The expected diagnostics, empty when the compilation must be clean.</param>
    /// <returns>A task that completes when verification finishes.</returns>
    private static Task VerifyWithReferencesAsync<TAnalyzer>(
        string source,
        IReadOnlyList<MetadataReference> references,
        params (DiagnosticDescriptor Descriptor, int? MarkupLocation, object[] Arguments)[] expectedDiagnostics)
        where TAnalyzer : DiagnosticAnalyzer, new()
    {
        var test = new CSharpAnalyzerTest<TAnalyzer, DefaultVerifier>
        {
            ReferenceAssemblies = AnalyzerTest.Net10ReferenceAssemblies
        };

        test.TestState.Sources.Add(("/0/Test0.cs", source));

        test.TestState.AdditionalReferences.Add(MetadataReference.CreateFromFile(
            typeof(ICommand).Assembly.Location));

        test.TestState.AdditionalReferences.Add(MetadataReference.CreateFromFile(
            typeof(IEvent).Assembly.Location));

        test.TestState.AdditionalReferences.Add(MetadataReference.CreateFromFile(
            typeof(IQuery<>).Assembly.Location));

        test.TestState.AdditionalReferences.Add(MetadataReference.CreateFromFile(
            typeof(IInbox).Assembly.Location));

        test.TestState.AdditionalReferences.Add(MetadataReference.CreateFromFile(
            typeof(IOutbox).Assembly.Location));

        test.TestState.AdditionalReferences.Add(MetadataReference.CreateFromFile(
            typeof(HandlerPriorityAttribute).Assembly.Location));

        test.TestState.AdditionalReferences.Add(MetadataReference.CreateFromFile(
            typeof(MessageContractAttribute).Assembly.Location));

        test.TestState.AdditionalReferences.Add(MetadataReference.CreateFromFile(
            typeof(ITransportPublisher).Assembly.Location));

        foreach (var reference in references)
        {
            test.TestState.AdditionalReferences.Add(reference);
        }

        foreach (var (descriptor, markupLocation, arguments) in expectedDiagnostics)
        {
            var expected = new DiagnosticResult(descriptor).WithArguments(arguments);

            if (markupLocation.HasValue)
            {
                expected = expected.WithLocation(markupLocation.Value);
            }

            test.ExpectedDiagnostics.Add(expected);
        }

        return test.RunAsync(CancellationToken.None);
    }
}
