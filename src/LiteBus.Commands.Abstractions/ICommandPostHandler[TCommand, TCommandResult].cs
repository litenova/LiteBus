using LiteBus.Messaging.Abstractions;

namespace LiteBus.Commands.Abstractions;

/// <summary>
///     Represents a post-handler that executes after a specific command type <typeparamref name="TCommand" /> is
///     processed,
///     with access to the command result of type <typeparamref name="TCommandResult" />.
/// </summary>
/// <typeparam name="TCommand">The specific command type this post-handler targets.</typeparam>
/// <typeparam name="TCommandResult">The type of result produced by the command.</typeparam>
/// <remarks>
///     These post-handlers execute after a command handler has processed a command and produced a result.
///     They have access to both the original command and its result, allowing for operations such as
///     result transformation, logging, caching, or other processing that depends on the command outcome.
/// </remarks>
public interface ICommandPostHandler<in TCommand, in TCommandResult> : IRegistrableCommandConstruct, IAsyncMessagePostHandler<TCommand, TCommandResult>
    where TCommand : ICommand<TCommandResult>
    where TCommandResult : notnull;