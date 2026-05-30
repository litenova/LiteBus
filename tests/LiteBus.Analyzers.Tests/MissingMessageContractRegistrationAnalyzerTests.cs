using System.Threading.Tasks;
using LiteBus.Analyzers;
using LiteBus.Commands.Abstractions;
using LiteBus.Messaging.Abstractions;
using Xunit;

namespace LiteBus.Analyzers.Tests;

/// <summary>
///     Tests for the <see cref="MissingMessageContractRegistrationAnalyzer" /> rule.
/// </summary>
public sealed class MissingMessageContractRegistrationAnalyzerTests
{
    /// <summary>
    ///     Verifies that a message with a contract attribute produces no diagnostic.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task MessageWithContractAttribute_ProducesNoDiagnostic()
    {
        const string source = """
                              using System.Threading;
                              using System.Threading.Tasks;
                              using LiteBus.Analyzers;
                              using LiteBus.Commands.Abstractions;

                              [MessageContract("payments.process-payment", 1)]
                              public sealed record ProcessPaymentCommand(int PaymentId) : ICommand;

                              public sealed class ProcessPaymentCommandHandler : ICommandHandler<ProcessPaymentCommand>
                              {
                                  public Task HandleAsync(ProcessPaymentCommand command, CancellationToken cancellationToken = default)
                                      => Task.CompletedTask;
                              }
                              """;

        return AnalyzerTest.VerifyNoDiagnosticsAsync<MissingMessageContractRegistrationAnalyzer>(source);
    }

    /// <summary>
    ///     Verifies that a handled message without contract registration produces LB1007.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task HandledMessageWithoutContractRegistration_ProducesDiagnostic()
    {
        const string source = """
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

        return AnalyzerTest.VerifyDiagnosticAsync<MissingMessageContractRegistrationAnalyzer>(
            source,
            DiagnosticDescriptors.MissingMessageContractRegistration,
            0,
            "ProcessPaymentCommand",
            "ProcessPaymentCommandHandler");
    }
}
