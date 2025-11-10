using System.ComponentModel.DataAnnotations;
using LiteBus.Commands.Abstractions;

namespace LiteBus.Samples.Commands;

public sealed class PlaceOrderValidator : ICommandValidator<PlaceOrderCommand>
{
    public Task ValidateAsync(PlaceOrderCommand command, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(command.CustomerId))
        {
            throw new ValidationException("CustomerId cannot be null or empty.");
        }

        if (command.LineItems.Count == 0)
        {
            throw new ValidationException("At least one line item is required.");
        }

        return Task.CompletedTask;
    }
}