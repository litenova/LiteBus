# LiteBus
[![.NET 5 CI](https://github.com/arishk/LiteBus/actions/workflows/dotnet-core.yml/badge.svg?branch=main)
[![NuGet](https://img.shields.io/nuget/vpre/LiteBus.svg)](https://www.nuget.org/packages/LiteBus)



A liteweight and easy to use in-process mediator to implement CQRS

* Written in .NET 5
* No Dependencies
* Multiple Messaging Type
    * Commands
    * Queries
    * Events
* Supports Stream Query (IAsyncEnumerable)
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
    builder.RegisterHandlers(typeof(Startup).Assembly);
});
```

## Message Handling Usages

The following examples shows the usages of LiteBus.

### Commands

Inject ``ICommandMediator`` to your class to send your commands.

```c#

    // Simple Command
    public class CreateColorCommand : ICommand
    {
        public string ColorName { get; set; }
    }

    // Simple Command Handler
    public class CreateColorCommandHandler : ICommandHandler<CreateColorCommand>
    {
        public Task HandleAsync(CreateColorCommand input, CancellationToken cancellationToken = default)
        {
            // Proccess the command
        }
    }

    // Command With Result
    public class CreateUserCommand : ICommand<int>
    {
        public string Name { get; set; }
    }
    
    // Handler to Handle Command With Result
    public class CreateUserCommandHandler : ICommandHandler<CreateUserCommand, int>
    {
        public Task<int> HandleAsync(CreateUserCommand input, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(1);
        }
    }

```

### Queries

Inject ``IQueryMediator`` to your class to send queries.

```C#

    // Simple Query
    public class ColorQuery : IQuery<IEnumerable<string>>
    {
        public int Id { get; set; }
    }
    
    // Simple Query Handler
    public class ColorQueryHandler : IQueryHandler<ColorQuery, IEnumerable<string>>
    {
        public Task<IEnumerable<string>> HandleAsync(ColorQuery input, CancellationToken cancellationToken = default)
        {
            // Process the query
        }
    }
    
    // Stream Query (a query that returns IAsyncEnumerable)
    public class ColorStreamQuery : IStreamQuery<string>
    {
        public int Id { get; set; }
    }
    
    // Stream Query Handler
    public class ColorStreamQueryHandler : IStreamQueryHandler<ColorStreamQuery, string>
    {
        public IAsyncEnumerable<string> HandleAsync(ColorStreamQuery input, CancellationToken cancellationToken = default)
        {
            throw new System.NotImplementedException();
        }
    }

```

### Events

Inject ``IEventMediator`` to your class to publish events.

```c#
    // The Event
    public class ColorCreatedEvent : IEvent
    {
        public string ColorName { get; set; }
    }
    
    // First Handler
    public class ColorCreatedEventHandler1 : IEventHandler<ColorCreatedEvent>
    {
        public Task HandleAsync(ColorCreatedEvent input, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    // Second Handler
    public class ColorCreatedEventHandler2 : IEventHandler<ColorCreatedEvent>
    {
        public Task HandleAsync(ColorCreatedEvent input, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }
```

### Plain Messages

You can send any object as a message if the object has any associated handlers.
Inject ``IMessageMediator`` to your class to publish events.

```c#
    // A plain message without implementing any interface
    public class PlainMessage
    {
        public int Number { get; set; }
    }
    
    // A message handler with result to handle the PlainMessage
    public class PlainMessageHandler : IMessageHandler<PlainMessage, Task<int>>
    {
        public Task<int> HandleAsync(PlainMessage message, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(message.Number * -1);
        }
    }
    
    // A message handler without result to handle the PlainMessage
    public class PlainMessageHandler2 : IMessageHandler<PlainMessage>
    {
        public Task HandleAsync(PlainMessage message, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }
```

## Hook Usages

Hooks allow you to execute an action in a certain stage of message handling.

* **PostHandleHooks**: execute an action after a message is handled
* **PreHandleHooks**: execute an action before a message is handled

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
