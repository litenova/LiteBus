using Microsoft.CodeAnalysis;

namespace LiteBus.Analyzers.UnitTests;

/// <summary>
///     Tests for the <see cref="NonTransactionalOutboxInCommandHandlerAnalyzer" /> rule.
/// </summary>
public sealed class NonTransactionalOutboxInCommandHandlerAnalyzerTests
{
    /// <summary>
    ///     Verifies that a command handler without an outbox produces no diagnostic.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task CommandHandlerWithoutOutbox_ProducesNoDiagnostic()
    {
        const string source = """
                              using System.Threading;
                              using System.Threading.Tasks;
                              using LiteBus.Commands.Abstractions;

                              public sealed record CreateUserCommand(string Name) : ICommand;

                              public sealed class CreateUserCommandHandler : ICommandHandler<CreateUserCommand>
                              {
                                  public Task HandleAsync(CreateUserCommand command, CancellationToken cancellationToken = default)
                                      => Task.CompletedTask;
                              }
                              """;

        return AnalyzerTest.VerifyNoDiagnosticsAsync<NonTransactionalOutboxInCommandHandlerAnalyzer>(source);
    }

    /// <summary>
    ///     Verifies that a command handler injecting <c>IOutbox</c> produces LB1025.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task CommandHandlerInjectingOutbox_ProducesDiagnostic()
    {
        const string source = """
                              using System.Threading;
                              using System.Threading.Tasks;
                              using LiteBus.Commands.Abstractions;
                              using LiteBus.Outbox.Abstractions;

                              public sealed record CreateUserCommand(string Name) : ICommand;

                              public sealed class CreateUserCommandHandler : ICommandHandler<CreateUserCommand>
                              {
                                  public CreateUserCommandHandler(IOutbox {|#0:outbox|})
                                  {
                                  }

                                  public Task HandleAsync(CreateUserCommand command, CancellationToken cancellationToken = default)
                                      => Task.CompletedTask;
                              }

                              namespace LiteBus.Outbox.Storage.EntityFrameworkCore
                              {
                                  public static class OutboxModuleBuilderEfCoreExtensions
                                  {
                                  }
                              }
                              """;

        return AnalyzerTest.VerifyDiagnosticAsync<NonTransactionalOutboxInCommandHandlerAnalyzer>(
            source,
            DiagnosticDescriptors.NonTransactionalOutboxInCommandHandler,
            0,
            "CreateUserCommandHandler");
    }

    /// <summary>
    ///     Verifies that a command handler with a result that injects <c>IOutbox</c> produces LB1025.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task CommandHandlerWithResultInjectingOutbox_ProducesDiagnostic()
    {
        const string source = """
                              using System.Threading;
                              using System.Threading.Tasks;
                              using LiteBus.Commands.Abstractions;
                              using LiteBus.Outbox.Abstractions;

                              public sealed record AddUserCommand(string Name) : ICommand<int>;

                              public sealed class AddUserCommandHandler : ICommandHandler<AddUserCommand, int>
                              {
                                  public AddUserCommandHandler(IOutbox {|#0:outbox|})
                                  {
                                  }

                                  public Task<int> HandleAsync(AddUserCommand command, CancellationToken cancellationToken = default)
                                      => Task.FromResult(0);
                              }

                              namespace LiteBus.Outbox.Storage.EntityFrameworkCore
                              {
                                  public static class OutboxModuleBuilderEfCoreExtensions
                                  {
                                  }
                              }
                              """;

        return AnalyzerTest.VerifyDiagnosticAsync<NonTransactionalOutboxInCommandHandlerAnalyzer>(
            source,
            DiagnosticDescriptors.NonTransactionalOutboxInCommandHandler,
            0,
            "AddUserCommandHandler");
    }

    /// <summary>
    ///     Verifies that a command handler injecting the transactional outbox produces no diagnostic.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task CommandHandlerInjectingTransactionalOutbox_ProducesNoDiagnostic()
    {
        const string source = """
                              using System.Threading;
                              using System.Threading.Tasks;
                              using LiteBus.Commands.Abstractions;
                              using LiteBus.Outbox.Abstractions;

                              public sealed class OrdersDbContext
                              {
                              }

                              public sealed record CreateUserCommand(string Name) : ICommand;

                              public sealed class CreateUserCommandHandler : ICommandHandler<CreateUserCommand>
                              {
                                  public CreateUserCommandHandler(ITransactionalOutbox<OrdersDbContext> outbox)
                                  {
                                  }

                                  public Task HandleAsync(CreateUserCommand command, CancellationToken cancellationToken = default)
                                      => Task.CompletedTask;
                              }
                              """;

        return AnalyzerTest.VerifyNoDiagnosticsAsync<NonTransactionalOutboxInCommandHandlerAnalyzer>(source);
    }

    /// <summary>
    ///     Verifies that a query handler injecting <c>IOutbox</c> produces no diagnostic.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task QueryHandlerInjectingOutbox_ProducesNoDiagnostic()
    {
        const string source = """
                              using System.Threading;
                              using System.Threading.Tasks;
                              using LiteBus.Outbox.Abstractions;
                              using LiteBus.Queries.Abstractions;

                              public sealed record GetUserQuery(int UserId) : IQuery<string>;

                              public sealed class GetUserQueryHandler : IQueryHandler<GetUserQuery, string>
                              {
                                  public GetUserQueryHandler(IOutbox outbox)
                                  {
                                  }

                                  public Task<string> HandleAsync(GetUserQuery query, CancellationToken cancellationToken = default)
                                      => Task.FromResult("user");
                              }
                              """;

        return AnalyzerTest.VerifyNoDiagnosticsAsync<NonTransactionalOutboxInCommandHandlerAnalyzer>(source);
    }

    /// <summary>
    ///     Verifies that an event handler injecting <c>IOutbox</c> produces no diagnostic.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task EventHandlerInjectingOutbox_ProducesNoDiagnostic()
    {
        const string source = """
                              using System.Threading;
                              using System.Threading.Tasks;
                              using LiteBus.Events.Abstractions;
                              using LiteBus.Outbox.Abstractions;

                              public sealed record UserCreatedEvent(string Name) : IEvent;

                              public sealed class UserCreatedEventHandler : IEventHandler<UserCreatedEvent>
                              {
                                  public UserCreatedEventHandler(IOutbox outbox)
                                  {
                                  }

                                  public Task HandleAsync(UserCreatedEvent message, CancellationToken cancellationToken = default)
                                      => Task.CompletedTask;
                              }
                              """;

        return AnalyzerTest.VerifyNoDiagnosticsAsync<NonTransactionalOutboxInCommandHandlerAnalyzer>(source);
    }

    /// <summary>
    ///     Verifies that a messaging-level handler for a command injecting <c>IOutbox</c> produces no diagnostic.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task MessagingHandlerForCommandInjectingOutbox_ProducesNoDiagnostic()
    {
        const string source = """
                              using System.Threading;
                              using System.Threading.Tasks;
                              using LiteBus.Commands.Abstractions;
                              using LiteBus.Messaging.Abstractions;
                              using LiteBus.Outbox.Abstractions;

                              public sealed record CreateUserCommand(string Name) : ICommand;

                              public sealed class CreateUserMessageHandler : IAsyncMessageHandler<CreateUserCommand>
                              {
                                  public CreateUserMessageHandler(IOutbox outbox)
                                  {
                                  }

                                  public Task HandleAsync(CreateUserCommand message, CancellationToken cancellationToken = default)
                                      => Task.CompletedTask;
                              }
                              """;

        return AnalyzerTest.VerifyNoDiagnosticsAsync<NonTransactionalOutboxInCommandHandlerAnalyzer>(source);
    }

    /// <summary>
    ///     Verifies that a class outside command handling that injects <c>IOutbox</c> produces no diagnostic.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task NonHandlerClassInjectingOutbox_ProducesNoDiagnostic()
    {
        const string source = """
                              using LiteBus.Outbox.Abstractions;

                              public sealed class OrderService
                              {
                                  public OrderService(IOutbox outbox)
                                  {
                                  }
                              }
                              """;

        return AnalyzerTest.VerifyNoDiagnosticsAsync<NonTransactionalOutboxInCommandHandlerAnalyzer>(source);
    }

    /// <summary>
    ///     Verifies that a private test-seam constructor injecting <c>IOutbox</c> produces no diagnostic.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task CommandHandlerWithPrivateOutboxConstructor_ProducesNoDiagnostic()
    {
        const string source = """
                              using System.Threading;
                              using System.Threading.Tasks;
                              using LiteBus.Commands.Abstractions;
                              using LiteBus.Outbox.Abstractions;

                              public sealed record CreateUserCommand(string Name) : ICommand;

                              public sealed class CreateUserCommandHandler : ICommandHandler<CreateUserCommand>
                              {
                                  private readonly IOutbox _outbox;

                                  public CreateUserCommandHandler()
                                      : this(null!)
                                  {
                                  }

                                  private CreateUserCommandHandler(IOutbox outbox)
                                  {
                                      _outbox = outbox;
                                  }

                                  public Task HandleAsync(CreateUserCommand command, CancellationToken cancellationToken = default)
                                      => Task.CompletedTask;
                              }
                              """;

        return AnalyzerTest.VerifyNoDiagnosticsAsync<NonTransactionalOutboxInCommandHandlerAnalyzer>(source);
    }

    /// <summary>
    ///     Verifies that the rule is a warning in <c>LiteBus.Outbox</c> that stays on by default.
    /// </summary>
    [Fact]
    public void Descriptor_IsWarningInOutboxCategoryAndEnabledByDefault()
    {
        Assert.Equal("LiteBus.Outbox", DiagnosticDescriptors.NonTransactionalOutboxInCommandHandler.Category);
        Assert.Equal(DiagnosticSeverity.Warning, DiagnosticDescriptors.NonTransactionalOutboxInCommandHandler.DefaultSeverity);
        Assert.True(DiagnosticDescriptors.NonTransactionalOutboxInCommandHandler.IsEnabledByDefault);
    }

    /// <summary>
    ///     Verifies that a command handler injecting <c>IOutbox</c> produces no diagnostic when the compilation sees no
    ///     transactional outbox storage, as in an application on in-memory outbox storage.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task CommandHandlerInjectingOutboxWithoutTransactionalStorage_ProducesNoDiagnostic()
    {
        const string source = """
                              using System.Threading;
                              using System.Threading.Tasks;
                              using LiteBus.Commands.Abstractions;
                              using LiteBus.Outbox.Abstractions;

                              public sealed record CreateUserCommand(string Name) : ICommand;

                              public sealed class CreateUserCommandHandler : ICommandHandler<CreateUserCommand>
                              {
                                  public CreateUserCommandHandler(IOutbox outbox)
                                  {
                                  }

                                  public Task HandleAsync(CreateUserCommand command, CancellationToken cancellationToken = default)
                                      => Task.CompletedTask;
                              }
                              """;

        return AnalyzerTest.VerifyNoDiagnosticsAsync<NonTransactionalOutboxInCommandHandlerAnalyzer>(source);
    }
}
