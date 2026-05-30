using System.Threading.Tasks;
using LiteBus.Analyzers;
using LiteBus.Commands.Abstractions;
using LiteBus.Events.Abstractions;
using LiteBus.Queries.Abstractions;
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

    /// <summary>
    ///     Verifies that <c>Contracts.Register&lt;T&gt;</c> satisfies durable contract registration.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task MessageRegisteredThroughContractsRegister_ProducesNoDiagnostic()
    {
        const string source = """
                              using System.Threading;
                              using System.Threading.Tasks;
                              using LiteBus.Commands.Abstractions;

                              public sealed record ProcessPaymentCommand(int PaymentId) : ICommand;

                              public sealed class ProcessPaymentCommandHandler : ICommandHandler<ProcessPaymentCommand>
                              {
                                  public Task HandleAsync(ProcessPaymentCommand command, CancellationToken cancellationToken = default)
                                      => Task.CompletedTask;
                              }

                              public static class InboxModuleConfiguration
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

        return AnalyzerTest.VerifyNoDiagnosticsAsync<MissingMessageContractRegistrationAnalyzer>(source);
    }

    /// <summary>
    ///     Verifies that handled event types without contract registration produce LB1007.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task HandledEventWithoutContractRegistration_ProducesDiagnostic()
    {
        const string source = """
                              using System.Threading;
                              using System.Threading.Tasks;
                              using LiteBus.Events.Abstractions;

                              public sealed record OrderSubmittedEvent(int OrderId) : IEvent;

                              public sealed class {|#0:OrderSubmittedEventHandler|} : IEventHandler<OrderSubmittedEvent>
                              {
                                  public Task HandleAsync(OrderSubmittedEvent @event, CancellationToken cancellationToken = default)
                                      => Task.CompletedTask;
                              }
                              """;

        return AnalyzerTest.VerifyDiagnosticAsync<MissingMessageContractRegistrationAnalyzer>(
            source,
            DiagnosticDescriptors.MissingMessageContractRegistration,
            0,
            "OrderSubmittedEvent",
            "OrderSubmittedEventHandler");
    }

    /// <summary>
    ///     Verifies that query handlers are not subject to durable contract registration.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task QueryHandler_ProducesNoDiagnostic()
    {
        const string source = """
                              using System.Threading;
                              using System.Threading.Tasks;
                              using LiteBus.Queries.Abstractions;

                              public sealed record GetUserQuery(int UserId) : IQuery<string>;

                              public sealed class GetUserQueryHandler : IQueryHandler<GetUserQuery, string>
                              {
                                  public Task<string> HandleAsync(GetUserQuery query, CancellationToken cancellationToken = default)
                                      => Task.FromResult("user");
                              }
                              """;

        return AnalyzerTest.VerifyNoDiagnosticsAsync<MissingMessageContractRegistrationAnalyzer>(source);
    }
}
