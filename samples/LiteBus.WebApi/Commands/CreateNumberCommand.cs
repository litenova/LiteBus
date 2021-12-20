using LiteBus.Commands.Abstractions;

namespace LiteBus.WebApi.Commands;

public class CreateNumberCommand : ICommand
{
    public decimal Number { get; set; }
}