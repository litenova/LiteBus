using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Registry;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Mediator.UnitTests.Pipeline;

/// <summary>
///     Verifies that the pipeline plan orders direct and indirect handlers the way the pipeline runs them.
/// </summary>
[Collection("Sequential")]
public sealed class PipelineExplainerOrderingTests : LiteBusTestBase
{
    [Fact]
    public void Pre_handlers_list_indirect_before_direct_and_post_handlers_list_direct_before_indirect()
    {
        var provider = new ServiceCollection()
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(_ => { });

                registry.AddCommands(builder =>
                {
                    builder.Register<WrappedCommand>();
                    builder.Register<WrappedCommandHandler>();
                    builder.Register<DirectWrappedPreHandler>();
                    builder.Register<IndirectWrappedPreHandler>();
                    builder.Register<DirectWrappedPostHandler>();
                    builder.Register<IndirectWrappedPostHandler>();
                });
            })
            .BuildServiceProvider();

        var plan = provider.GetRequiredService<IMessageReader>().Explain(typeof(WrappedCommand));

        plan.Steps.Where(step => step.Stage == "prehandler")
            .Select(step => step.HandlerType.Name)
            .Should().Equal(nameof(IndirectWrappedPreHandler), nameof(DirectWrappedPreHandler));

        plan.Steps.Where(step => step.Stage == "prehandler")
            .Select(step => step.IsIndirect)
            .Should().Equal(true, false);

        plan.Steps.Where(step => step.Stage == "post")
            .Select(step => step.HandlerType.Name)
            .Should().Equal(nameof(DirectWrappedPostHandler), nameof(IndirectWrappedPostHandler));

        plan.Steps.Where(step => step.Stage == "post")
            .Select(step => step.IsIndirect)
            .Should().Equal(false, true);
    }

    private sealed class WrappedCommand : ICommand;

    private sealed class WrappedCommandHandler : ICommandHandler<WrappedCommand>
    {
        public Task HandleAsync(WrappedCommand message, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class DirectWrappedPreHandler : ICommandPreHandler<WrappedCommand>
    {
        public Task PreHandleAsync(WrappedCommand message, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class IndirectWrappedPreHandler : ICommandPreHandler<ICommand>
    {
        public Task PreHandleAsync(ICommand message, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class DirectWrappedPostHandler : ICommandPostHandler<WrappedCommand>
    {
        public Task PostHandleAsync(WrappedCommand message, object? messageResult, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class IndirectWrappedPostHandler : ICommandPostHandler<ICommand>
    {
        public Task PostHandleAsync(ICommand message, object? messageResult, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }
}
