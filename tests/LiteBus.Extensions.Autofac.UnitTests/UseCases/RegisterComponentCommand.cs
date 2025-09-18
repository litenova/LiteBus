using LiteBus.Commands.Abstractions;

namespace LiteBus.Extensions.Autofac.UnitTests.UseCases;

/// <summary>
/// A test command used to verify the Autofac registration and execution pipeline.
/// </summary>
public sealed class RegisterComponentCommand : ICommand
{
    public List<Type> ExecutedHandlers { get; } = new();
}