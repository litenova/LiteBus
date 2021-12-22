using System;
using System.Collections.Generic;
using LiteBus.Commands.Abstractions;
using LiteBus.UnitTests.Data.Global.Commands;

namespace LiteBus.UnitTests.Data.FakeCommand.Messages;

public class FakeCommand : FakeParentCommand, ICommand<FakeCommandResult>
{
}