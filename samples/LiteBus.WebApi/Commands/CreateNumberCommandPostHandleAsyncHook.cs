using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Commands.Abstractions;
using LiteBus.Events.Abstractions;
using LiteBus.WebApi.Events;

namespace LiteBus.WebApi.Commands
{
    public class CreateNumberCommandPostHandleAsyncHook : ICommandPostHandleAsyncHook<CreateNumberCommand>
    {
        private readonly IEventMediator _eventMediator;

        public CreateNumberCommandPostHandleAsyncHook(IEventMediator eventMediator)
        {
            _eventMediator = eventMediator;
        }

        public Task ExecuteAsync(CreateNumberCommand message, CancellationToken cancellationToken = default)
        {
            Debug.WriteLine($"{nameof(CreateNumberCommandPostHandleAsyncHook)} executed!");

            return _eventMediator.PublishAsync(new NumberCreatedEvent
            {
                Number = message.Number
            }, cancellationToken);
        }
    }
}