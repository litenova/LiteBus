using LiteBus.Commands.Abstractions;

namespace LiteBus.WebApi.Commands;

public class CreateNumberCommandWithResult : ICommand<string>
{
    public decimal Number { get; set; }
}