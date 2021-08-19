<h1 align="center">
  <br>
  <a href="https://github.com/litenova/LiteBus">
    <img src="https://raw.githubusercontent.com/litenova/LiteBus/main/assets/logo/icon.png">
  </a>
  <br>
  LiteBus
  <br>
</h1>

<h4 align="center">A liteweight and easy to use in-process mediator to implement CQS</h4>

<p align="center">
  <a href="#">
    <img src="https://github.com/arishk/LiteBus/actions/workflows/dotnet-core.yml/badge.svg?branch=main">
  </a>
  <a href="#">
    <img src="https://img.shields.io/nuget/vpre/LiteBus.svg">
  </a>
</p>

<p align="center">
  <a href="#key-features">Key Features</a> •
  <a href="#installation-and-configuration">Installation</a> •
  <a href="#how-to-use">How To Use</a>
</p>

## Key Features

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

## How to Use

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
        public Task HandleAsync(CreateColorCommand command, CancellationToken cancellationToken = default)
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
        public Task<int> HandleAsync(CreateUserCommand command, CancellationToken cancellationToken = default)
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
        public Task<ColorReadModel> HandleAsync(ColorQuery query, CancellationToken cancellationToken = default)
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
        public IAsyncEnumerable<ColorReadModel> HandleAsync(ColorStreamQuery query, CancellationToken cancellationToken = default)
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
        public Task HandleAsync(ColorCreatedEvent @event, CancellationToken cancellationToken = default)
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

You can send any object as a message if the object has any associated handlers. Inject ``IMessageMediator`` to your
class to publish events.

```c#
    // A plain message
    public class PlainMessage
    {
        public int Number { get; set; }
    }
    
    // The handler to handle the message with result
    public class PlainMessageHandler : IMessageHandler<PlainMessage, Task<int>>
    {
        public Task<int> HandleAsync(PlainMessage message, CancellationToken cancellationToken = default)
        {
            // process the message
        }
    }
    
    // The handler to handle the message without result
    public class PlainMessageHandler2 : IMessageHandler<PlainMessage>
    {
        public Task HandleAsync(PlainMessage message, CancellationToken cancellationToken = default)
        {
            // process the message
        }
    }
```

## Hooks

Hooks allow you to execute an action in a certain stage of message handling. Currently, hooks are only available for
commands.

* **Post Handle Hooks**: execute an action after a message is handled
* **Pre Handle Hooks** execute an action before a message is handled

### Post Handle Hook

```c#

    // Exectues an action after each command is handled
    public class GlobalCommandPostHandleHook : ICommandPostHandleHook
    {
        public Task ExecuteAsync(IBaseCommand message, CancellationToken cancellationToken = default)
        {
            Debug.WriteLine("GlobalCommandPostHandleHook executed!");
            return Task.CompletedTask;
        }
    }

    // Exectues an action after the specified command is handled
    public class CreateColorCommandPostHandleHook : ICommandPostHandleHook<CreateColorCommand>
    {
        public Task ExecuteAsync(CreateColorCommand message, CancellationToken cancellationToken = default)
        {
            Debug.WriteLine("CreateColorCommandPostHandleHook executed!");
            return Task.CompletedTask;
        }
    }
```

### Pre Handle Hook

```c#

    // Exectues an action beafore each command is handled
    public class GlobalCommandPreHandleHook : ICommandPreHandleHook
    {
        public Task ExecuteAsync(IBaseCommand message, CancellationToken cancellationToken = default)
        {
            Debug.WriteLine("GlobalCommandPreHandleHook executed!");
            return Task.CompletedTask;
        }
    }

    // Exectues an action before the specified command is handled
    public class CreateColorCommandPreHandleHook : ICommandPreHandleHook<CreateColorCommand>
    {
        public Task ExecuteAsync(CreateColorCommand message, CancellationToken cancellationToken = default)
        {
            Debug.WriteLine("CreateColorCommandPreHandleHook executed!");
            return Task.CompletedTask;
        }
    }
```

## Inheritance

The LiteBus uses the actual type of a message to determine the corresponding handler(s). Consider the following
inheritance:

```c#
    // The base command
    public class CreateFileCommand : ICommand
    {
        public string Name { get; set; }
    }
    
    // The derived command
    public class CreateImageCommand : CreateFileCommand
    {
        public int Width { get; set; }
        
        public int Height { get; set; }
    }
    
    // The second derived command without handler
    public class CreateDocumentCommand : CreateFileCommand
    {
        public string Author { get; set; }
    }
    
    // The base command handler
    public class CreateFileCommandHandler : ICommandHandler<CreateFileCommand>
    {
        public Task HandleAsync(CreateFileCommand command, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }
    }
    
    // The derived command handler
    public class CreateImageCommandHandler : ICommandHandler<CreateImageCommand>
    {
        public Task HandleAsync(CreateImageCommand command, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }
    }
```

### Delivering Message to the Actual Type Handler

If a user tries to send the ``CreateImageCommand`` as ``CreateFileCommand``, the LiteBus will deliver the command
to ``CreateImageCommandHandler``.

```c#
    CreateFileCommand command = new CreateImageCommand();
    
    _mediator.SendAsync(command);
```

### Delivering Message to the Less Derived (Base Type) Handler

If a user tries to send the ``CreateDocumentCommand`` as ``CreateFileCommand`` or as it is, the LiteBus will deliver the
command to ``CreateFileCommandHandler`` since the ``CreateDocumentCommand`` does not have handler.

```c#
    CreateFileCommand command = new CreateDocumentFile();
    
    _mediator.SendAsync(command);
    
    // or
    
    var command = new CreateDocumentFile();
    
    _mediator.SendAsync(command);
```

**Note:** In such scenarios, the LiteBus will only deliver the message to the direct base class' handler if there is
any.
