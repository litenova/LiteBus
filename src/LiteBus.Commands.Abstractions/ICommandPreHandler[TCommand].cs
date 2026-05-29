using LiteBus.Messaging.Abstractions;

namespace LiteBus.Commands.Abstractions;

/// <summary>
///     Represents an action that is executed on <typeparamref name="TCommand" /> pre-handle phase.
/// </summary>
/// <typeparam name="TCommand">The specific command type this pre-handler targets.</typeparam>
public interface ICommandPreHandler<in TCommand> : IRegistrableCommandConstruct, IAsyncMessagePreHandler<TCommand> where TCommand : ICommand;