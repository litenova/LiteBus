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
  <a href="#usages">Usages</a> •
  <a href="#extensibility">Extensibility</a>
  <a href="#roadmap">Roadmap</a>
</p>

## Overview

* Written in .NET 6
* No Dependencies
* Minimum Reflection Usage
* Utilizing Covariance, Contravariance, and Polymorphism to Dispatch and Handle Messages
* Built-in Messaging Types
    * Command without Result `ICommand`
    * Command with Result `ICommand<TResult>`
    * Query `IQuery<TResult>`
    * Stream Query `IStreamQuery<TResult>`: A type of query that returns `IAsyncEnumerable<TResult>`
    * Event `IEvent`
* Flexible and Extensible `[Guideline Not Documented Yet]`
* Modular Design, Only Add What You Need `[Implemented but Not Documented Yet]`
* Utilizing C# 8 Default Interface Implementation Feature Resulting in Easy to Use API
* Supports Polymorphism Dispatch `[Implemented but Not Documented Yet]`
* Orderable Handlers `[Implemented but Not Documented Yet]`
* Supports Plain Messages (Classes with no interface implementation) `[Implemented but Not Documented Yet]`
* Supports Generic Messages `[Implemented but Not Documented Yet]`

## Installation

LiteBus modules and features are released in separate **Nuget Packages**. Follow the instructions to install packages:

### Metapackage

The metapackage contains all the features. You can find the configuration of this package in **Configuration** section.

* [`LiteBus`](https://www.nuget.org/packages/LiteBus.Commands) contains the implementation of all features
* [`LiteBus.Extensions.MicrosoftDependencyInjection`](https://www.nuget.org/packages/LiteBus.Extensions.MicrosoftDependencyInjection) provides integration with Microsoft Dependency Injection. It's useful for ASP.NET Core applications.


### Commands

The commands feature consists of following packages:

* [`LiteBus.Commands.Abstractions`](https://www.nuget.org/packages/LiteBus.Commands.Abstractions) this package has no dependency and contains all the commands abstractions such `ICommand`, `ICommandHandler`, etc.
* [`LiteBus.Commands`](https://www.nuget.org/packages/LiteBus.Commands) the implementation of commands
* [`LiteBus.Commands.Extensions.MicrosoftDependencyInjection`](https://www.nuget.org/packages/LiteBus.Extensions.MicrosoftDependencyInjection) provides integration with Microsoft Dependency Injection. It's useful for ASP.NET Core applications.  

### Queries

The queries feature consists of following packages:

* [`LiteBus.Queries.Abstractions`](https://www.nuget.org/packages/LiteBus.Queries.Abstractions) this package has no dependency and contains all the queries abstractions such `IQuery`, `IStreamQuery`, `IQueryHandler`, etc.
* [`LiteBus.Queries`](https://www.nuget.org/packages/LiteBus.Queries) the implementation of queries
* [`LiteBus.Queries.Extensions.MicrosoftDependencyInjection`](https://www.nuget.org/packages/LiteBus.Extensions.MicrosoftDependencyInjection) provides integration with Microsoft Dependency Injection. It's useful for ASP.NET Core applications.

### Events

The events feature consists of following packages:

* [`LiteBus.Events.Abstractions`](https://www.nuget.org/packages/LiteBus.Events.Abstractions) this package has no dependency and contains all the events abstractions such `IEvent`, `IEventHandler`, etc.
* [`LiteBus.Events`](https://www.nuget.org/packages/LiteBus.Events) the implementation of events
* [`LiteBus.Events.Extensions.MicrosoftDependencyInjection`](https://www.nuget.org/packages/LiteBus.Extensions.MicrosoftDependencyInjection) provides integration with Microsoft Dependency Injection. It's useful for ASP.NET Core applications.



## Configuration

Follow the instruction below to configure LiteBus.

### Microsoft Dependency Injection (ASP.NET Core, etc.)

You can configure each module of LiteBus as needed in the `ConfigureServices` method of `Startup.cs`:

```c#
services.AddLiteBus(builder =>
{
    builder.AddCommands(commandBuilder =>
           {
               commandBuilder.RegisterFrom(typeof(CreateProductCommand).Assembly) // Register all handlers from the specified Assembly
                             .RegisterPreHandler<ProductValidationHandler>()
                             .RegisterPostHandler<ProductAuditingHandler>();
           })
           .AddQueries(queryBuilder =>
           {
               queryBuilder.RegisterFrom(typeof(GetAllProducts).Assembly); 
           })
           .AddEvents(eventBuilder =>
           {
               eventBuilder.RegisterFrom(typeof(NumberCreatedEvent).Assembly);
           });
});
```

## Features and Usages

The following examples demonstrates the features and usages of LiteBus.

### Commands

Commands are intended to perform actions that changes the state of the system. To use commands follow the instructions below.

#### Command Contracts 

Specify your commands by implementing

* `ICommand` a command without result
* `ICommand<TCommandResult>` a command with result

#### Command Handler Contracts

* `ICommandHandler<TCommand>` an asynchronous command handler that does not return a result
* `ICommandHandler<TCommand, TCommandResult>` an asynchronous command handler that returns a result
* `ISyncCommandHandler<TCommand>` a synchronous command handler that does not return a result
* `ISyncCommandHandler<TCommand, TCommandResult>` a synchronous command handler that returns a result

#### Command Mediator/Dispatcher

You can use the `ICommandMediator` or `ICommandDispatcher` to execute your commands. Use them by Injecting one of the interfaces to your desired class.

#### Command Pre Handlers

Pre-handlers allow you to perform actions before a command gets handled. They are handy for performing validation and starting transactions. By implementing the pre handlers, the pre handlers are executed automatically when executing a command. 

* `ICommandPreHandler<TCommand>` This generic pre handler is executed on the pre handle phase of the specified `TCommand`. This pre handler supports generic variance. 
* `ICommandPreHandler` This pre handler acts as a global command pre handler. It's executed on every command pre-handle phase.

#### Command Post-Handlers

Post-handlers allow you to perform actions after a command gets handled. They are handy for commiting transactions and auditing. By implementing the post-handlers, the post-handlers are executed automatically when executing a command.

* `ICommandPostHandler<TCommand>` This generic post-handler is executed on the post handle phase of the specified `TCommand`. This post-handler supports generic variance.
* `ICommandPostHandler<TCommand, TCommandResult>` This generic post-handler is executed on the post handle phase of the specified `TCommand` which has `TCommandResult` type.
* `ICommandPostHandler` This post-handler acts as a global command post-handler. It's executed on every command post-handle phase.

#### Command Error Handlers

Error handlers allow you to catch and handle any errors thrown during the pre-handle, handle and post handle phase of a command. You can implement error handlers by deriving from:

* `ICommandErrorHandler<TCommand>` This generic post-handler is executed on any phase of the specified command that runs into error. It supports generic variance.
* `ICommandErrorHandler` Acts as the global command error handler.

#### Examples

A Command without Result

```c#
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
        public Task HandleAsync(IHandleContext<CreateProductCommand> context)
        {
            // You can access the command though the context
            // Process here...
        }
    }
    
    // The post handler
    public class ProductCommandPostHandler : ICommandPostHandler<CreateProductCommand, long>
    {
        public Task HandleAsync(IHandleContext<CreateProductCommand, long> context)
        {
            // You can access the command and its result though the context
            // Process here...
        }
    }    
```

A command with result
```c#
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
        public Task HandleAsync(IHandleContext<CreateProductCommand> context)
        {
            // You can access the command though the context
            // Process here...
        }
    }
    
    // The post handler
    public class ProductCommandPostHandler : ICommandPostHandler<CreateProductCommand>
    {
        public Task HandleAsync(IHandleContext<CreateProductCommand> context)
        {
            // You can access the command though the context
            // Process here...
        }
    }    
```

### Queries

Queries are intended to query data without changing the state of the system. To use queries follow the instructions below.

#### Query Contracts

Specify your queries by implementing

* `IStreamQuery<TQueryResult>` a query that returns an stream of data. The result is represented in the form `IAsyncEnumerable<TQueryResult`.
* `IQuery<TQueryResult>` a query with result

#### Query Handler Contracts

* `IQueryHandler<TQuery, TQueryResult>` an asynchronous query handler that returns a result
* `IStreamQueryHandler<TQuery, TQueryResult>` an asynchronous query handler that returns a stream of data in form `IAsyncEnumerable<TQueryResult`.
* `ISyncQueryHandler<TQuery, TQueryResult>` a synchronous query handler that returns a result

#### Query Mediator/Dispatcher

You can use the `IQueryMediator` or `IQueryDispatcher` to execute your queries. Use them by Injecting one of the interfaces to your desired class.

#### Query Pre Handlers

Pre-handlers allow you to perform actions before a query gets handled. They are handy for performing validation and starting transactions. By implementing the pre handlers, the pre handlers are executed automatically when executing a query.

* `IQueryPreHandler<TQuery>` This generic pre handler is executed on the pre handle phase of the specified `TQuery`. This pre handler supports generic variance.
* `IQueryPreHandler` This pre handler acts as a global query pre handler. It's executed on every query pre-handle phase.

#### Query Post-Handlers

Post-handlers allow you to perform actions after a query gets handled. They are handy for commiting transactions and auditing. By implementing the post-handlers, the post-handlers are executed automatically when executing a query.

* `IQueryPostHandler<TQuery>` This generic post-handler is executed on the post handle phase of the specified `TQuery`. This post-handler supports generic variance.
* `IQueryPostHandler<TQuery, TQueryResult>` This generic post-handler is executed on the post handle phase of the specified `TQuery` which has `TQueryResult` type.
* `IQueryPostHandler` This post-handler acts as a global query post-handler. It's executed on every query post-handle phase.

Please note, post handlers are not supported for stream queries.

#### Query Error Handlers

Error handlers allow you to catch and handle any errors thrown during the pre-handle, handle and post handle phase of a query. You can implement error handlers by deriving from:

* `IQueryErrorHandler<TQuery>` This generic post-handler is executed on any phase of the specified query that runs into error. It supports generic variance.
* `IQueryErrorHandler` Acts as the global query error handler.

Please note, error handlers are not supported for stream queries.

#### Examples

A simple query

```c#
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
        public Task HandleAsync(IHandleContext<GetSingleProductQuery> context)
        {
            // You can access the query though the context
            // Process here...
        }
    }
    
    // The post handler
    public class ProductQueryPostHandler : IQueryPostHandler<GetSingleProductQuery>
    {
        public Task HandleAsync(IHandleContext<GetSingleProductQuery> context)
        {
            // You can access the query though the context
            // Process here...
        }
    }    
```
An stream query

```c#
    // An stream query
    public class GetAllProductsQuery : IStreamQuery<Product>
    {
        
    }

    // The async handler
    public class GetSingleProductQueryHandler : IQueryHandler<GetSingleProductQuery, Product>
    {
        public IAsyncEnumerable<Product> StreamAsync(GetSingleProductQuery query, CancellationToken cancellationToken = default)
        {
            // Process here...
        }
    }
    
    // The pre handler
    public class ProductQueryPreHandler : IQueryPreHandler<GetSingleProductQuery>
    {
        public Task HandleAsync(IHandleContext<GetSingleProductQuery> context)
        {
            // You can access the query though the context
            // Process here...
        }
    }    
```

### Events

Events act as informative messages. They can have multiple handlers. 

#### Event Contracts

Specify your events by implementing

* `IEvent`

#### Event Handler Contracts

* `IEventHandler<TEvent>` an asynchronous event handler
* `ISyncEventHandler<TEvent>` a synchronous event handler

#### Event Mediator/Dispatcher

You can use the `IEventMediator` or `IEventDispatcher` or `IEventPublisher` to execute your events. Use them by Injecting one of the interfaces to your desired class.

#### Event Pre Handlers

Pre-handlers allow you to perform actions before a event gets handled. They are handy for performing validation and starting transactions. By implementing the pre handlers, the pre handlers are executed automatically when executing a event.

* `IEventPreHandler<TEvent>` This generic pre handler is executed on the pre handle phase of the specified `TEvent`. This pre handler supports generic variance.
* `IEventPreHandler` This pre handler acts as a global event pre handler. It's executed on every event pre-handle phase.

#### Event Post-Handlers

Post-handlers allow you to perform actions after a event gets handled. They are handy for commiting transactions and auditing. By implementing the post-handlers, the post-handlers are executed automatically when executing a event.

* `IEventPostHandler<TEvent>` This generic post-handler is executed on the post handle phase of the specified `TEvent`. This post-handler supports generic variance.
* `IEventPostHandler` This post-handler acts as a global event post-handler. It's executed on every event post-handle phase.

#### Event Error Handlers

Error handlers allow you to catch and handle any errors thrown during the pre-handle, handle and post handle phase of a event. You can implement error handlers by deriving from:

* `IEventErrorHandler<TEvent>` This generic post-handler is executed on any phase of the specified event that runs into error. It supports generic variance.
* `IEventErrorHandler` Acts as the global event error handler.

#### Examples

A simple event

```c#
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
        public Task HandleAsync(IHandleContext<ProductCreatedEvent> context)
        {
            // You can access the event though the context
            // Process here...
        }
    }
    
    // The post handler
    public class ProductEventPostHandler : IEventPostHandler<ProductCreatedEvent>
    {
        public Task HandleAsync(IHandleContext<ProductCreatedEvent> context)
        {
            // You can access the event and its result though the context
            // Process here...
        }
    }    
```

## Open-Generic Messages
You can handles messages (i.e., Commands, Events, Queries) in a open generic form. This feature is specially useful for queries where you might need to return a different result based on the specified type on the message.

See the example below:

```c#
    // The open-generic query
    public class GetPersonByIdQuery<TPerson> : IQuery<Person> where T: Person
    {
        public long Id { get; set; }
    }
    
    // The handler
    public class GetPersonByIdQueryHandler<TPerson> : IQueryHandler<GetPersonByIdQuery<TPerson>, Person> where T: Person
    {
        private DbContext _dbContext;
    
        public Task<Product> QueryAsync(GetPersonByIdQuery<TPerson> query, CancellationToken cancellationToken = default)
        {
            var people = _dbContext.Set<TPerson>().AsQueryable();
            
            // Process here...
        }
    }
    
    // The pre handler (if needed)
    public class GetPersonByIdQueryValidator<TPerson> : IQueryPreHandler<GetPersonByIdQuery<TPerson>> where T: Person
    {
        public Task HandleAsync(IHandleContext<GetPersonByIdQuery<TPerson>> context)
        {
            // your code goes here
        }
    }
    
    // How to use query mediator
    public class PeopleController
    {
        private readonly IQueryMediator _queryMediator;
        
        public PeopleController(IQueryMediator queryMediator)
        {
            _queryMediator = queryMediator;
        }
        
        [HttpGet("employees/{id}"]
        public Task<Employee> GetEmployee(long id)
        {
            var query = new GetPersonByIdQuery<Employee>() { Id = id };
            
            var result = await _queryMediator.QueryAsync(query);
            
            return result as Employee;
        }
    }
    
    
```

Please note:
* This feature is supported in all message types (i.e., Commands, Events, Queries).
* To use post-handlers and pre-handlers for generic types, they should also be in open-generic form. However, this is not needed for global pre and post handlers. 

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

## Extensibility

To be added

## Roadmap

- [ ] Integration with Message Brokers
  - [ ] RabbitMQ
  - [ ] Kafka
  - [ ] Azure Event Bus
- [ ] Saga Support
- [ ] Outbox Support
- [ ] More Parallel Capabilities
