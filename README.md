# LiteBus
![.NET 5 CI](https://github.com/arishk/LiteBus/actions/workflows/dotnet-core.yml/badge.svg?branch=main)
[![NuGet](https://img.shields.io/nuget/vpre/LiteBus.svg)](https://www.nuget.org/packages/LiteBus)



A liteweight and easy to use in-process mediator to implement CQRS

* Written in .NET 5
* No Dependencies
* Minimum Reflection Usage (only for registering handlers)
* Multiple Messaging Type
    * Commands
    * Queries
    * Events
* Supports Streaming Queries (IAsyncEnumerable)
* Supports Both Command with Result and Without Result

## Installation and Configuration 

Depending on your usage, follow one of the guidelines below.

### ASP.NET Core

Install with NuGet:

```
Install-Package LiteBus
Install-Package LiteBus.Extensions.MicrosoftDependencyInjection
```

or with .NET CLI:

```
dotnet add package LiteBus
dotnet add package LiteBus.Extensions.MicrosoftDependencyInjection
```

and configure the LiteBus as below in the `ConfigureServices` method of `Startup.cs`:

```c#
services.AddLiteBus(builder =>
{
    builder.Register(typeof(Startup).Assembly);
});
```

## Message Handling Usages

The following examples demonstrates the usages of LiteBus.

### Commands

Inject ``ICommandMediator`` to your class to send your commands.

```c#
    // A command without result
    public class CreateColorCommand : ICommand
    {
        public string ColorName { get; set; }
    }

    // The handler
    public class CreateColorCommandHandler : ICommandHandler<CreateColorCommand>
    {
        public Task Handle(CreateColorCommand command)
        {
            // Proccess the command
        }
    }
```
```c#
    // A command with result
    public class CreateUserCommand : ICommand<int>
    {
        public string Name { get; set; }
    }
    
    // The handler
    public class CreateUserCommandHandler : ICommandHandler<CreateUserCommand, int>
    {
        public Task<int> Handle(CreateUserCommand command)
        {
            return Task.FromResult(1);
        }
    }
```

### Queries

Inject ``IQueryMediator`` to your class to query data.

```C#
    // A query
    public class ColorQuery : IQuery<ColorReadModel>
    {
        public int Id { get; set; }
    }
    
    // The handler
    public class ColorQueryHandler : IQueryHandler<ColorQuery, ColorReadModel>
    {
        public Task<ColorReadModel> Handle(ColorQuery query)
        {
            // Process the query
        }
    }
```
```c#
    // A stream query
    public class ColorStreamQuery : IStreamQuery<ColorReadModel>
    {
        public int Id { get; set; }
    }
    
    // The handler
    public class ColorStreamQueryHandler : IStreamQueryHandler<ColorStreamQuery, ColorReadModel>
    {
        public IAsyncEnumerable<ColorReadModel> Handle(ColorStreamQuery query)
        {
            // Process the query
        }
    }
```

### Events

Inject ``IEventMediator`` or ``IEventPublisher`` to your class to publish events.

```c#
    // An event
    public class ColorCreatedEvent : IEvent
    {
        public string ColorName { get; set; }
    }
    
    // The first handler
    public class ColorCreatedEventHandler1 : IEventHandler<ColorCreatedEvent>
    {
        public Task Handle(ColorCreatedEvent @event)
        {
            // process the event
        }
    }

    // The second handler
    public class ColorCreatedEventHandler2 : IEventHandler<ColorCreatedEvent>
    {
        public Task HandleAsync(ColorCreatedEvent input, CancellationToken cancellationToken = default)
        {
            // process the event
        }
    }
```

### Plain Messages (To be added in later versions)

You can send any object as a message if the object has any associated handlers.
Inject ``IMessageMediator`` to your class to publish events.

```c#
    // A plain message
    public class PlainMessage
    {
        public int Number { get; set; }
    }
    
    // The handler to handle the message with result
    public class PlainMessageHandler : IMessageHandler<PlainMessage, Task<int>>
    {
        public Task<int> Handle(PlainMessage message)
        {
            // process the message
        }
    }
    
    // The handler to handle the message without result
    public class PlainMessageHandler2 : IMessageHandler<PlainMessage>
    {
        public Task Handle(PlainMessage message)
        {
            // process the message
        }
    }
```

## Hooks

Hooks allow you to execute an action in a certain stage of message handling. Currently, hooks are only available for commands.

* **PostHandleHooks**: execute an action after a message is handled
* **PreHandleHooks** (to be added in later versions): execute an action before a message is handled

### Command Hooks
```c#

    // Exectues an action after each command is handled
    public class GlobalCommandPostHandleHook : ICommandPostHandleHook
    {
        public Task ExecuteAsync(IBaseCommand message)
        {
            Debug.WriteLine("GlobalCommandPostHandleHook executed!");
            return Task.CompletedTask;
        }
    }

    // Exectues an action after an specific command is handled
    public class CreateColorCommandPostHandleHook : ICommandPostHandleHook<CreateColorCommand>
    {
        public Task ExecuteAsync(CreateColorCommand message)
        {
            Debug.WriteLine("CreateColorCommandPostHandleHook executed!");
            return Task.CompletedTask;
        }
    }
```
## Inheritance
LiteBus uses the actual type of messages to find the corresponding handlers. Consider the following example:

```c#
    // The base command
    public class CreateFileCommand : ICommand
    {
        public string Name { get; set; }
    }
    
    // The derived command
    public class CreateFileCommand : CreateFileCommand
    {
        public int Width { get; set; }
        
        public int Height { get; set; }
    }
    
    // The base command handler
    public class CreateFileCommandHandler : ICommandHandler<CreateFileCommand>
    {
        public Task Handle(CreateFileCommand command)
        {
            throw new NotImplementedException();
        }
    }
    
    // The derived command handler
    public class CreateImageCommandHandler : ICommandHandler<CreateImageCommand>
    {
        public Task Handle(CreateImageCommand command)
        {
            throw new NotImplementedException();
        }
    }
```

In this example, If you send the ``CreateImageCommand`` as ``CreateFileCommand``, the LiteBus will deliver the command to ``CreateImageCommandHandler``.

```c#
    CreateFileCommand command = new CreateImageCommand();
    
    _mediator.SendAsync(command);
```

