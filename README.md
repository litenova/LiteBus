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
  For a detailed understanding and advanced use cases, please refer to the <a href="https://github.com/litenova/LiteBus/wiki"><b>Wiki</b></a>.
</p>


## Features

- Developed with .NET 8.0
- Independent (No external dependencies)
- Reduced use of reflection
- Provides polymorphic dispatch and handling of messages with support for covariance and contravariance
- Core Messaging Types include:
  - `ICommand`: Command without result
  - `ICommand<TResult>`: Command with a result
  - `IQuery<TResult>`: Query
  - `IStreamQuery<TResult>`: Query yielding `IAsyncEnumerable<TResult>`
  - `IEvent`: Event
- Designed for flexibility and extensibility
- Modular architecture: Abstractions and implementations are provided in distinct packages
- Allows ordering of handlers
- Can handle plain messages (class types without specific interface implementations)
- Supports generic messages
- Features both global and individual pre and post handlers. These handlers also support covariance and contravariance
- Events do not necessarily need to inherit from `IEvent`, accommodating DDD scenarios. This is beneficial for maintaining clean domain events without binding them to any particular library interface.
