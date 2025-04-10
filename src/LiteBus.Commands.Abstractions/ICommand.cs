namespace LiteBus.Commands.Abstractions;

/// <summary>
///     Represents a command in the CQRS pattern. Commands are used to request a change in the system state
///     without expecting any value to be returned.
/// </summary>
/// <remarks>
///     Commands follow the Command-Query Responsibility Segregation (CQRS) pattern and typically represent
///     a single, atomic operation that modifies state. Commands should be processed exactly once by a single handler.
/// </remarks>
public interface ICommand : IRegistrableCommandConstruct;