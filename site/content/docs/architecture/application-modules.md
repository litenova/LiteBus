# Application Modules

In a modular monolith each assembly owns its handlers, contracts, outbox registrations, and recurring jobs, but LiteBus composes in one central `AddLiteBus` call. An application module lets each assembly contribute its own configuration to that call instead of routing every registration through the host project. This page explains how one assembly contributes, how the host registers those contributions, and what happens when several contributions configure the same LiteBus module.

## Contributing Configuration From an Assembly

An application module is a class implementing `IApplicationModule`. Its `Configure` method receives the same `ILiteBusBuilder` the host composes, and calls the same `AddCommands`, `AddEvents`, `AddOutbox`, and similar extensions:

```csharp
public sealed class LedgerModule : IApplicationModule
{
    public void Configure(ILiteBusBuilder liteBus)
    {
        liteBus.AddCommands(commands => commands.RegisterFromAssembly(typeof(LedgerModule).Assembly));
        liteBus.AddOutbox(outbox => outbox.Contracts.Register<EntryPosted>("ledger.entry-posted"));
    }
}
```

Put the module class in the assembly it configures, next to the handlers and contracts it registers. Each assembly then carries one visible list of what it contributes: the handlers it registers, the durable contracts it names, and the recurring jobs it schedules.

## Registering Application Modules

The host registers contributions explicitly or by discovery. Register one module by type or by instance:

```csharp
services.AddLiteBus(liteBus =>
{
    liteBus.AddApplicationModule<LedgerModule>();
    liteBus.AddApplicationModule(new BillingModule(connectionString));
});
```

`AddApplicationModule<TModule>()` constructs the module itself, so the type needs a parameterless constructor. `AddApplicationModule(IApplicationModule module)` takes an instance, for a module whose configuration needs values only the host knows.

Discover every module in an assembly at once:

```csharp
services.AddLiteBus(liteBus =>
{
    liteBus.AddApplicationModulesFromAssembly(typeof(LedgerModule).Assembly);
});
```

Discovery registers every non-abstract public or internal class implementing `IApplicationModule` that has a parameterless constructor, in type-name order. A top-level type passes when it is public or internal. A nested type passes only when it is nested public or nested internal and every declaring type also passes. A private nested class is skipped, as are abstract classes and classes without a parameterless constructor: register a class without a parameterless constructor by instance instead. Type-name order keeps the composition deterministic no matter how the runtime enumerates the assembly.

Each registration runs the module's `Configure` against the builder immediately, so by the time `AddLiteBus` validates the module graph, every contribution is already part of the composition.

## Configuring One LiteBus Module From Several Contributions

Several application modules configure the same LiteBus modules: two assemblies both call `AddCommands`, and both register handlers into one command module. Every `Add*` registration extension of a module with a builder, `AddMessaging`, `AddCommands`, `AddQueries`, `AddEvents`, `AddInbox`, `AddOutbox`, `AddSagas`, and their `Add*Module` forms, checks whether its module is already registered first. When it is, the call runs its configuration action on the module's existing builder instead of registering a second module. Calling `AddCommands` twice therefore configures one module instead of throwing.

```csharp
services.AddLiteBus(liteBus =>
{
    liteBus.AddApplicationModule<LedgerModule>();
    liteBus.AddApplicationModule<BillingModule>();
    liteBus.AddCommands(commands => commands.RegisterFromAssembly(typeof(Program).Assembly));
});
```

All three calls add to the same command module: the two application modules register their assemblies first, and the host's own call adds a third. A child a repeated call declares, such as a storage or dispatch adapter registered inside a repeated `AddInbox`, joins the same parent module through the normal composite membership.

Registering the same module type twice through `Modules.Register` directly still throws `LiteBusConfigurationException` at compose time. The repeated-configure behavior belongs to the `Add*` extensions only. Configuration after `BuildOrder` freezes the registry throws as before, so application modules run during composition, never after the host has started.

## Overlapping Assembly Scans Are Safe

Two contributions may scan the same assembly. The host keeps central `RegisterFromAssembly` calls while modules add their own `RegisterFromAssembly` calls for the assemblies they own, so during the move each assembly is scanned twice. That overlap is safe. The message registry skips a type it already processed, so scanning it again registers nothing, throws nothing, and creates no duplicate descriptors.

Move the scans in this order. Add a `Configure` method per module that scans its own assembly first. Keep the central scans while the per-module scans land. Remove the central scans last, once every assembly contributes its own. The composition passes through overlap in the middle, and the registry deduplication is what makes that middle state valid.

```csharp
// During the move: the host scan and the module scan cover LedgerModule.Assembly twice.
services.AddLiteBus(liteBus =>
{
    liteBus.AddApplicationModule<LedgerModule>();
    liteBus.AddApplicationModule<BillingModule>();
    liteBus.AddCommands(commands => commands.RegisterFromAssembly(typeof(LedgerModule).Assembly));
});
```

```csharp
public sealed class LedgerModule : IApplicationModule
{
    public void Configure(ILiteBusBuilder liteBus)
    {
        liteBus.AddCommands(commands => commands.RegisterFromAssembly(typeof(LedgerModule).Assembly));
    }
}
```

Removing the host scan before the module scans land leaves handlers unregistered. Adding the module scans first keeps every handler registered at every step.

## Resolving an IModule Name Collision

An application that already defines its own module contract named `IModule` meets a name collision on its first `Configure`. Adding `using LiteBus.Runtime.Abstractions;`, which is needed for `IApplicationModule` and `ILiteBusBuilder`, makes the `IModule` reference ambiguous (CS0104) between `LiteBus.Runtime.Abstractions.IModule` and the application's own contract, for example `Application.Platform.Modules.IModule`. `IModule` is the natural name for an application's module entry contract, so a modular monolith that adopts application modules often meets this in every module file.

Qualify the application's contract in the declaration:

```csharp
using LiteBus.Runtime.Abstractions;

public sealed class LedgerModule : Application.Platform.Modules.IModule, IApplicationModule
{
    public void Configure(ILiteBusBuilder liteBus)
    {
        liteBus.AddCommands(commands => commands.RegisterFromAssembly(typeof(LedgerModule).Assembly));
    }
}
```

Or alias one of the two names at the top of the file:

```csharp
using AppModule = Application.Platform.Modules.IModule;
using LiteBus.Runtime.Abstractions;

public sealed class LedgerModule : AppModule, IApplicationModule
{
    public void Configure(ILiteBusBuilder liteBus)
    {
        liteBus.AddCommands(commands => commands.RegisterFromAssembly(typeof(LedgerModule).Assembly));
    }
}
```

Either form keeps both contracts visible. The page's own example shows a module implementing `IApplicationModule` only because it assumes no prior contract exists.

## Next

Read [Architecture](README.md) for the module composition model these contributions feed, and [Hosted Services](hosted-services.md) for the startup tasks and background services a module registers through the manifest.
