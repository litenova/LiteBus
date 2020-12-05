# Paykan
![.NET 5 CI](https://github.com/mshfzd/Paykan/workflows/.NET%205%20CI/badge.svg)
[![NuGet](https://img.shields.io/nuget/vpre/paykan.svg)](https://www.nuget.org/packages/paykan)



A lightweight and easy to use in-process mediator to implement CQRS

* Written in .NET 5
* No Dependencies
* Multiple Messaging Type
    * Commands
    * Queries
    * Events
* Supports Stream Query (IAsyncEnumerable)
* Supports Both Command with Result and Without Result

## Installation and Configuration in ASP.NET Core 

Install with NuGet:

```
Install-Package Paykan
Install-Package Paykan.Extensions.MicrosoftDependencyInjection
```

or with .NET CLI:

```
dotnet add package Paykan
dotnet add package Paykan.Extensions.MicrosoftDependencyInjection
```

and configure the Paykan as below in the `ConfigureServices` method of `Startup.cs`:

```c#
services.AddPaykan(builder =>
{
    builder.RegisterHandlers(typeof(Startup).Assembly);
});
```
