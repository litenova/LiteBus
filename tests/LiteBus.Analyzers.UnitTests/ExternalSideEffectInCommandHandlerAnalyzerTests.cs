using Microsoft.CodeAnalysis;

namespace LiteBus.Analyzers.UnitTests;

/// <summary>
///     Tests for the <see cref="ExternalSideEffectInCommandHandlerAnalyzer" /> rule.
/// </summary>
public sealed class ExternalSideEffectInCommandHandlerAnalyzerTests
{
    /// <summary>
    ///     Verifies that a command handler without side effects produces no diagnostic.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task CommandHandlerWithoutSideEffects_ProducesNoDiagnostic()
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

        return AnalyzerTest.VerifyNoDiagnosticsAsync<ExternalSideEffectInCommandHandlerAnalyzer>(source);
    }

    /// <summary>
    ///     Verifies that a command handler injecting <c>HttpClient</c> produces LB1024.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task CommandHandlerInjectingHttpClient_ProducesDiagnostic()
    {
        const string source = """
                              using System.Net.Http;
                              using System.Threading;
                              using System.Threading.Tasks;
                              using LiteBus.Commands.Abstractions;

                              public sealed record CreateUserCommand(string Name) : ICommand;

                              public sealed class CreateUserCommandHandler : ICommandHandler<CreateUserCommand>
                              {
                                  private readonly HttpClient _client;

                                  public CreateUserCommandHandler(HttpClient {|#0:client|})
                                  {
                                      _client = client;
                                  }

                                  public Task HandleAsync(CreateUserCommand command, CancellationToken cancellationToken = default)
                                      => Task.CompletedTask;
                              }
                              """;

        return AnalyzerTest.VerifyDiagnosticAsync<ExternalSideEffectInCommandHandlerAnalyzer>(
            source,
            DiagnosticDescriptors.ExternalSideEffectInCommandHandler,
            0,
            "CreateUserCommandHandler",
            "System.Net.Http.HttpClient");
    }

    /// <summary>
    ///     Verifies that a command handler injecting <c>IHttpClientFactory</c> produces LB1024.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task CommandHandlerInjectingHttpClientFactory_ProducesDiagnostic()
    {
        const string source = """
                              using System.Net.Http;
                              using System.Threading;
                              using System.Threading.Tasks;
                              using LiteBus.Commands.Abstractions;

                              public sealed record CreateUserCommand(string Name) : ICommand;

                              public sealed class CreateUserCommandHandler : ICommandHandler<CreateUserCommand>
                              {
                                  public CreateUserCommandHandler(IHttpClientFactory {|#0:factory|})
                                  {
                                  }

                                  public Task HandleAsync(CreateUserCommand command, CancellationToken cancellationToken = default)
                                      => Task.CompletedTask;
                              }

                              namespace System.Net.Http
                              {
                                  // IHttpClientFactory ships in Microsoft.Extensions.Http, which the test compilation does not
                                  // reference, so the contract is declared under its real metadata name.
                                  public interface IHttpClientFactory
                                  {
                                  }
                              }
                              """;

        return AnalyzerTest.VerifyDiagnosticAsync<ExternalSideEffectInCommandHandlerAnalyzer>(
            source,
            DiagnosticDescriptors.ExternalSideEffectInCommandHandler,
            0,
            "CreateUserCommandHandler",
            "System.Net.Http.IHttpClientFactory");
    }

    /// <summary>
    ///     Verifies that a command handler injecting an <c>HttpClient</c> subclass produces LB1024.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task CommandHandlerInjectingHttpClientSubclass_ProducesDiagnostic()
    {
        const string source = """
                              using System.Net.Http;
                              using System.Threading;
                              using System.Threading.Tasks;
                              using LiteBus.Commands.Abstractions;

                              public sealed class RecordingHttpClient : HttpClient
                              {
                              }

                              public sealed record CreateUserCommand(string Name) : ICommand;

                              public sealed class CreateUserCommandHandler : ICommandHandler<CreateUserCommand>
                              {
                                  public CreateUserCommandHandler(RecordingHttpClient {|#0:client|})
                                  {
                                  }

                                  public Task HandleAsync(CreateUserCommand command, CancellationToken cancellationToken = default)
                                      => Task.CompletedTask;
                              }
                              """;

        return AnalyzerTest.VerifyDiagnosticAsync<ExternalSideEffectInCommandHandlerAnalyzer>(
            source,
            DiagnosticDescriptors.ExternalSideEffectInCommandHandler,
            0,
            "CreateUserCommandHandler",
            "System.Net.Http.HttpClient");
    }

    /// <summary>
    ///     Verifies that a query handler injecting <c>HttpClient</c> produces no diagnostic.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task QueryHandlerInjectingHttpClient_ProducesNoDiagnostic()
    {
        const string source = """
                              using System.Net.Http;
                              using System.Threading;
                              using System.Threading.Tasks;
                              using LiteBus.Queries.Abstractions;

                              public sealed record GetUserQuery(int UserId) : IQuery<string>;

                              public sealed class GetUserQueryHandler : IQueryHandler<GetUserQuery, string>
                              {
                                  public GetUserQueryHandler(HttpClient client)
                                  {
                                  }

                                  public Task<string> HandleAsync(GetUserQuery query, CancellationToken cancellationToken = default)
                                      => Task.FromResult("user");
                              }
                              """;

        return AnalyzerTest.VerifyNoDiagnosticsAsync<ExternalSideEffectInCommandHandlerAnalyzer>(source);
    }

    /// <summary>
    ///     Verifies that an event handler injecting <c>HttpClient</c> produces no diagnostic.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task EventHandlerInjectingHttpClient_ProducesNoDiagnostic()
    {
        const string source = """
                              using System.Net.Http;
                              using System.Threading;
                              using System.Threading.Tasks;
                              using LiteBus.Events.Abstractions;

                              public sealed record UserCreatedEvent(string Name) : IEvent;

                              public sealed class UserCreatedEventHandler : IEventHandler<UserCreatedEvent>
                              {
                                  public UserCreatedEventHandler(HttpClient client)
                                  {
                                  }

                                  public Task HandleAsync(UserCreatedEvent message, CancellationToken cancellationToken = default)
                                      => Task.CompletedTask;
                              }
                              """;

        return AnalyzerTest.VerifyNoDiagnosticsAsync<ExternalSideEffectInCommandHandlerAnalyzer>(source);
    }

    /// <summary>
    ///     Verifies that a messaging-level handler for a command produces LB1024.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task MessagingHandlerForCommandInjectingHttpClient_ProducesDiagnostic()
    {
        const string source = """
                              using System.Net.Http;
                              using System.Threading;
                              using System.Threading.Tasks;
                              using LiteBus.Commands.Abstractions;
                              using LiteBus.Messaging.Abstractions;

                              public sealed record CreateUserCommand(string Name) : ICommand;

                              public sealed class CreateUserMessageHandler : IAsyncMessageHandler<CreateUserCommand>
                              {
                                  public CreateUserMessageHandler(HttpClient {|#0:client|})
                                  {
                                  }

                                  public Task HandleAsync(CreateUserCommand message, CancellationToken cancellationToken = default)
                                      => Task.CompletedTask;
                              }
                              """;

        return AnalyzerTest.VerifyDiagnosticAsync<ExternalSideEffectInCommandHandlerAnalyzer>(
            source,
            DiagnosticDescriptors.ExternalSideEffectInCommandHandler,
            0,
            "CreateUserMessageHandler",
            "System.Net.Http.HttpClient");
    }

    /// <summary>
    ///     Verifies that a side-effect type outside constructor parameters produces no diagnostic.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task CommandHandlerWithMethodParameter_ProducesNoDiagnostic()
    {
        const string source = """
                              using System.Net.Http;
                              using System.Threading;
                              using System.Threading.Tasks;
                              using LiteBus.Commands.Abstractions;

                              public sealed record CreateUserCommand(string Name) : ICommand;

                              public sealed class CreateUserCommandHandler : ICommandHandler<CreateUserCommand>
                              {
                                  public Task HandleAsync(CreateUserCommand command, CancellationToken cancellationToken = default)
                                      => Task.CompletedTask;

                                  private void Configure(HttpClient client)
                                  {
                                  }
                              }
                              """;

        return AnalyzerTest.VerifyNoDiagnosticsAsync<ExternalSideEffectInCommandHandlerAnalyzer>(source);
    }

    /// <summary>
    ///     Verifies that a type named in <c>litebus.LB1024.forbidden_types</c> produces LB1024.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task CustomForbiddenType_ProducesDiagnostic()
    {
        const string source = """
                              using System.Threading;
                              using System.Threading.Tasks;
                              using LiteBus.Commands.Abstractions;

                              namespace App.SideEffects;

                              public sealed class BillingClient
                              {
                              }

                              public sealed record CreateUserCommand(string Name) : ICommand;

                              public sealed class CreateUserCommandHandler : ICommandHandler<CreateUserCommand>
                              {
                                  public CreateUserCommandHandler(BillingClient {|#0:client|})
                                  {
                                  }

                                  public Task HandleAsync(CreateUserCommand command, CancellationToken cancellationToken = default)
                                      => Task.CompletedTask;
                              }
                              """;

        const string editorConfig = """
                                    is_global = true
                                    litebus.LB1024.forbidden_types = App.SideEffects.BillingClient
                                    """;

        return AnalyzerTest.VerifyWithEditorConfigAsync<ExternalSideEffectInCommandHandlerAnalyzer>(
            source,
            editorConfig,
            (DiagnosticDescriptors.ExternalSideEffectInCommandHandler, 0, ["CreateUserCommandHandler", "App.SideEffects.BillingClient"]));
    }

    /// <summary>
    ///     Verifies that a configured list replaces the default forbidden types.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task CustomForbiddenTypeList_ReplacesDefaults()
    {
        const string source = """
                              using System.Net.Http;
                              using System.Threading;
                              using System.Threading.Tasks;
                              using LiteBus.Commands.Abstractions;

                              namespace App.SideEffects;

                              public sealed class BillingClient
                              {
                              }

                              public sealed record CreateUserCommand(string Name) : ICommand;

                              public sealed class CreateUserCommandHandler : ICommandHandler<CreateUserCommand>
                              {
                                  public CreateUserCommandHandler(HttpClient client)
                                  {
                                  }

                                  public Task HandleAsync(CreateUserCommand command, CancellationToken cancellationToken = default)
                                      => Task.CompletedTask;
                              }
                              """;

        const string editorConfig = """
                                    is_global = true
                                    litebus.LB1024.forbidden_types = App.SideEffects.BillingClient
                                    """;

        return AnalyzerTest.VerifyWithEditorConfigAsync<ExternalSideEffectInCommandHandlerAnalyzer>(source, editorConfig);
    }

    /// <summary>
    ///     Verifies that a per-file <c>.editorconfig</c> section configures the forbidden types.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task PerTreeConfiguration_Applies()
    {
        const string source = """
                              using System.Threading;
                              using System.Threading.Tasks;
                              using LiteBus.Commands.Abstractions;

                              namespace App.SideEffects;

                              public sealed class BillingClient
                              {
                              }

                              public sealed record CreateUserCommand(string Name) : ICommand;

                              public sealed class CreateUserCommandHandler : ICommandHandler<CreateUserCommand>
                              {
                                  public CreateUserCommandHandler(BillingClient {|#0:client|})
                                  {
                                  }

                                  public Task HandleAsync(CreateUserCommand command, CancellationToken cancellationToken = default)
                                      => Task.CompletedTask;
                              }
                              """;

        const string editorConfig = """
                                    [*.cs]
                                    litebus.LB1024.forbidden_types = App.SideEffects.BillingClient
                                    """;

        return AnalyzerTest.VerifyWithEditorConfigAsync<ExternalSideEffectInCommandHandlerAnalyzer>(
            source,
            editorConfig,
            (DiagnosticDescriptors.ExternalSideEffectInCommandHandler, 0, ["CreateUserCommandHandler", "App.SideEffects.BillingClient"]));
    }

    /// <summary>
    ///     Verifies that a configured value naming nothing reports nothing.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task BlankConfiguration_DisablesRule()
    {
        const string source = """
                              using System.Net.Http;
                              using System.Threading;
                              using System.Threading.Tasks;
                              using LiteBus.Commands.Abstractions;

                              public sealed record CreateUserCommand(string Name) : ICommand;

                              public sealed class CreateUserCommandHandler : ICommandHandler<CreateUserCommand>
                              {
                                  public CreateUserCommandHandler(HttpClient client)
                                  {
                                  }

                                  public Task HandleAsync(CreateUserCommand command, CancellationToken cancellationToken = default)
                                      => Task.CompletedTask;
                              }
                              """;

        const string editorConfig = """
                                    is_global = true
                                    litebus.LB1024.forbidden_types = ;
                                    """;

        return AnalyzerTest.VerifyWithEditorConfigAsync<ExternalSideEffectInCommandHandlerAnalyzer>(source, editorConfig);
    }

    /// <summary>
    ///     Verifies that a private test-seam constructor injecting <c>HttpClient</c> produces no diagnostic.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task CommandHandlerWithPrivateSideEffectConstructor_ProducesNoDiagnostic()
    {
        const string source = """
                              using System.Net.Http;
                              using System.Threading;
                              using System.Threading.Tasks;
                              using LiteBus.Commands.Abstractions;

                              public sealed record CreateUserCommand(string Name) : ICommand;

                              public sealed class CreateUserCommandHandler : ICommandHandler<CreateUserCommand>
                              {
                                  public CreateUserCommandHandler()
                                      : this(new HttpClient())
                                  {
                                  }

                                  private CreateUserCommandHandler(HttpClient client)
                                  {
                                  }

                                  public Task HandleAsync(CreateUserCommand command, CancellationToken cancellationToken = default)
                                      => Task.CompletedTask;
                              }
                              """;

        return AnalyzerTest.VerifyNoDiagnosticsAsync<ExternalSideEffectInCommandHandlerAnalyzer>(source);
    }

    /// <summary>
    ///     Verifies that the rule is a warning in <c>LiteBus.Handlers</c> that stays off until enabled.
    /// </summary>
    [Fact]
    public void Descriptor_IsWarningInHandlersCategoryAndDisabledByDefault()
    {
        Assert.Equal("LiteBus.Handlers", DiagnosticDescriptors.ExternalSideEffectInCommandHandler.Category);
        Assert.Equal(DiagnosticSeverity.Warning, DiagnosticDescriptors.ExternalSideEffectInCommandHandler.DefaultSeverity);
        Assert.False(DiagnosticDescriptors.ExternalSideEffectInCommandHandler.IsEnabledByDefault);
    }
}
