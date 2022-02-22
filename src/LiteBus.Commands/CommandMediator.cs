using System.Threading;
using System.Threading.Tasks;
using LiteBus.Commands.Abstractions;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Workflows.Discovery;
using LiteBus.Messaging.Workflows.Execution;
using LiteBus.Messaging.Workflows.Execution.Handle;

namespace LiteBus.Commands;

/// <inheritdoc cref="ICommandMediator" />
public class CommandMediator : ICommandMediator
{
    private readonly IMediator _mediator;

    public CommandMediator(IMediator mediator)
    {
        _mediator = mediator;
    }

    public async Task SendAsync(ICommand command,
                                CancellationToken cancellationToken = default)
    {
        var executionWorkflow = new SingleAsyncHandlerExecutionWorkflow<ICommand>(cancellationToken);

        var findStrategy = new ActualTypeOrFirstAssignableTypeDiscoveryWorkflow();

        await _mediator.Mediate(command, findStrategy, executionWorkflow);
    }

    public void Send(ICommand command)
    {
        var executionWorkflow = new SingleSyncHandlerExecutionWorkflow<ICommand>();

        var findStrategy = new ActualTypeOrFirstAssignableTypeDiscoveryWorkflow();

        _mediator.Mediate(command, findStrategy, executionWorkflow);
    }

    public async Task<TCommandResult> SendAsync<TCommandResult>(ICommand<TCommandResult> command,
                                                                CancellationToken cancellationToken = default)
    {
        var executionWorkflow =
            new SingleAsyncHandlerExecutionWorkflow<ICommand<TCommandResult>, TCommandResult>(cancellationToken);

        var findStrategy = new ActualTypeOrFirstAssignableTypeDiscoveryWorkflow();

        return await _mediator.Mediate(command, findStrategy, executionWorkflow);
    }

    public TCommandResult Send<TCommandResult>(ICommand<TCommandResult> command)
    {
        var executionWorkflow = new SingleSyncHandlerExecutionWorkflow<ICommand<TCommandResult>, TCommandResult>();

        var findStrategy = new ActualTypeOrFirstAssignableTypeDiscoveryWorkflow();

        return _mediator.Mediate(command, findStrategy, executionWorkflow);
    }
}