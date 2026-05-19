using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging.Abstractions;
using LiteBus.Queries;
using LiteBus.Queries.Abstractions;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.MessageModule.UnitTests;

/// <summary>
///     Tests that post-handlers can override the result returned to the caller by writing a replacement
///     value to <see cref="AmbientExecutionContext.Current" />'s <c>MessageResult</c> property.
/// </summary>
[Collection("Sequential")]
public sealed class PostHandlerResultOverrideTests : LiteBusTestBase
{
    [Fact]
    public async Task Send_CommandWithResult_PostHandlerOverridesResult()
    {
        // ARRANGE
        var serviceProvider = new ServiceCollection()
            .AddLiteBus(configuration =>
            {
                configuration.AddCommandModule(builder =>
                {
                    builder.Register<OverrideResultCommandHandler>();
                    builder.Register<OverrideResultCommandPostHandler>();
                });
            })
            .BuildServiceProvider();

        var commandMediator = serviceProvider.GetRequiredService<ICommandMediator>();
        var command = new OverrideResultCommand();

        // ACT
        var result = await commandMediator.SendAsync(command);

        // ASSERT
        result.Should().Be("overridden");
    }

    [Fact]
    public async Task Send_CommandWithResult_WhenPostHandlerDoesNotOverride_ReturnsOriginalResult()
    {
        // ARRANGE
        var serviceProvider = new ServiceCollection()
            .AddLiteBus(configuration =>
            {
                configuration.AddCommandModule(builder =>
                {
                    builder.Register<NoOverrideCommandHandler>();
                    builder.Register<NoOverrideCommandPostHandler>();
                });
            })
            .BuildServiceProvider();

        var commandMediator = serviceProvider.GetRequiredService<ICommandMediator>();
        var command = new NoOverrideCommand();

        // ACT
        var result = await commandMediator.SendAsync(command);

        // ASSERT
        result.Should().Be("original");
    }

    [Fact]
    public async Task Send_CommandWithMultiplePostHandlers_LastWriteWins()
    {
        // ARRANGE
        var serviceProvider = new ServiceCollection()
            .AddLiteBus(configuration =>
            {
                configuration.AddCommandModule(builder =>
                {
                    builder.Register<MultiOverrideCommandHandler>();
                    builder.Register<MultiOverrideCommandFirstPostHandler>();
                    builder.Register<MultiOverrideCommandSecondPostHandler>();
                });
            })
            .BuildServiceProvider();

        var commandMediator = serviceProvider.GetRequiredService<ICommandMediator>();
        var command = new MultiOverrideCommand();

        // ACT
        var result = await commandMediator.SendAsync(command);

        // ASSERT
        result.Should().Be("second-override");
    }

    [Fact]
    public async Task Query_WithPostHandlerOverride_ReturnsOverriddenResult()
    {
        // ARRANGE
        var serviceProvider = new ServiceCollection()
            .AddLiteBus(configuration =>
            {
                configuration.AddQueryModule(builder =>
                {
                    builder.Register<OverrideResultQueryHandler>();
                    builder.Register<OverrideResultQueryPostHandler>();
                });
            })
            .BuildServiceProvider();

        var queryMediator = serviceProvider.GetRequiredService<IQueryMediator>();
        var query = new OverrideResultQuery();

        // ACT
        var result = await queryMediator.QueryAsync(query);

        // ASSERT
        result.Should().Be(99);
    }

    [Fact]
    public async Task Send_CommandWithImmutableResult_PostHandlerCanReplaceResult()
    {
        // ARRANGE
        var serviceProvider = new ServiceCollection()
            .AddLiteBus(configuration =>
            {
                configuration.AddCommandModule(builder =>
                {
                    builder.Register<ImmutableResultCommandHandler>();
                    builder.Register<ImmutableResultCommandPostHandler>();
                });
            })
            .BuildServiceProvider();

        var commandMediator = serviceProvider.GetRequiredService<ICommandMediator>();
        var command = new ImmutableResultCommand();

        // ACT
        var result = await commandMediator.SendAsync(command);

        // ASSERT
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be("injected");
    }

    [Fact]
    public async Task Send_VoidCommand_PostHandlerWritingToMessageResult_DoesNotThrow()
    {
        // ARRANGE
        var serviceProvider = new ServiceCollection()
            .AddLiteBus(configuration =>
            {
                configuration.AddCommandModule(builder =>
                {
                    builder.Register<VoidCommandHandler>();
                    builder.Register<VoidCommandPostHandler>();
                });
            })
            .BuildServiceProvider();

        var commandMediator = serviceProvider.GetRequiredService<ICommandMediator>();
        var command = new VoidCommand();

        // ACT
        var act = async () => await commandMediator.SendAsync(command);

        // ASSERT
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Send_TwoCommandsSequentially_OverrideDoesNotLeakBetweenCalls()
    {
        // ARRANGE
        var serviceProvider = new ServiceCollection()
            .AddLiteBus(configuration =>
            {
                configuration.AddCommandModule(builder =>
                {
                    builder.Register<FirstSequentialCommandHandler>();
                    builder.Register<FirstSequentialCommandPostHandler>();
                    builder.Register<SecondSequentialCommandHandler>();
                });
            })
            .BuildServiceProvider();

        var commandMediator = serviceProvider.GetRequiredService<ICommandMediator>();

        // ACT
        var firstResult = await commandMediator.SendAsync(new FirstSequentialCommand());
        var secondResult = await commandMediator.SendAsync(new SecondSequentialCommand());

        // ASSERT
        firstResult.Should().Be("first-override");
        secondResult.Should().Be("second-original");
    }

    // -------------------------------------------------------------------------
    // Test Case 1: Post-handler overrides command result
    // -------------------------------------------------------------------------

    private sealed record OverrideResultCommand : ICommand<string>;

    private sealed class OverrideResultCommandHandler : ICommandHandler<OverrideResultCommand, string>
    {
        public Task<string> HandleAsync(OverrideResultCommand message, CancellationToken cancellationToken = default)
            => Task.FromResult("original");
    }

    private sealed class OverrideResultCommandPostHandler : ICommandPostHandler<OverrideResultCommand, string>
    {
        public Task PostHandleAsync(OverrideResultCommand message, string? messageResult, CancellationToken cancellationToken = default)
        {
            AmbientExecutionContext.Current.MessageResult = "overridden";
            return Task.CompletedTask;
        }
    }

    // -------------------------------------------------------------------------
    // Test Case 2: Post-handler does not override when MessageResult is null
    // -------------------------------------------------------------------------

    private sealed record NoOverrideCommand : ICommand<string>;

    private sealed class NoOverrideCommandHandler : ICommandHandler<NoOverrideCommand, string>
    {
        public Task<string> HandleAsync(NoOverrideCommand message, CancellationToken cancellationToken = default)
            => Task.FromResult("original");
    }

    private sealed class NoOverrideCommandPostHandler : ICommandPostHandler<NoOverrideCommand, string>
    {
        public Task PostHandleAsync(NoOverrideCommand message, string? messageResult, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    // -------------------------------------------------------------------------
    // Test Case 3: Last post-handler in a chain wins
    // -------------------------------------------------------------------------

    private sealed record MultiOverrideCommand : ICommand<string>;

    private sealed class MultiOverrideCommandHandler : ICommandHandler<MultiOverrideCommand, string>
    {
        public Task<string> HandleAsync(MultiOverrideCommand message, CancellationToken cancellationToken = default)
            => Task.FromResult("original");
    }

    [HandlerPriority(1)]
    private sealed class MultiOverrideCommandFirstPostHandler : ICommandPostHandler<MultiOverrideCommand, string>
    {
        public Task PostHandleAsync(MultiOverrideCommand message, string? messageResult, CancellationToken cancellationToken = default)
        {
            AmbientExecutionContext.Current.MessageResult = "first-override";
            return Task.CompletedTask;
        }
    }

    [HandlerPriority(2)]
    private sealed class MultiOverrideCommandSecondPostHandler : ICommandPostHandler<MultiOverrideCommand, string>
    {
        public Task PostHandleAsync(MultiOverrideCommand message, string? messageResult, CancellationToken cancellationToken = default)
        {
            AmbientExecutionContext.Current.MessageResult = "second-override";
            return Task.CompletedTask;
        }
    }

    // -------------------------------------------------------------------------
    // Test Case 4: Post-handler override works with query
    // -------------------------------------------------------------------------

    private sealed record OverrideResultQuery : IQuery<int>;

    private sealed class OverrideResultQueryHandler : IQueryHandler<OverrideResultQuery, int>
    {
        public Task<int> HandleAsync(OverrideResultQuery message, CancellationToken cancellationToken = default)
            => Task.FromResult(42);
    }

    private sealed class OverrideResultQueryPostHandler : IQueryPostHandler<OverrideResultQuery, int>
    {
        public Task PostHandleAsync(OverrideResultQuery message, int messageResult, CancellationToken cancellationToken = default)
        {
            AmbientExecutionContext.Current.MessageResult = 99;
            return Task.CompletedTask;
        }
    }

    // -------------------------------------------------------------------------
    // Test Case 5: Post-handler override works with immutable result type
    // -------------------------------------------------------------------------

    public sealed record ImmutableResult(bool IsSuccess, string? Error = null)
    {
        public ImmutableResult WithError(string error) => new(false, error);
    }

    private sealed record ImmutableResultCommand : ICommand<ImmutableResult>;

    private sealed class ImmutableResultCommandHandler : ICommandHandler<ImmutableResultCommand, ImmutableResult>
    {
        public Task<ImmutableResult> HandleAsync(ImmutableResultCommand message, CancellationToken cancellationToken = default)
            => Task.FromResult(new ImmutableResult(true));
    }

    private sealed class ImmutableResultCommandPostHandler : ICommandPostHandler<ImmutableResultCommand, ImmutableResult>
    {
        public Task PostHandleAsync(ImmutableResultCommand message, ImmutableResult? messageResult, CancellationToken cancellationToken = default)
        {
            if (messageResult is { IsSuccess: true })
            {
                AmbientExecutionContext.Current.MessageResult = messageResult.WithError("injected");
            }

            return Task.CompletedTask;
        }
    }

    // -------------------------------------------------------------------------
    // Test Case 6: Override does not affect the void command pipeline
    // -------------------------------------------------------------------------

    private sealed record VoidCommand : ICommand;

    private sealed class VoidCommandHandler : ICommandHandler<VoidCommand>
    {
        public Task HandleAsync(VoidCommand message, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class VoidCommandPostHandler : ICommandPostHandler<VoidCommand>
    {
        public Task PostHandleAsync(VoidCommand message, object? messageResult, CancellationToken cancellationToken = default)
        {
            // Writing to MessageResult on a void command should be silently ignored
            // because the void mediation strategy does not read this property.
            AmbientExecutionContext.Current.MessageResult = "should-be-ignored";
            return Task.CompletedTask;
        }
    }

    // -------------------------------------------------------------------------
    // Test Case 7: Override is not visible across independent mediator calls
    // -------------------------------------------------------------------------

    private sealed record FirstSequentialCommand : ICommand<string>;

    private sealed class FirstSequentialCommandHandler : ICommandHandler<FirstSequentialCommand, string>
    {
        public Task<string> HandleAsync(FirstSequentialCommand message, CancellationToken cancellationToken = default)
            => Task.FromResult("first-original");
    }

    private sealed class FirstSequentialCommandPostHandler : ICommandPostHandler<FirstSequentialCommand, string>
    {
        public Task PostHandleAsync(FirstSequentialCommand message, string? messageResult, CancellationToken cancellationToken = default)
        {
            AmbientExecutionContext.Current.MessageResult = "first-override";
            return Task.CompletedTask;
        }
    }

    private sealed record SecondSequentialCommand : ICommand<string>;

    private sealed class SecondSequentialCommandHandler : ICommandHandler<SecondSequentialCommand, string>
    {
        public Task<string> HandleAsync(SecondSequentialCommand message, CancellationToken cancellationToken = default)
            => Task.FromResult("second-original");
    }
}
