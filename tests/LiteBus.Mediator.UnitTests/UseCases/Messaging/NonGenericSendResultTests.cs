using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Mediator.UnitTests.UseCases.Messaging;

/// <summary>
///     Verifies that a result-bearing command sent through a reference typed as <see cref="ICommand" /> still runs
///     the result-bearing pipeline, so post-handlers, error handlers and completion handlers see the real result.
/// </summary>
[Collection("Sequential")]
public sealed class NonGenericSendResultTests : LiteBusTestBase
{
    [Fact]
    public async Task SendAsync_through_ICommand_post_handler_receives_the_result()
    {
        var observer = new NonGenericSendObserver();
        var provider = Build(observer, typeof(GreetingCommand), typeof(GreetingCommandHandler), typeof(GreetingPostHandler));

        ICommand command = new GreetingCommand();

        await provider.GetRequiredService<ICommandMediator>().SendAsync(command).ConfigureAwait(false);

        observer.PostHandlerValue.Should().Be("handled");
    }

    [Fact]
    public async Task SendAsync_through_ICommand_completion_handler_sees_the_recovered_result()
    {
        var observer = new NonGenericSendObserver();
        var provider = Build(
            observer,
            typeof(FragileResultCommand),
            typeof(FragileResultCommandHandler),
            typeof(FragileResultCommandErrorHandler),
            typeof(FragileResultCompletionHandler));

        ICommand command = new FragileResultCommand();

        await provider.GetRequiredService<ICommandMediator>().SendAsync(command).ConfigureAwait(false);

        observer.CompletionHasResult.Should().BeTrue();
        observer.CompletionValue.Should().Be("recovered");
    }

    [Fact]
    public async Task SendAsync_through_ICommand_value_type_result_reaches_the_post_handler()
    {
        var observer = new NonGenericSendObserver();
        var provider = Build(observer, typeof(AnswerCommand), typeof(AnswerCommandHandler), typeof(AnswerPostHandler));

        ICommand command = new AnswerCommand();

        await provider.GetRequiredService<ICommandMediator>().SendAsync(command).ConfigureAwait(false);

        observer.PostHandlerValue.Should().Be(42);
    }

    [Fact]
    public async Task TrySendAsync_through_ICommand_reports_success_and_runs_the_result_pipeline()
    {
        var observer = new NonGenericSendObserver();
        var provider = Build(observer, typeof(GreetingCommand), typeof(GreetingCommandHandler), typeof(GreetingPostHandler));

        ICommand command = new GreetingCommand();

        var result = await provider.GetRequiredService<ICommandMediator>().TrySendAsync(command).ConfigureAwait(false);

        result.IsSuccess.Should().BeTrue();
        result.Outcome.Should().Be(MediationOutcome.Succeeded);
        observer.PostHandlerValue.Should().Be("handled");
    }

    [Fact]
    public async Task SendAsync_through_ICommand_plain_command_still_runs_the_pipeline()
    {
        var observer = new NonGenericSendObserver();
        var provider = Build(observer, typeof(PingCommand), typeof(PingCommandHandler), typeof(PingPostHandler));

        ICommand command = new PingCommand();

        await provider.GetRequiredService<ICommandMediator>().SendAsync(command).ConfigureAwait(false);

        observer.MainHandlerRan.Should().BeTrue();
        observer.PostHandlerRan.Should().BeTrue();
    }

    /// <summary>
    ///     Builds a provider registering only the command types one test needs, sharing the observer with the handlers.
    /// </summary>
    /// <param name="observer">The observer shared with the handlers.</param>
    /// <param name="commandTypes">The command and handler types to register.</param>
    /// <returns>The configured service provider.</returns>
    private static ServiceProvider Build(NonGenericSendObserver observer, params Type[] commandTypes)
    {
        var services = new ServiceCollection();
        services.AddSingleton(observer);

        return services
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(_ => { });

                registry.AddCommands(builder =>
                {
                    foreach (var commandType in commandTypes)
                    {
                        builder.Register(commandType);
                    }
                });
            })
            .BuildServiceProvider();
    }

    private sealed record GreetingCommand : ICommand<string>;

    private sealed class GreetingCommandHandler : ICommandHandler<GreetingCommand, string>
    {
        public Task<string> HandleAsync(GreetingCommand message, CancellationToken cancellationToken = default)
        {
            return Task.FromResult("handled");
        }
    }

    private sealed class GreetingPostHandler : ICommandPostHandler<GreetingCommand, string>
    {
        private readonly NonGenericSendObserver _observer;

        public GreetingPostHandler(NonGenericSendObserver observer)
        {
            _observer = observer;
        }

        public Task PostHandleAsync(GreetingCommand message, string? messageResult, CancellationToken cancellationToken = default)
        {
            _observer.PostHandlerValue = messageResult;
            return Task.CompletedTask;
        }
    }

    private sealed record FragileResultCommand : ICommand<string>;

    private sealed class FragileResultCommandHandler : ICommandHandler<FragileResultCommand, string>
    {
        public Task<string> HandleAsync(FragileResultCommand message, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("handler failed");
        }
    }

    private sealed class FragileResultCommandErrorHandler : ICommandErrorHandler<FragileResultCommand, string>
    {
        public Task HandleErrorAsync(MessageErrorContext<FragileResultCommand, string> context, CancellationToken cancellationToken = default)
        {
            context.HandledResult = "recovered";
            context.Outcome = MessageErrorOutcome.Handled;
            return Task.CompletedTask;
        }
    }

    private sealed class FragileResultCompletionHandler : ICommandCompletionHandler<FragileResultCommand, string>
    {
        private readonly NonGenericSendObserver _observer;

        public FragileResultCompletionHandler(NonGenericSendObserver observer)
        {
            _observer = observer;
        }

        public Task HandleCompletionAsync(MessageCompletionContext<FragileResultCommand, string> context, CancellationToken cancellationToken)
        {
            _observer.CompletionHasResult = context.HasResult;
            _observer.CompletionValue = context.MessageResult;
            return Task.CompletedTask;
        }
    }

    private sealed record AnswerCommand : ICommand<int>;

    private sealed class AnswerCommandHandler : ICommandHandler<AnswerCommand, int>
    {
        public Task<int> HandleAsync(AnswerCommand message, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(42);
        }
    }

    private sealed class AnswerPostHandler : ICommandPostHandler<AnswerCommand, int>
    {
        private readonly NonGenericSendObserver _observer;

        public AnswerPostHandler(NonGenericSendObserver observer)
        {
            _observer = observer;
        }

        public Task PostHandleAsync(AnswerCommand message, int messageResult, CancellationToken cancellationToken = default)
        {
            _observer.PostHandlerValue = messageResult;
            return Task.CompletedTask;
        }
    }

    private sealed record PingCommand : ICommand;

    private sealed class PingCommandHandler : ICommandHandler<PingCommand>
    {
        private readonly NonGenericSendObserver _observer;

        public PingCommandHandler(NonGenericSendObserver observer)
        {
            _observer = observer;
        }

        public Task HandleAsync(PingCommand message, CancellationToken cancellationToken = default)
        {
            _observer.MainHandlerRan = true;
            return Task.CompletedTask;
        }
    }

    private sealed class PingPostHandler : ICommandPostHandler<PingCommand>
    {
        private readonly NonGenericSendObserver _observer;

        public PingPostHandler(NonGenericSendObserver observer)
        {
            _observer = observer;
        }

        public Task PostHandleAsync(PingCommand message, object? messageResult, CancellationToken cancellationToken = default)
        {
            _observer.PostHandlerRan = true;
            return Task.CompletedTask;
        }
    }
}

/// <summary>
///     Records what the pipeline stages observed on the non-generic send path, so the tests can assert the
///     result-bearing pipeline ran.
/// </summary>
internal sealed class NonGenericSendObserver
{
    /// <summary>
    ///     Gets or sets the result the post-handler received.
    /// </summary>
    public object? PostHandlerValue { get; set; }

    /// <summary>
    ///     Gets or sets a value indicating whether the completion handler saw a result.
    /// </summary>
    public bool CompletionHasResult { get; set; }

    /// <summary>
    ///     Gets or sets the result the completion handler saw.
    /// </summary>
    public object? CompletionValue { get; set; }

    /// <summary>
    ///     Gets or sets a value indicating whether the main handler ran.
    /// </summary>
    public bool MainHandlerRan { get; set; }

    /// <summary>
    ///     Gets or sets a value indicating whether the post-handler ran.
    /// </summary>
    public bool PostHandlerRan { get; set; }
}
