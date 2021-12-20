using System.Diagnostics;
using System.Threading.Tasks;
using LiteBus.Commands.Abstractions;
using LiteBus.Events.Abstractions;
using LiteBus.Messaging.Abstractions;
using LiteBus.WebApi.Events;

namespace LiteBus.WebApi.Commands;

public class CreateNumberCommandPostHandler : ICommandPostHandler<CreateNumberCommand>
{
    private readonly IEventMediator _eventMediator;

    public CreateNumberCommandPostHandler(IEventMediator eventMediator)
    {
        _eventMediator = eventMediator;
    }

    public Task PostHandleAsync(IHandleContext<CreateNumberCommand> context)
    {
        Debug.WriteLine($"{nameof(CreateNumberCommandPostHandler)} executed!");

        return _eventMediator.PublishAsync(new NumberCreatedEvent
                                           {
                                               Number = context.Message.Number
                                           },
                                           context.CancellationToken);
    }
}