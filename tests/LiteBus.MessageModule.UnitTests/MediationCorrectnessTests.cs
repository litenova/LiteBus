using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Queries;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Queries.Abstractions;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.MessageModule.UnitTests;

/// <summary>
///     Regression tests for mediation scope retention, cancellation propagation, and error-handler semantics.
/// </summary>
[Collection("Sequential")]
public sealed class MediationCorrectnessTests : LiteBusTestBase
{
    [Fact]
    public async Task Send_Command_ShouldRetainAmbientScopeUntilHandlerContinuationCompletes()
    {
        var serviceProvider = new ServiceCollection().AddLiteBus(registry =>
        {
            registry.AddMessageModule(_ => { });
            registry.AddCommandModule(builder => builder.Register<DeferredAmbientCommandHandler>());
        }).BuildServiceProvider();

        var commandMediator = serviceProvider.GetRequiredService<ICommandMediator>();
        var command = new DeferredAmbientCommand();

        await commandMediator.SendAsync(command);

        command.AmbientAvailableDuringContinuation.Should().BeTrue();
    }

    [Fact]
    public async Task Send_Command_ShouldPassCancellationTokenExplicitlyToHandler()
    {
        using var cts = new CancellationTokenSource();
        var serviceProvider = new ServiceCollection().AddLiteBus(registry =>
        {
            registry.AddMessageModule(_ => { });
            registry.AddCommandModule(builder => builder.Register<CancellationObservingCommandHandler>());
        }).BuildServiceProvider();

        var commandMediator = serviceProvider.GetRequiredService<ICommandMediator>();

        await commandMediator.SendAsync(new CancellationObservingCommand(), cancellationToken: cts.Token);

        CancellationObservingCommandHandler.ReceivedToken.Should().Be(cts.Token);
    }

    [Fact]
    public async Task Send_Command_WithSwallowingErrorHandler_ShouldRethrowByDefault()
    {
        var serviceProvider = new ServiceCollection().AddLiteBus(registry =>
        {
            registry.AddMessageModule(_ => { });
            registry.AddCommandModule(builder =>
            {
                builder.Register<FailingCommandHandler>();
                builder.Register<SwallowingCommandErrorHandler>();
            });
        }).BuildServiceProvider();

        var commandMediator = serviceProvider.GetRequiredService<ICommandMediator>();

        var act = () => commandMediator.SendAsync(new FailingCommand());

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("handler failed");
    }

    [Fact]
    public async Task Send_Command_WithHandledErrorOutcome_ShouldSuppressException()
    {
        var serviceProvider = new ServiceCollection().AddLiteBus(registry =>
        {
            registry.AddMessageModule(_ => { });
            registry.AddCommandModule(builder =>
            {
                builder.Register<FailingCommandHandler>();
                builder.Register<HandledOutcomeCommandErrorHandler>();
            });
        }).BuildServiceProvider();

        var commandMediator = serviceProvider.GetRequiredService<ICommandMediator>();

        await commandMediator.SendAsync(new FailingCommand());
    }

    [Fact]
    public async Task Send_CommandWithResult_WhenErrorHandlerDoesNotSetOutcome_ShouldNotReturnDefaultResult()
    {
        var serviceProvider = new ServiceCollection().AddLiteBus(registry =>
        {
            registry.AddMessageModule(_ => { });
            registry.AddCommandModule(builder =>
            {
                builder.Register<FailingResultCommandHandler>();
                builder.Register<SwallowingCommandErrorHandler>();
            });
        }).BuildServiceProvider();

        var commandMediator = serviceProvider.GetRequiredService<ICommandMediator>();

        var act = () => commandMediator.SendAsync(new FailingResultCommand());

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Send_CommandWithResult_WhenErrorHandlerSetsHandledResult_ShouldReturnFallbackResult()
    {
        var serviceProvider = new ServiceCollection().AddLiteBus(registry =>
        {
            registry.AddMessageModule(_ => { });
            registry.AddCommandModule(builder =>
            {
                builder.Register<FailingResultCommandHandler>();
                builder.Register<TypedHandledResultCommandErrorHandler>();
            });
        }).BuildServiceProvider();

        var commandMediator = serviceProvider.GetRequiredService<ICommandMediator>();

        var result = await commandMediator.SendAsync(new FailingResultCommand());

        result.Should().Be("fallback");
    }

    [Fact]
    public async Task Send_Command_WhenCancelled_ShouldNotInvokeErrorHandler()
    {
        var serviceProvider = new ServiceCollection().AddLiteBus(registry =>
        {
            registry.AddMessageModule(_ => { });
            registry.AddCommandModule(builder =>
            {
                builder.Register<CancellingCommandHandler>();
                builder.Register<RecordingCommandErrorHandler>();
            });
        }).BuildServiceProvider();

        var commandMediator = serviceProvider.GetRequiredService<ICommandMediator>();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => commandMediator.SendAsync(new CancellingCommand(), cancellationToken: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        RecordingCommandErrorHandler.WasInvoked.Should().BeFalse();
    }

    [Fact]
    public async Task Query_CommandHandlerPredicate_ShouldFilterHandlers()
    {
        var serviceProvider = new ServiceCollection().AddLiteBus(registry =>
        {
            registry.AddMessageModule(_ => { });
            registry.AddQueryModule(builder =>
            {
                builder.Register<PrimaryQueryHandler>();
                builder.Register<SecondaryQueryHandler>();
            });
        }).BuildServiceProvider();

        var queryMediator = serviceProvider.GetRequiredService<IQueryMediator>();
        var query = new PredicateQuery();

        var result = await queryMediator.QueryAsync(query, new QueryMediationSettings
        {
            Filters =
            {
                HandlerPredicate = descriptor => descriptor.HandlerType == typeof(PrimaryQueryHandler)
            }
        });

        result.Should().Be("primary");
    }

    private sealed record DeferredAmbientCommand : ICommand
    {
        public bool AmbientAvailableDuringContinuation { get; set; }
    }

    private sealed class DeferredAmbientCommandHandler : ICommandHandler<DeferredAmbientCommand>
    {
        public async Task HandleAsync(DeferredAmbientCommand message, CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            message.AmbientAvailableDuringContinuation = AmbientExecutionContext.HasCurrent;
        }
    }

    private sealed record CancellationObservingCommand : ICommand;

    private sealed class CancellationObservingCommandHandler : ICommandHandler<CancellationObservingCommand>
    {
        public static CancellationToken ReceivedToken { get; private set; }

        public Task HandleAsync(CancellationObservingCommand message, CancellationToken cancellationToken = default)
        {
            ReceivedToken = cancellationToken;
            return Task.CompletedTask;
        }
    }

    private sealed record FailingCommand : ICommand;

    private sealed class FailingCommandHandler : ICommandHandler<FailingCommand>
    {
        public Task HandleAsync(FailingCommand message, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("handler failed");
    }

    private sealed class SwallowingCommandErrorHandler : ICommandErrorHandler
    {
        public Task HandleErrorAsync(
            ICommand message,
            object? messageResult,
            Exception exception,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class HandledOutcomeCommandErrorHandler : ICommandErrorHandler
    {
        public Task HandleErrorAsync(
            ICommand message,
            object? messageResult,
            Exception exception,
            CancellationToken cancellationToken = default)
        {
            if (exception is InvalidOperationException)
            {
                // Typed handlers mutate context through the non-generic path in production handlers.
            }

            return Task.CompletedTask;
        }

        object IMessageErrorHandler.HandleError(MessageErrorContext context)
        {
            context.Outcome = MessageErrorOutcome.Handled;
            return Task.CompletedTask;
        }
    }

    private sealed record FailingResultCommand : ICommand<string>;

    private sealed class FailingResultCommandHandler : ICommandHandler<FailingResultCommand, string>
    {
        public Task<string> HandleAsync(FailingResultCommand message, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("handler failed");
    }

    private sealed class TypedHandledResultCommandErrorHandler : ICommandErrorHandler<FailingResultCommand, string>
    {
        public Task HandleErrorAsync(
            FailingResultCommand message,
            string? messageResult,
            Exception exception,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        object IMessageErrorHandler.HandleError(MessageErrorContext context)
        {
            context.Outcome = MessageErrorOutcome.Handled;
            context.HandledResult = "fallback";
            return Task.CompletedTask;
        }
    }

    private sealed record CancellingCommand : ICommand;

    private sealed class CancellingCommandHandler : ICommandHandler<CancellingCommand>
    {
        public Task HandleAsync(CancellingCommand message, CancellationToken cancellationToken = default)
            => throw new OperationCanceledException(cancellationToken);
    }

    private sealed class RecordingCommandErrorHandler : ICommandErrorHandler
    {
        public static bool WasInvoked { get; private set; }

        public Task HandleErrorAsync(
            ICommand message,
            object? messageResult,
            Exception exception,
            CancellationToken cancellationToken = default)
        {
            WasInvoked = true;
            return Task.CompletedTask;
        }
    }

    private sealed record PredicateQuery : IQuery<string>;

    private sealed class PrimaryQueryHandler : IQueryHandler<PredicateQuery, string>
    {
        public Task<string> HandleAsync(PredicateQuery query, CancellationToken cancellationToken = default)
            => Task.FromResult("primary");
    }

    private sealed class SecondaryQueryHandler : IQueryHandler<PredicateQuery, string>
    {
        public Task<string> HandleAsync(PredicateQuery query, CancellationToken cancellationToken = default)
            => Task.FromResult("secondary");
    }
}
