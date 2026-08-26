using LiteBus.Commands.Abstractions;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Mediator.UnitTests.Completion;

/// <summary>
///     A probe command used to prove one class can handle several message types.
/// </summary>
internal sealed class ProbeCommandA : ICommand
{
    /// <summary>
    ///     Gets the stages that ran for this command.
    /// </summary>
    public List<string> Ran { get; } = [];
}

/// <summary>
///     A second probe command with a result, to exercise the other closed post-handler shape.
/// </summary>
internal sealed class ProbeCommandB : ICommand<string>
{
    /// <summary>
    ///     Gets the stages that ran for this command.
    /// </summary>
    public List<string> Ran { get; } = [];
}

/// <summary>
///     Implements pre-handler and post-handler contracts for two message types and two result shapes.
/// </summary>
/// <remarks>
///     This type compiling and dispatching correctly is the regression test for pipeline dispatch. If the pipeline
///     invoked these stages through a default interface method on their non-generic contract, this class would have no
///     most-specific implementation and would fail to compile with CS8705. Dispatch is driven by the handler descriptor
///     instead, which records the closed contract at registration. The completion stage is included because it has the
///     same problem and needs the same answer.
/// </remarks>
internal sealed class MultiContractHandler :
    ICommandPreHandler<ProbeCommandA>,
    ICommandPreHandler<ProbeCommandB>,
    ICommandPostHandler<ProbeCommandA>,
    ICommandPostHandler<ProbeCommandB, string>,
    ICommandCompletionHandler<ProbeCommandA>,
    ICommandCompletionHandler<ProbeCommandB, string>
{
    /// <inheritdoc />
    public Task HandleCompletionAsync(
        MessageCompletionContext<ProbeCommandA> context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.Message.Ran.Add("done:A");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task HandleCompletionAsync(
        MessageCompletionContext<ProbeCommandB, string> context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.Message.Ran.Add($"done:B:{context.MessageResult}");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task PreHandleAsync(ProbeCommandA message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        message.Ran.Add("pre:A");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task PreHandleAsync(ProbeCommandB message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        message.Ran.Add("pre:B");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task PostHandleAsync(ProbeCommandA message, object? messageResult, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        message.Ran.Add("post:A");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task PostHandleAsync(ProbeCommandB message, string? messageResult, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        message.Ran.Add($"post:B:{messageResult}");
        return Task.CompletedTask;
    }
}

/// <summary>
///     Handles the first probe command.
/// </summary>
internal sealed class ProbeCommandAHandler : ICommandHandler<ProbeCommandA>
{
    /// <inheritdoc />
    public Task HandleAsync(ProbeCommandA message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        message.Ran.Add("main:A");
        return Task.CompletedTask;
    }
}

/// <summary>
///     Handles the second probe command.
/// </summary>
internal sealed class ProbeCommandBHandler : ICommandHandler<ProbeCommandB, string>
{
    /// <inheritdoc />
    public Task<string> HandleAsync(ProbeCommandB message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        message.Ran.Add("main:B");
        return Task.FromResult("done");
    }
}
