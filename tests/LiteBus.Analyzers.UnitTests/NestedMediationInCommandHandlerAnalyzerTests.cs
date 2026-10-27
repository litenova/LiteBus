using Microsoft.CodeAnalysis;

namespace LiteBus.Analyzers.UnitTests;

/// <summary>
///     Tests for the <see cref="NestedMediationInCommandHandlerAnalyzer" /> rule.
/// </summary>
public sealed class NestedMediationInCommandHandlerAnalyzerTests
{
    /// <summary>
    ///     Verifies that a command handler without mediation produces no diagnostic.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task CommandHandlerWithoutMediation_ProducesNoDiagnostic()
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

        return AnalyzerTest.VerifyNoDiagnosticsAsync<NestedMediationInCommandHandlerAnalyzer>(source);
    }

    /// <summary>
    ///     Verifies that a command handler sending another command produces LB1023.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task CommandHandlerSendingCommand_ProducesDiagnostic()
    {
        const string source = """
                              using System.Threading;
                              using System.Threading.Tasks;
                              using LiteBus.Commands.Abstractions;

                              public sealed record CreateUserCommand(string Name) : ICommand;
                              public sealed record AuditUserCommand(string Name) : ICommand;

                              public sealed class CreateUserCommandHandler : ICommandHandler<CreateUserCommand>
                              {
                                  private readonly ICommandMediator _commandMediator;

                                  public CreateUserCommandHandler(ICommandMediator commandMediator)
                                  {
                                      _commandMediator = commandMediator;
                                  }

                                  public Task HandleAsync(CreateUserCommand command, CancellationToken cancellationToken = default)
                                      => {|#0:_commandMediator.SendAsync(new AuditUserCommand(command.Name), cancellationToken: cancellationToken)|};
                              }
                              """;

        return AnalyzerTest.VerifyDiagnosticAsync<NestedMediationInCommandHandlerAnalyzer>(
            source,
            DiagnosticDescriptors.NestedMediationInCommandHandler,
            0,
            "CreateUserCommandHandler",
            "LiteBus.Commands.Abstractions.ICommandMediator.SendAsync");
    }

    /// <summary>
    ///     Verifies that a positional send through the mediator extension overload produces LB1023.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task CommandHandlerSendingCommandThroughExtension_ProducesDiagnostic()
    {
        const string source = """
                              using System.Threading;
                              using System.Threading.Tasks;
                              using LiteBus.Commands.Abstractions;

                              public sealed record CreateUserCommand(string Name) : ICommand;
                              public sealed record AuditUserCommand(string Name) : ICommand;

                              public sealed class CreateUserCommandHandler : ICommandHandler<CreateUserCommand>
                              {
                                  private readonly ICommandMediator _commandMediator;

                                  public CreateUserCommandHandler(ICommandMediator commandMediator)
                                  {
                                      _commandMediator = commandMediator;
                                  }

                                  public Task HandleAsync(CreateUserCommand command, CancellationToken cancellationToken = default)
                                      => {|#0:_commandMediator.SendAsync(new AuditUserCommand(command.Name), cancellationToken)|};
                              }
                              """;

        return AnalyzerTest.VerifyDiagnosticAsync<NestedMediationInCommandHandlerAnalyzer>(
            source,
            DiagnosticDescriptors.NestedMediationInCommandHandler,
            0,
            "CreateUserCommandHandler",
            "LiteBus.Commands.Abstractions.ICommandMediator.SendAsync");
    }

    /// <summary>
    ///     Verifies that a result command handler sending another command produces LB1023.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task ResultCommandHandlerSendingCommand_ProducesDiagnostic()
    {
        const string source = """
                              using System.Threading;
                              using System.Threading.Tasks;
                              using LiteBus.Commands.Abstractions;

                              public sealed record CreateUserCommand(string Name) : ICommand<int>;
                              public sealed record AuditUserCommand(string Name) : ICommand;

                              public sealed class CreateUserCommandHandler : ICommandHandler<CreateUserCommand, int>
                              {
                                  private readonly ICommandMediator _commandMediator;

                                  public CreateUserCommandHandler(ICommandMediator commandMediator)
                                  {
                                      _commandMediator = commandMediator;
                                  }

                                  public Task<int> HandleAsync(CreateUserCommand command, CancellationToken cancellationToken = default)
                                  {
                                      _ = {|#0:_commandMediator.SendAsync(new AuditUserCommand(command.Name), cancellationToken: cancellationToken)|};
                                      return Task.FromResult(1);
                                  }
                              }
                              """;

        return AnalyzerTest.VerifyDiagnosticAsync<NestedMediationInCommandHandlerAnalyzer>(
            source,
            DiagnosticDescriptors.NestedMediationInCommandHandler,
            0,
            "CreateUserCommandHandler",
            "LiteBus.Commands.Abstractions.ICommandMediator.SendAsync");
    }

    /// <summary>
    ///     Verifies that a command handler trying another command produces LB1023.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task CommandHandlerTryingCommand_ProducesDiagnostic()
    {
        const string source = """
                              using System.Threading;
                              using System.Threading.Tasks;
                              using LiteBus.Commands.Abstractions;

                              public sealed record CreateUserCommand(string Name) : ICommand;
                              public sealed record AuditUserCommand(string Name) : ICommand;

                              public sealed class CreateUserCommandHandler : ICommandHandler<CreateUserCommand>
                              {
                                  private readonly ICommandMediator _commandMediator;

                                  public CreateUserCommandHandler(ICommandMediator commandMediator)
                                  {
                                      _commandMediator = commandMediator;
                                  }

                                  public Task HandleAsync(CreateUserCommand command, CancellationToken cancellationToken = default)
                                  {
                                      _ = {|#0:_commandMediator.TrySendAsync(new AuditUserCommand(command.Name), cancellationToken: cancellationToken)|};
                                      return Task.CompletedTask;
                                  }
                              }
                              """;

        return AnalyzerTest.VerifyDiagnosticAsync<NestedMediationInCommandHandlerAnalyzer>(
            source,
            DiagnosticDescriptors.NestedMediationInCommandHandler,
            0,
            "CreateUserCommandHandler",
            "LiteBus.Commands.Abstractions.ICommandMediator.TrySendAsync");
    }

    /// <summary>
    ///     Verifies that a command handler publishing an event produces LB1023.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task CommandHandlerPublishingEvent_ProducesDiagnostic()
    {
        const string source = """
                              using System.Threading;
                              using System.Threading.Tasks;
                              using LiteBus.Commands.Abstractions;
                              using LiteBus.Events.Abstractions;

                              public sealed record CreateUserCommand(string Name) : ICommand;
                              public sealed record UserCreatedEvent(string Name) : IEvent;

                              public sealed class CreateUserCommandHandler : ICommandHandler<CreateUserCommand>
                              {
                                  private readonly IEventMediator _eventMediator;

                                  public CreateUserCommandHandler(IEventMediator eventMediator)
                                  {
                                      _eventMediator = eventMediator;
                                  }

                                  public Task HandleAsync(CreateUserCommand command, CancellationToken cancellationToken = default)
                                      => {|#0:_eventMediator.PublishAsync(new UserCreatedEvent(command.Name), cancellationToken: cancellationToken)|};
                              }
                              """;

        return AnalyzerTest.VerifyDiagnosticAsync<NestedMediationInCommandHandlerAnalyzer>(
            source,
            DiagnosticDescriptors.NestedMediationInCommandHandler,
            0,
            "CreateUserCommandHandler",
            "LiteBus.Events.Abstractions.IEventMediator.PublishAsync");
    }

    /// <summary>
    ///     Verifies that a command handler mediating another message produces LB1023.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task CommandHandlerMediatingMessage_ProducesDiagnostic()
    {
        const string source = """
                              using System.Collections.Generic;
                              using System.Threading;
                              using System.Threading.Tasks;
                              using LiteBus.Commands.Abstractions;
                              using LiteBus.Messaging.Abstractions;

                              public sealed record CreateUserCommand(string Name) : ICommand;
                              public sealed record AuditUserCommand(string Name) : ICommand;

                              public sealed class CreateUserCommandHandler : ICommandHandler<CreateUserCommand>
                              {
                                  private readonly IMessageMediator _mediator;

                                  public CreateUserCommandHandler(IMessageMediator mediator)
                                  {
                                      _mediator = mediator;
                                  }

                                  public Task HandleAsync(CreateUserCommand command, CancellationToken cancellationToken = default)
                                  {
                                      var request = new MessageMediationRequest<AuditUserCommand, Task>
                                      {
                                          MessageResolveStrategy = null!,
                                          MessageMediationStrategy = null!,
                                          Tags = new List<string>()
                                      };
                                      return {|#0:_mediator.Mediate(new AuditUserCommand(command.Name), request, cancellationToken)|};
                                  }
                              }
                              """;

        return AnalyzerTest.VerifyDiagnosticAsync<NestedMediationInCommandHandlerAnalyzer>(
            source,
            DiagnosticDescriptors.NestedMediationInCommandHandler,
            0,
            "CreateUserCommandHandler",
            "LiteBus.Messaging.Abstractions.IMessageMediator.Mediate");
    }

    /// <summary>
    ///     Verifies that mediating a query reads without nesting a unit of work and produces no diagnostic.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task CommandHandlerMediatingQuery_ProducesNoDiagnostic()
    {
        const string source = """
                              using System.Collections.Generic;
                              using System.Threading;
                              using System.Threading.Tasks;
                              using LiteBus.Commands.Abstractions;
                              using LiteBus.Messaging.Abstractions;
                              using LiteBus.Queries.Abstractions;

                              public sealed record CreateUserCommand(string Name) : ICommand;
                              public sealed record GetUserQuery(string Name) : IQuery<string>;

                              public sealed class CreateUserCommandHandler : ICommandHandler<CreateUserCommand>
                              {
                                  private readonly IMessageMediator _mediator;

                                  public CreateUserCommandHandler(IMessageMediator mediator)
                                  {
                                      _mediator = mediator;
                                  }

                                  public Task HandleAsync(CreateUserCommand command, CancellationToken cancellationToken = default)
                                  {
                                      var request = new MessageMediationRequest<GetUserQuery, Task<string>>
                                      {
                                          MessageResolveStrategy = null!,
                                          MessageMediationStrategy = null!,
                                          Tags = new List<string>()
                                      };
                                      _ = _mediator.Mediate(new GetUserQuery(command.Name), request, cancellationToken);
                                      return Task.CompletedTask;
                                  }
                              }
                              """;

        return AnalyzerTest.VerifyNoDiagnosticsAsync<NestedMediationInCommandHandlerAnalyzer>(source);
    }

    /// <summary>
    ///     Verifies that reading through <c>IQueryMediator</c> produces no diagnostic.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task CommandHandlerQuerying_ProducesNoDiagnostic()
    {
        const string source = """
                              using System.Threading;
                              using System.Threading.Tasks;
                              using LiteBus.Commands.Abstractions;
                              using LiteBus.Queries.Abstractions;

                              public sealed record CreateUserCommand(int UserId) : ICommand;
                              public sealed record GetUserQuery(int UserId) : IQuery<string>;

                              public sealed class CreateUserCommandHandler : ICommandHandler<CreateUserCommand>
                              {
                                  private readonly IQueryMediator _queryMediator;

                                  public CreateUserCommandHandler(IQueryMediator queryMediator)
                                  {
                                      _queryMediator = queryMediator;
                                  }

                                  public async Task HandleAsync(CreateUserCommand command, CancellationToken cancellationToken = default)
                                  {
                                      _ = await _queryMediator.QueryAsync(new GetUserQuery(command.UserId), cancellationToken).ConfigureAwait(false);
                                  }
                              }
                              """;

        return AnalyzerTest.VerifyNoDiagnosticsAsync<NestedMediationInCommandHandlerAnalyzer>(source);
    }

    /// <summary>
    ///     Verifies that an <c>EvaluateAsync</c> guard check produces no diagnostic.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task CommandHandlerEvaluatingCommand_ProducesNoDiagnostic()
    {
        const string source = """
                              using System.Threading;
                              using System.Threading.Tasks;
                              using LiteBus.Commands.Abstractions;

                              public sealed record CreateUserCommand(string Name) : ICommand;

                              public sealed class CreateUserCommandHandler : ICommandHandler<CreateUserCommand>
                              {
                                  private readonly ICommandMediator _commandMediator;

                                  public CreateUserCommandHandler(ICommandMediator commandMediator)
                                  {
                                      _commandMediator = commandMediator;
                                  }

                                  public async Task HandleAsync(CreateUserCommand command, CancellationToken cancellationToken = default)
                                  {
                                      _ = await _commandMediator.EvaluateAsync(command, cancellationToken: cancellationToken).ConfigureAwait(false);
                                  }
                              }
                              """;

        return AnalyzerTest.VerifyNoDiagnosticsAsync<NestedMediationInCommandHandlerAnalyzer>(source);
    }

    /// <summary>
    ///     Verifies that a query handler sending a command produces no diagnostic.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task QueryHandlerSendingCommand_ProducesNoDiagnostic()
    {
        const string source = """
                              using System.Threading;
                              using System.Threading.Tasks;
                              using LiteBus.Commands.Abstractions;
                              using LiteBus.Queries.Abstractions;

                              public sealed record GetUserQuery(int UserId) : IQuery<string>;
                              public sealed record AuditUserCommand(int UserId) : ICommand;

                              public sealed class GetUserQueryHandler : IQueryHandler<GetUserQuery, string>
                              {
                                  private readonly ICommandMediator _commandMediator;

                                  public GetUserQueryHandler(ICommandMediator commandMediator)
                                  {
                                      _commandMediator = commandMediator;
                                  }

                                  public Task<string> HandleAsync(GetUserQuery query, CancellationToken cancellationToken = default)
                                  {
                                      _ = _commandMediator.SendAsync(new AuditUserCommand(query.UserId), cancellationToken: cancellationToken);
                                      return Task.FromResult("user");
                                  }
                              }
                              """;

        return AnalyzerTest.VerifyNoDiagnosticsAsync<NestedMediationInCommandHandlerAnalyzer>(source);
    }

    /// <summary>
    ///     Verifies that an event handler publishing an event produces no diagnostic.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task EventHandlerPublishingEvent_ProducesNoDiagnostic()
    {
        const string source = """
                              using System.Threading;
                              using System.Threading.Tasks;
                              using LiteBus.Events.Abstractions;

                              public sealed record UserCreatedEvent(string Name) : IEvent;
                              public sealed record WelcomeEmailEvent(string Name) : IEvent;

                              public sealed class UserCreatedEventHandler : IEventHandler<UserCreatedEvent>
                              {
                                  private readonly IEventMediator _eventMediator;

                                  public UserCreatedEventHandler(IEventMediator eventMediator)
                                  {
                                      _eventMediator = eventMediator;
                                  }

                                  public Task HandleAsync(UserCreatedEvent message, CancellationToken cancellationToken = default)
                                  {
                                      _ = _eventMediator.PublishAsync(new WelcomeEmailEvent(message.Name), cancellationToken: cancellationToken);
                                      return Task.CompletedTask;
                                  }
                              }
                              """;

        return AnalyzerTest.VerifyNoDiagnosticsAsync<NestedMediationInCommandHandlerAnalyzer>(source);
    }

    /// <summary>
    ///     Verifies that a messaging-level handler for a command produces LB1023.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task MessagingHandlerForCommand_ProducesDiagnostic()
    {
        const string source = """
                              using System.Threading;
                              using System.Threading.Tasks;
                              using LiteBus.Commands.Abstractions;
                              using LiteBus.Messaging.Abstractions;

                              public sealed record CreateUserCommand(string Name) : ICommand;
                              public sealed record AuditUserCommand(string Name) : ICommand;

                              public sealed class CreateUserMessageHandler : IAsyncMessageHandler<CreateUserCommand>
                              {
                                  private readonly ICommandMediator _commandMediator;

                                  public CreateUserMessageHandler(ICommandMediator commandMediator)
                                  {
                                      _commandMediator = commandMediator;
                                  }

                                  public Task HandleAsync(CreateUserCommand message, CancellationToken cancellationToken = default)
                                      => {|#0:_commandMediator.SendAsync(new AuditUserCommand(message.Name), cancellationToken: cancellationToken)|};
                              }
                              """;

        return AnalyzerTest.VerifyDiagnosticAsync<NestedMediationInCommandHandlerAnalyzer>(
            source,
            DiagnosticDescriptors.NestedMediationInCommandHandler,
            0,
            "CreateUserMessageHandler",
            "LiteBus.Commands.Abstractions.ICommandMediator.SendAsync");
    }

    /// <summary>
    ///     Verifies that a messaging-level handler for a query produces no diagnostic.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task MessagingHandlerForQuery_ProducesNoDiagnostic()
    {
        const string source = """
                              using System.Threading;
                              using System.Threading.Tasks;
                              using LiteBus.Commands.Abstractions;
                              using LiteBus.Messaging.Abstractions;
                              using LiteBus.Queries.Abstractions;

                              public sealed record GetUserQuery(int UserId) : IQuery<string>;
                              public sealed record AuditUserCommand(int UserId) : ICommand;

                              public sealed class GetUserMessageHandler : IAsyncMessageHandler<GetUserQuery, string>
                              {
                                  private readonly ICommandMediator _commandMediator;

                                  public GetUserMessageHandler(ICommandMediator commandMediator)
                                  {
                                      _commandMediator = commandMediator;
                                  }

                                  public Task<string> HandleAsync(GetUserQuery message, CancellationToken cancellationToken = default)
                                  {
                                      _ = _commandMediator.SendAsync(new AuditUserCommand(message.UserId), cancellationToken: cancellationToken);
                                      return Task.FromResult("user");
                                  }
                              }
                              """;

        return AnalyzerTest.VerifyNoDiagnosticsAsync<NestedMediationInCommandHandlerAnalyzer>(source);
    }

    /// <summary>
    ///     Verifies that the rule is a warning in <c>LiteBus.Handlers</c> that stays off until enabled.
    /// </summary>
    [Fact]
    public void Descriptor_IsWarningInHandlersCategoryAndDisabledByDefault()
    {
        Assert.Equal("LiteBus.Handlers", DiagnosticDescriptors.NestedMediationInCommandHandler.Category);
        Assert.Equal(DiagnosticSeverity.Warning, DiagnosticDescriptors.NestedMediationInCommandHandler.DefaultSeverity);
        Assert.False(DiagnosticDescriptors.NestedMediationInCommandHandler.IsEnabledByDefault);
    }
}
