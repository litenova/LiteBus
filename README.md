<h1 align="center">
  <br>
  <a href="https://github.com/litenova/LiteBus">
    <img src="assets/logo/icon.png">
  </a>
  <br>
  LiteBus
  <br>
</h1>

<h4 align="center">An easy-to-use and ambitious and in-process mediator to implement CQS</h4>

<p align="center">

  <a href="https://github.com/litenova/LiteBus/actions/workflows/release.yml">
    <img src="https://github.com/litenova/LiteBus/actions/workflows/release.yml/badge.svg" alt="CI/CD Badge">
  </a>

   <a href='https://coveralls.io/github/litenova/LiteBus?branch=main'>
    <img src='https://coveralls.io/repos/github/litenova/LiteBus/badge.svg?branch=main' alt='Coverage Status' />
  </a>
  <a href="https://www.nuget.org/packages/LiteBus">
    <img src="https://img.shields.io/nuget/vpre/LiteBus.svg" alt="LiteBus Nuget Version">
  </a>
</p>

<p align="center">
  <a href="#overview">Overview</a> •
  <a href="#installation">Installation</a> •
  <a href="#configuration">Configuration</a> •
  <a href="#features-and-usages">Features and Usages</a> •
  <a href="#extensibility">Extensibility</a>
  <a href="#roadmap">Roadmap</a>
</p>

## Overview

* Written in .NET 7
* No Dependencies
* Minimum Reflection Usage
* Utilizing Covariance, Contravariance, and Polymorphism to Dispatch and Handle Messages
* Built-in Messaging Types
  * Command without Result `ICommand`
  * Command with Result `ICommand<TResult>`
  * Query `IQuery<TResult>`
  * Stream Query `IStreamQuery<TResult>`: A type of query that returns `IAsyncEnumerable<TResult>`
  * Event `IEvent`
* Flexible and Extensible
* Modular Design, Only Add What You Need
* Utilizing C# 8 Default Interface Implementation Feature Resulting in Easy to Use API
* Supports Polymorphism Dispatch
* Orderable Handlers
* Supports Plain Messages (Classes with no interface implementation)
* Supports Generic Messages

## Installation

LiteBus modules and features are released in separate **Nuget Packages**. Follow the instructions to install packages:

### Metapackage

The metapackage contains all the features. You can find the configuration of this package in **Configuration** section.

* [`LiteBus`](https://www.nuget.org/packages/LiteBus.Commands) contains the implementation of all features
* [`LiteBus.Extensions.MicrosoftDependencyInjection`](https://www.nuget.org/packages/LiteBus.Extensions.MicrosoftDependencyInjection) provides integration with Microsoft Dependency Injection. It's useful for ASP.NET Core applications.

### Commands

The commands feature consists of the following packages:

* [`LiteBus.Commands.Abstractions`](https://www.nuget.org/packages/LiteBus.Commands.Abstractions) - contains command abstractions such as `ICommand`, `ICommandHandler`, etc.
* [`LiteBus.Commands`](https://www.nuget.org/packages/LiteBus.Commands) - contains the implementation of commands
* [`LiteBus.Commands.Extensions.MicrosoftDependencyInjection`](https://www.nuget.org/packages/LiteBus.Extensions.MicrosoftDependencyInjection) - provides integration with Microsoft Dependency Injection. It's useful for ASP.NET Core applications.

### Queries

The queries feature consists of the following packages:

* [`LiteBus.Queries.Abstractions`](https://www.nuget.org/packages/LiteBus.Queries.Abstractions) - contains query abstractions such as `IQuery`, `IStreamQuery`, `IQueryHandler`, etc.
* [`LiteBus.Queries`](https://www.nuget.org/packages/LiteBus.Queries) - contains the implementation of queries
* [`LiteBus.Queries.Extensions.MicrosoftDependencyInjection`](https://www.nuget.org/packages/LiteBus.Extensions.MicrosoftDependencyInjection) - provides integration with Microsoft Dependency Injection. It's useful for ASP.NET Core applications.

### Events

The events feature consists of the following packages:

* [`LiteBus.Events.Abstractions`](https://www.nuget.org/packages/LiteBus.Events.Abstractions) - contains event abstractions such as `IEvent`, `IEventHandler`, etc.
* [`LiteBus.Events`](https://www.nuget.org/packages/LiteBus.Events) - contains the implementation of events
* [`LiteBus.Events.Extensions.MicrosoftDependencyInjection`](https://www.nuget.org/packages/LiteBus.Extensions.MicrosoftDependencyInjection) - provides integration with Microsoft Dependency Injection. It's useful for ASP.NET Core applications.


## Configuration

Follow the instruction below to configure LiteBus.

### Microsoft Dependency Injection (ASP.NET Core, etc.)

You can configure each module of LiteBus as needed in the `ConfigureServices` method of `Startup.cs`:

```csharp
services.AddLiteBus(builder =>
{
    builder.AddCommands(commandBuilder =>
           {
               commandBuilder.RegisterFromAssembly(typeof(CreateProductCommand).Assembly) // Register all handlers from the specified Assembly
                             .RegisterPreHandler<ProductValidationHandler>()
                             .RegisterPostHandler<ProductAuditingHandler>();
           })
           .AddQueries(queryBuilder =>
           {
               queryBuilder.RegisterFromAssembly(typeof(GetAllProducts).Assembly); 
           })
           .AddEvents(eventBuilder =>
           {
               eventBuilder.RegisterFromAssembly(typeof(NumberCreatedEvent).Assembly);
           });
});
```

## Features and Usages

The following examples demonstrate the features and usages of LiteBus.

### Commands

Commands are intended to perform actions that change the state of the system. To use commands, follow the instructions below.

#### Command Contracts 

Specify your commands by implementing:

* `ICommand` - a command without a result
* `ICommand<TCommandResult>` - a command with a result

#### Command Handler Contracts

* `ICommandHandler<TCommand>` - an asynchronous command handler that does not return a result
* `ICommandHandler<TCommand, TCommandResult>` - an asynchronous command handler that returns a result
* `ISyncCommandHandler<TCommand>` - a synchronous command handler that does not return a result
* `ISyncCommandHandler<TCommand, TCommandResult>` - a synchronous command handler that returns a result

#### Command Mediator/Dispatcher

You can use the `ICommandMediator` or `ICommandDispatcher` to execute your commands. Use them by injecting one of the interfaces into your desired class.

#### Command Pre Handlers

Pre-handlers allow you to perform actions before a command gets handled. They are handy for performing validation and starting transactions. By implementing the pre handlers, the pre handlers are executed automatically when executing a command. 

* `ICommandPreHandler<TCommand>` - this generic pre handler is executed on the pre-handle phase of the specified `TCommand`. This pre handler supports generic variance. 
* `ICommandPreHandler` - this pre handler acts as a global command pre handler. It's executed on every command pre-handle phase.

#### Command Post-Handlers

Post-handlers allow you to perform actions after a command gets handled. They are handy for committing transactions and auditing. By implementing the post-handlers, the post-handlers are executed automatically when executing a command.

* `ICommandPostHandler<TCommand>` - this generic post-handler is executed on the post-handle phase of the specified `TCommand`. This post-handler supports generic variance.
* `ICommandPostHandler<TCommand, TCommandResult>` - this generic post-handler is executed on the post-handle phase of the specified `TCommand` which has `TCommandResult` type.
* `ICommandPostHandler` - this post-handler acts as a global command post-handler. It's executed on every command post-handle phase.

#### Command Error Handlers

Error handlers allow you to catch and handle any errors thrown during the pre-handle, handle, and post-handle phase of a command. You can implement error handlers by deriving from:

* `ICommandErrorHandler<TCommand>` - this generic post-handler is executed on any phase of the specified command that runs into an error. It supports generic variance.
* `ICommandErrorHandler` - acts as the global command error handler.

#### Examples

A command without Result

```csharp
// A command without result
public class CreateProductCommand : ICommand
{
    public string Title { get; set; }
}

// The async handler
public class CreateProductCommandHandler : ICommandHandler<CreateProductCommand>
{
    public Task HandleAsync(CreateProductCommand command, CancellationToken cancellationToken = default)
    {
        // Process here...
    }
}

// The sync handler
public class CreateProductSyncCommandHandler : ISyncCommandHandler<CreateProductCommand>
{
    public void Handle(CreateProductCommand command)
    {
        // Process here...
    }
}

// The pre handler
public class ProductCommandPreHandler : ICommandPreHandler<CreateProductCommand>
{
    public Task PreHandleAsync(IHandleContext<CreateProductCommand> context)
    {
        // You can access the command through the context
        // Process here...
    }
}

// The post handler
public class ProductCommandPostHandler : ICommandPostHandler<CreateProductCommand, long>
{
    public Task PostHandleAsync(IHandleContext<CreateProductCommand, long> context)
    {
        // You can access the command and its result through the context
        // Process here...
    }
}
```

A command with result

```csharp
// A command with result
public class CreateProductCommand : ICommand<long>
{
    public string Title { get; set; }
}

// The async handler
public class CreateProductCommandHandler : ICommandHandler<CreateProductCommand, long>
{
    public Task<long> HandleAsync(CreateProductCommand command, CancellationToken cancellationToken = default)
    {
        // Process here...
    }
}

// The sync handler
public class CreateProductSyncCommandHandler : ISyncCommandHandler<CreateProductCommand, long>
{
    public long Handle(CreateProductCommand command)
    {
        // Process here...
    }
}

// The pre handler
public class ProductCommandPreHandler : ICommandPreHandler<CreateProductCommand>
{
    public Task PreHandleAsync(IHandleContext<CreateProductCommand> context)
    {
        // You can access the command through the context
        // Process here...
    }
}

// The post handler
public class ProductCommandPostHandler : ICommandPostHandler<CreateProductCommand>
{
    public Task PostHandleAsync(IHandleContext<CreateProductCommand> context)
    {
        // You can access the command through the context
        // Process here...
    }
}
```

### Queries

Queries are intended to query data without changing the state of the system. To use queries, follow the instructions below.

#### Query Contracts

Specify your queries by implementing:

* `IStreamQuery<TQueryResult>` - a query that returns a stream of data. The result is represented in the form `IAsyncEnumerable<TQueryResult>`.
* `IQuery<TQueryResult>` - a query with a result

#### Query Handler Contracts

* `IQueryHandler<TQuery, TQueryResult>` - an asynchronous query handler that returns a result
* `IStreamQueryHandler<TQuery, TQueryResult>` - an asynchronous query handler that returns a stream of data in the form `IAsyncEnumerable<TQueryResult>`
* `ISyncQueryHandler<TQuery, TQueryResult>` - a synchronous query handler that returns a result

#### Query Mediator/Dispatcher

You can use the `IQueryMediator` or `IQueryDispatcher` to execute your queries. Use them by injecting one of the interfaces into your desired class.

#### Query Pre Handlers

Pre-handlers allow you to perform actions before a query gets handled. They are handy for performing validation and starting transactions. By implementing the pre handlers, the pre handlers are executed automatically when executing a query.

* `IQueryPreHandler<TQuery>` - this generic pre handler is executed on the pre-handle phase of the specified `TQuery`. This pre handler supports generic variance.
* `IQueryPreHandler` - this pre handler acts as a global query pre handler. It's executed on every query pre-handle phase.

#### Query Post-Handlers

Post-handlers allow you to perform actions after a query gets handled. They are handy for committing transactions and auditing. By implementing the post-handlers, the post-handlers are executed automatically when executing a query.

* `IQueryPostHandler<TQuery>` - this generic post-handler is executed on the post-handle phase of the specified `TQuery`. This post-handler supports generic variance.
* `IQueryPostHandler<TQuery, TQueryResult>` - this generic post-handler is executed on the post-handle phase of the specified `TQuery` which has `TQueryResult` type.
* `IQueryPostHandler` - this post-handler acts as a global query post-handler. It's executed on every query post-handle phase.

#### Query Error Handlers

Error handlers allow you to catch and handle any errors thrown during the pre-handle, handle, and post-handle phase of a query. You can implement error handlers by deriving from:

* `IQueryErrorHandler<TQuery>` - this generic post-handler is executed on any phase of the specified query that runs into an error. It supports generic variance.
* `IQueryErrorHandler` - acts as the global query error handler.

#### Examples

A simple query

```csharp
// A simple query
public class GetSingleProductQuery : IQuery<Product>
{
    public long Id { get; set; }
}

// The async handler
public class GetSingleProductQueryHandler : IQueryHandler<GetSingleProductQuery, Product>
{
    public Task<Product> HandleAsync(GetSingleProductQuery query, CancellationToken cancellationToken = default)
    {
        // Process here...
    }
}

// The sync handler
public class GetSingleProductSyncQueryHandler : ISyncQueryHandler<GetSingleProductQuery, Product>
{
    public Product Handle(GetSingleProductQuery query)
    {
        // Process here...
    }
}

// The pre handler
public class ProductQueryPreHandler : IQueryPreHandler<GetSingleProductQuery>
{
    public Task PreHandleAsync(IHandleContext<GetSingleProductQuery> context)
    {
        // You can access the query through the context
        // Process here...
    }
}

// The post handler
public class ProductQueryPostHandler : IQueryPostHandler<GetSingleProductQuery>
{
    public Task PostHandleAsync(IHandleContext<GetSingleProductQuery> context)
    {
        // You can access the query through the context
        // Process here...
    }
}
```

An stream query

```csharp
// A stream query
public class GetAllProductsQuery : IStreamQuery<Product>
{

}

// The async handler
public class GetSingleProductQueryHandler : IStreamQueryHandler<GetSingleProductQuery, Product>
{
    public IAsyncEnumerable<Product> StreamAsync(GetSingleProductQuery query, CancellationToken cancellationToken = default)
    {
        // Process here...
    }
}

// The pre handler
public class ProductQueryPreHandler : IQueryPreHandler<GetSingleProductQuery>
{
    public Task PreHandleAsync(IHandleContext<GetSingleProductQuery> context)
    {
        // You can access the query through the context
        // Process here...
    }
}

```

### Events

Events act as informative messages. They can have multiple handlers.

#### Event Contracts

Specify your events by implementing:

* `IEvent`

#### Event Handler Contracts

* `IEventHandler<TEvent>` - an asynchronous event handler
* `ISyncEventHandler<TEvent>` - a synchronous event handler

#### Event Mediator/Dispatcher

You can use the `IEventMediator`, `IEventDispatcher`, or `IEventPublisher` to execute your events. Use them by injecting one of the interfaces into your desired class.

#### Event Pre Handlers

Pre-handlers allow you to perform actions before an event gets handled. They are handy for performing validation and starting transactions. By implementing the pre handlers, the pre handlers are executed automatically when executing an event.

* `IEventPreHandler<TEvent>` - this generic pre handler is executed on the pre-handle phase of the specified `TEvent`. This pre handler supports generic variance.
* `IEventPreHandler` - this pre handler acts as a global event pre handler. It's executed on every event pre-handle phase.

#### Event Post-Handlers

Post-handlers allow you to perform actions after an event gets handled. They are handy for committing transactions and auditing. By implementing the post-handlers, the post-handlers are executed automatically when executing an event.

* `IEventPostHandler<TEvent>` - this generic post-handler is executed on the post-handle phase of the specified `TEvent`. This post-handler supports generic variance.
* `IEventPostHandler` - this post-handler acts as a global event post-handler. It's executed on every event post-handle phase.

#### Event Error Handlers

Error handlers allow you to catch and handle any errors thrown during the pre-handle, handle, and post-handle phase of an event. You can implement error handlers by deriving from:

* `IEventErrorHandler<TEvent>` - this generic post-handler is executed on any phase of the specified event that runs into an error. It supports generic variance.
* `IEventErrorHandler` - acts as the global event error handler.

#### Examples

A simple event

```csharp
// A simple event
public class ProductCreatedEvent : IEvent
{
    public long Id { get; set; }
}

// The async handler 1
public class ProductCreatedEventHandler1 : IEventHandler<ProductCreatedEvent>
{
    public Task HandleAsync(ProductCreatedEvent @event, CancellationToken cancellationToken = default)
    {
        // Process here...
    }
}

// The async handler 2
public class ProductCreatedEventHandler2 : IEventHandler<ProductCreatedEvent>
{
    public Task HandleAsync(ProductCreatedEvent @event, CancellationToken cancellationToken = default)
    {
        // Process here...
    }
}

// The async handler 3
public class ProductCreatedEventHandler3 : ISyncEventHandler<ProductCreatedEvent>
{
    public void HandleAsync(ProductCreatedEvent @event, CancellationToken cancellationToken = default)
    {
        // Process here...
    }
}

// The pre handler
public class ProductEventPreHandler : IEventPreHandler<ProductCreatedEvent>
{
    public Task PreHandleAsync(IHandleContext<ProductCreatedEvent> context)
    {
        // You can access the event through the context
        // Process here...
    }
}

// The post handler
public class ProductEventPostHandler : IEventPostHandler<ProductCreatedEvent>
{
    public Task PostHandleAsync(IHandleContext<ProductCreatedEvent> context)
    {
        // You can access the event through the context
        // Process here...
    }
}


## Inheritance

The LiteBus uses the actual type of a message to determine the corresponding handler(s). Consider the following inheritance:

```csharp
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

// The second derived command without a handler
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


### Delivering Message to the Actual Type Handler

If a user tries to send the `CreateImageCommand` as `CreateFileCommand`, the LiteBus will deliver the command to `CreateImageCommandHandler`.

```csharp
CreateFileCommand command = new CreateImageCommand();

_mediator.SendAsync(command);
```

### Delivering Message to the Less Derived (Base Type) Handler

If a user tries to send the `CreateDocumentCommand` as `CreateFileCommand` or as it is, the LiteBus will deliver the command to `CreateFileCommandHandler` since the `CreateDocumentCommand` does not have a handler.

```csharp
CreateFileCommand command = new CreateDocumentCommand();

_mediator.SendAsync(command);

// or

var command = new CreateDocumentCommand();

_mediator.SendAsync(command);
```

**Note:** In such scenarios, the LiteBus will only deliver the message to the direct base class' handler if there is any.

## Extensibility

To Be Added

## Roadmap

- [ ] Providing Out-of-Process Message Handling
  - [ ] RabbitMQ
  - [ ] Kafka
  - [ ] Azure Event Bus
- [ ] Saga Support
- [ ] Outbox Support
- [ ] More Parallel Capabilities
