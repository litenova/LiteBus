using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace LiteBus.Analyzers.UnitTests;

/// <summary>
///     Compilation-end tests for the durable contract registration analyzers.
/// </summary>
public sealed class ContractRegistrationCompilationEndTests
{
    /// <summary>
    ///     Verifies that a handled message without contract registration is reported across two files.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public async Task HandledMessageWithoutContractRegistrationAcrossTwoFiles_ProducesDiagnostic()
    {
        const string file0 = """
                             using LiteBus.Commands.Abstractions;

                             public sealed record ProcessPaymentCommand(int PaymentId) : ICommand;
                             """;

        const string file1 = """
                             using System.Threading;
                             using System.Threading.Tasks;
                             using LiteBus.Commands.Abstractions;

                             public sealed class {|#0:ProcessPaymentCommandHandler|} : ICommandHandler<ProcessPaymentCommand>
                             {
                                 public Task HandleAsync(ProcessPaymentCommand command, CancellationToken cancellationToken = default)
                                     => Task.CompletedTask;
                             }
                             """;

        await AnalyzerTest.VerifyMultiFileAsync<MissingMessageContractRegistrationAnalyzer>(
            [file0, file1],
            (DiagnosticDescriptors.MissingMessageContractRegistration, 0, ["ProcessPaymentCommand", "ProcessPaymentCommandHandler", "ProcessPaymentCommand"])).ConfigureAwait(false);
    }

    /// <summary>
    ///     Verifies that a registration call in another file satisfies durable contract registration.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public async Task MessageRegisteredInAnotherFile_ProducesNoDiagnostic()
    {
        const string file0 = """
                             using System.Threading;
                             using System.Threading.Tasks;
                             using LiteBus.Commands.Abstractions;

                             public sealed record ProcessPaymentCommand(int PaymentId) : ICommand;

                             public sealed class ProcessPaymentCommandHandler : ICommandHandler<ProcessPaymentCommand>
                             {
                                 public Task HandleAsync(ProcessPaymentCommand command, CancellationToken cancellationToken = default)
                                     => Task.CompletedTask;
                             }
                             """;

        const string file1 = """
                             using LiteBus.Commands.Abstractions;

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

                                 public void RegisterFromAssembly(System.Reflection.Assembly assembly)
                                 {
                                 }
                             }
                             """;

        await AnalyzerTest.VerifyMultiFileAsync<MissingMessageContractRegistrationAnalyzer>(
            [file0, file1]).ConfigureAwait(false);
    }

    /// <summary>
    ///     Verifies that per-file analysis reports no missing contract registration while full analysis does.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public async Task MissingMessageContractRegistration_PerFileAnalysisReportsNothingWhileFullAnalysisReports()
    {
        const string file0 = """
                             using System.Threading;
                             using System.Threading.Tasks;
                             using LiteBus.Commands.Abstractions;

                             public sealed record ProcessPaymentCommand(int PaymentId) : ICommand;

                             public sealed class ProcessPaymentCommandHandler : ICommandHandler<ProcessPaymentCommand>
                             {
                                 public Task HandleAsync(ProcessPaymentCommand command, CancellationToken cancellationToken = default)
                                     => Task.CompletedTask;
                             }
                             """;

        const string file1 = """
                             public sealed class Placeholder
                             {
                             }
                             """;

        const string marked = """
                              using System.Threading;
                              using System.Threading.Tasks;
                              using LiteBus.Commands.Abstractions;

                              public sealed record ProcessPaymentCommand(int PaymentId) : ICommand;

                              public sealed class {|#0:ProcessPaymentCommandHandler|} : ICommandHandler<ProcessPaymentCommand>
                              {
                                  public Task HandleAsync(ProcessPaymentCommand command, CancellationToken cancellationToken = default)
                                      => Task.CompletedTask;
                              }
                              """;

        var compilation = await AnalyzerTest.CreateCompilationAsync([file0, file1]).ConfigureAwait(false);

        var perFile = await AnalyzerTest.GetPerFileDiagnosticsAsync(
            compilation,
            0,
            new MissingMessageContractRegistrationAnalyzer()).ConfigureAwait(false);

        Assert.DoesNotContain(perFile, diagnostic => diagnostic.Id == DiagnosticIds.MissingMessageContractRegistration);

        await AnalyzerTest.VerifyMultiFileAsync<MissingMessageContractRegistrationAnalyzer>(
            [marked, file1],
            (DiagnosticDescriptors.MissingMessageContractRegistration, 0, ["ProcessPaymentCommand", "ProcessPaymentCommandHandler", "ProcessPaymentCommand"])).ConfigureAwait(false);
    }

    /// <summary>
    ///     Verifies that an attributed message is reported when only an unrelated registration lives in another file.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public async Task AttributedMessageWithUnrelatedRegistrationAcrossTwoFiles_ProducesDiagnostic()
    {
        const string file0 = """
                             using LiteBus.Commands.Abstractions;
                             using LiteBus.Messaging.Abstractions;

                             [MessageContract("payments.process-payment", 1)]
                             public sealed record {|#0:ProcessPaymentCommand|}(int PaymentId) : ICommand;
                             """;

        const string file1 = """
                             public sealed record UnrelatedMessage(int Value);

                             public static class ModuleConfiguration
                             {
                                 public static void Configure(ContractsRegistry contracts)
                                 {
                                     contracts.Register<UnrelatedMessage>("unrelated", 1);
                                 }
                             }

                             public sealed class ContractsRegistry
                             {
                                 public void Register<T>(string name, int version)
                                 {
                                 }

                                 public void RegisterFromAssembly(System.Reflection.Assembly assembly)
                                 {
                                 }
                             }
                             """;

        await AnalyzerTest.VerifyMultiFileAsync<ExplicitMessageContractRegistrationAnalyzer>(
            [file0, file1],
            (DiagnosticDescriptors.ExplicitMessageContractRegistration, 0, ["ProcessPaymentCommand"])).ConfigureAwait(false);
    }

    /// <summary>
    ///     Verifies that an explicit registration in another file satisfies the explicit registration rule.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public async Task AttributedMessageRegisteredInAnotherFile_ProducesNoDiagnostic()
    {
        const string file0 = """
                             using LiteBus.Commands.Abstractions;
                             using LiteBus.Messaging.Abstractions;

                             [MessageContract("payments.process-payment", 1)]
                             public sealed record ProcessPaymentCommand(int PaymentId) : ICommand;
                             """;

        const string file1 = """
                             using LiteBus.Commands.Abstractions;

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

                                 public void RegisterFromAssembly(System.Reflection.Assembly assembly)
                                 {
                                 }
                             }
                             """;

        await AnalyzerTest.VerifyMultiFileAsync<ExplicitMessageContractRegistrationAnalyzer>(
            [file0, file1]).ConfigureAwait(false);
    }

    /// <summary>
    ///     Verifies that per-file analysis reports no explicit registration diagnostic while full analysis does.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public async Task ExplicitMessageContractRegistration_PerFileAnalysisReportsNothingWhileFullAnalysisReports()
    {
        const string file0 = """
                             using LiteBus.Commands.Abstractions;
                             using LiteBus.Messaging.Abstractions;

                             [MessageContract("payments.process-payment", 1)]
                             public sealed record ProcessPaymentCommand(int PaymentId) : ICommand;
                             """;

        const string file1 = """
                             public sealed class Placeholder
                             {
                             }
                             """;

        const string marked = """
                              using LiteBus.Commands.Abstractions;
                              using LiteBus.Messaging.Abstractions;

                              [MessageContract("payments.process-payment", 1)]
                              public sealed record {|#0:ProcessPaymentCommand|}(int PaymentId) : ICommand;
                              """;

        var compilation = await AnalyzerTest.CreateCompilationAsync([file0, file1]).ConfigureAwait(false);

        var perFile = await AnalyzerTest.GetPerFileDiagnosticsAsync(
            compilation,
            0,
            new ExplicitMessageContractRegistrationAnalyzer()).ConfigureAwait(false);

        Assert.DoesNotContain(perFile, diagnostic => diagnostic.Id == DiagnosticIds.ExplicitMessageContractRegistration);

        await AnalyzerTest.VerifyMultiFileAsync<ExplicitMessageContractRegistrationAnalyzer>(
            [marked, file1],
            (DiagnosticDescriptors.ExplicitMessageContractRegistration, 0, ["ProcessPaymentCommand"])).ConfigureAwait(false);
    }

    /// <summary>
    ///     Verifies that a partial attributed message with two declarations reports twice at the first declaration.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public async Task PartialAttributedMessageAcrossTwoFiles_ReportsTwiceAtFirstDeclaration()
    {
        const string file0 = """
                             using LiteBus.Commands.Abstractions;

                             public sealed partial record ProcessPaymentCommand(int PaymentId) : ICommand;
                             """;

        const string file1 = """
                             using LiteBus.Messaging.Abstractions;

                             [MessageContract("payments.process-payment", 1)]
                             public sealed partial record ProcessPaymentCommand;
                             """;

        var compilation = await AnalyzerTest.CreateCompilationAsync([file0, file1]).ConfigureAwait(false);

        var diagnostics = await compilation
            .WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new ExplicitMessageContractRegistrationAnalyzer()))
            .GetAnalyzerDiagnosticsAsync()
            .ConfigureAwait(false);

        Assert.Equal(2, diagnostics.Length);

        foreach (var diagnostic in diagnostics)
        {
            Assert.Equal(DiagnosticIds.ExplicitMessageContractRegistration, diagnostic.Id);
            Assert.Equal("/0/Test0.cs", diagnostic.Location.SourceTree?.FilePath);
        }

        Assert.Equal(diagnostics[0].Location.SourceSpan, diagnostics[1].Location.SourceSpan);
    }
}
