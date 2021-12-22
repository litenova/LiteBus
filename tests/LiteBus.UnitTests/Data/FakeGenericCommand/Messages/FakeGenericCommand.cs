using System;
using System.Collections.Generic;
using LiteBus.Commands.Abstractions;
using LiteBus.UnitTests.Data.Global.Commands;

namespace LiteBus.UnitTests.Data.FakeGenericCommand.Messages;

public class FakeGenericCommand<TPayload> : FakeParentCommand, ICommand<FakeGenericCommandResult>
{
}