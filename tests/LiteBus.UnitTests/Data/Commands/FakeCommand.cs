using System;
using System.Collections.Generic;
using LiteBus.Commands.Abstractions;

namespace LiteBus.UnitTests.Data.Commands;

public class FakeCommand : ICommand<FakeCommandResult>
{
    public List<Type> ExecutedTypes { get; } = new();
}