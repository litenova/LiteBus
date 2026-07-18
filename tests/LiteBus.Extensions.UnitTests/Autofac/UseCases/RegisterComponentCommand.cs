using LiteBus.Commands.Abstractions;

namespace LiteBus.Extensions.UnitTests.Autofac.UseCases;

/// <summary>
///     A test command used to verify the Autofac registration and execution pipeline.
/// </summary>
public sealed class RegisterComponentCommand : ICommand
{
    public List<Type> ExecutedHandlers { get; } = new();
}
