namespace LiteBus.Analyzers.UnitTests;

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
                              using LiteBus.Messaging.Abstractions;
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
            "ProcessPaymentCommandHandler",
            "ProcessPaymentCommand");
    }

    /// <summary>
    ///     Verifies that <c>Contracts.Register(typeof(Foo), ...)</c> satisfies durable contract registration.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task MessageRegisteredThroughTypeOfRegister_ProducesNoDiagnostic()
    {
        const string source = """
                              using System;
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
                                      contracts.Register(typeof(ProcessPaymentCommand), "payments.process-payment", 1);
                                  }
                              }

                              public sealed class ContractsRegistry
                              {
                                  public void Register(Type messageType, string name, int version)
                                  {
                                  }

                                  public void RegisterFromAssembly(System.Reflection.Assembly assembly)
                                  {
                                  }
                              }
                              """;

        return AnalyzerTest.VerifyNoDiagnosticsAsync<MissingMessageContractRegistrationAnalyzer>(source);
    }

    /// <summary>
    ///     Verifies that closed generic contract registration suggestions use the constructed type name.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task ClosedGenericHandledMessage_ProducesClosedTypeRegistrationSuggestion()
    {
        const string source = """
                              using System.Threading;
                              using System.Threading.Tasks;
                              using LiteBus.Commands.Abstractions;

                              public sealed record Payload(int Value);

                              public sealed record ProcessPayloadCommand<T>(T Value) : ICommand;

                              public sealed class {|#0:ProcessPayloadCommandHandler|} : ICommandHandler<ProcessPayloadCommand<Payload>>
                              {
                                  public Task HandleAsync(ProcessPayloadCommand<Payload> command, CancellationToken cancellationToken = default)
                                      => Task.CompletedTask;
                              }
                              """;

        return AnalyzerTest.VerifyDiagnosticAsync<MissingMessageContractRegistrationAnalyzer>(
            source,
            DiagnosticDescriptors.MissingMessageContractRegistration,
            0,
            "ProcessPayloadCommand<Payload>",
            "ProcessPayloadCommandHandler",
            "ProcessPayloadCommand<Payload>");
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

                                  public void RegisterFromAssembly(System.Reflection.Assembly assembly)
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
            "OrderSubmittedEventHandler",
            "OrderSubmittedEvent");
    }

    /// <summary>
    ///     Verifies that <c>RegisterFromAssembly</c> satisfies durable contract registration for LB1007.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task HandledMessageCoveredByRegisterFromAssembly_ProducesNoDiagnostic()
    {
        const string source = """
                              using System.Reflection;
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

        return AnalyzerTest.VerifyNoDiagnosticsAsync<MissingMessageContractRegistrationAnalyzer>(source);
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