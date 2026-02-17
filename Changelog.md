# Changelog

All notable changes to this project will be documented in this file.

## v4.3.0

### Added

- **Open Generic Handler Support:** LiteBus now supports open generic pre-handlers, post-handlers, and error handlers
  (e.g., `MyPreHandler<T> : ICommandPreHandler<T> where T : ICommand`). When registered, LiteBus automatically closes
  the generic for every concrete message type that satisfies its constraints at startup. This enables cross-cutting
  concerns like logging, validation, metrics, and authorization to be implemented once and applied universally — without
  modifying existing messages or handlers. Registration order does not matter. All standard C# generic constraints
  (interface, class, struct, new()) are fully respected.

## v4.2.0

### Added

- **Event Contextual Data:** Added the `Items` property back to `EventMediationSettings`, allowing contextual data to be
  passed through the event mediation pipeline, similar to commands and queries.

### Improved

- **.NET 10 Support:** Added support for .NET 10 across all relevant projects.
- **Developer Experience:** Made assembly signing conditional on the existence of the `LiteBus.snk` file. This
  simplifies the build process for contributors who fork the repository, as they no longer need to generate a strong
  name key to build the project locally.

## v4.1.0

### Added

- **Type-Safe Stream Query Post-Handler:** Introduced the new `IStreamQueryPostHandler<TQuery, TQueryResult>` interface.
  This provides a strongly-typed post-handler for stream queries, giving access to the original query and the
  `IAsyncEnumerable<TQueryResult>` result stream, aligning its design with regular command and query post-handlers.

### Fixed

- **Stream Query Context Preservation:** Fixed a critical bug in the stream query mediation strategy where the
  `AmbientExecutionContext` was lost during stream enumeration. This prevented stream handlers from accessing the
  context (e.g., `Items` collection) in logic that executed after yielding all results, and also prevented stream
  post-handlers from accessing the context. The context is now correctly preserved throughout the entire streaming
  lifecycle.

## v4.0.0

This is a major release with a fundamental architectural redesign to decouple the library from specific Dependency
Injection (DI) containers, introduce a durable Command Inbox, and provide advanced control over event mediation.

### 🚀 Features

- **Dependency Injection Abstraction (`LiteBus.Runtime`):** The entire library has been refactored to be DI-agnostic,
  introducing a new runtime layer. This decouples the core logic from any specific DI container and allows for
  integrations via a lightweight adapter pattern.
- **Autofac Support:** Added first-class integration with Autofac via the new `LiteBus.Extensions.Autofac` package and
  its companions.
- **Durable Command Inbox:** Introduced a new Command Inbox feature for guaranteed, durable, and deferred command
  execution. Commands can be marked with `[StoreInInbox]` to be persisted and processed by a background service (
  `CommandInboxProcessorHostedService`).
- **Advanced Event Mediation:** Overhauled event mediation with powerful new controls:
- The new `[HandlerPriority]` attribute replaces `[HandlerOrder]` for defining execution priority.
- Added configurable concurrency for both priority groups (`PriorityGroupsConcurrencyMode`) and handlers within the same
  group (`HandlersWithinSamePriorityConcurrencyMode`).
- Enhanced `HandlerPredicate` that receives a full `IHandlerDescriptor` for advanced filtering logic based on handler
  type, priority, tags, and message type.

### ✨ Improvements

- **Simplified Module Registration:** The `AddCommandModule`, `AddEventModule`, and `AddQueryModule` extensions now
  automatically register the core `MessageModule`, reducing boilerplate configuration.
- **Robust Message Registry:** The internal `MessageRegistry` has been re-engineered for improved performance and
  correctness, ensuring handlers are correctly associated with messages regardless of registration order.
- **API Clarity:** Renamed several properties for better intent, such as `Order` to `Priority` on descriptors and
  `Handlers` to `MainHandlers` on `IMessageDependencies`.
- **Testability:** Added `IMessageRegistry.Clear()` to allow resetting the registry state, which is useful in test
  environments.

### 💥 Breaking Changes

- **Project Structure & NuGet Packages:** The project structure and package names have been completely refactored. You
  must update your `.csproj` files to reference the new packages (e.g.,
  `LiteBus.Extensions.Microsoft.DependencyInjection`, `LiteBus.Commands.Extensions.Microsoft.DependencyInjection`).
- **DI Registration API:** The `AddLiteBus` registration process is now part of the new DI-specific extension packages.
  Module registration extensions (`AddCommandModule`, etc.) have moved to their respective core namespaces (e.g.,
  `LiteBus.Commands`).
- **Attribute Renaming:** `[HandlerOrder]` has been replaced by `[HandlerPriority]`. The `Order` property on
  `IHandlerDescriptor` is now `Priority`.
- **Mediation Settings `Items` Key:** The key type for the `Items` dictionary on `CommandMediationSettings`,
  `QueryMediationSettings`, and `ExecutionContext` has been changed from `object` to `string`.
- **`EventMediationSettings` Redesign:** The structure of `EventMediationSettings` has been completely changed to
  support the new priority and concurrency features. The `Filters` property is now `Routing`, and a new `Execution`
  property has been added.
- **`IMessageDependencies` Renaming:** The `Handlers` and `IndirectHandlers` properties have been renamed to
  `MainHandlers` and `IndirectMainHandlers`, respectively. This affects custom mediation strategies.

> **Note:** Due to the significant architectural changes, please refer to the **v4 Migration Guide** in the release
> notes for detailed instructions on upgrading your project.

## v3.1.0

- **Added**: Support for passing contextual metadata through the mediation pipeline. The `CommandMediationSettings`,
  `QueryMediationSettings`, and `EventMediationSettings` now include an `Items` dictionary (
  `IDictionary<object, object?>`) that can be used to pass data to all handlers (pre-handlers, main handlers,
  post-handlers, and error-handlers) via `AmbientExecutionContext.Current.Items`.

## v3.0.0

- **Breaking Change**: All LiteBus assemblies are now strong-named to support usage in enterprise applications and
  projects that require signed dependencies. This is a breaking change that requires a major version update.

## v2.2.3

- **Fixed**: Remove extra DI container registration

## v2.2.2

- **Fixed**: DI container registration now properly filters out interfaces and abstract classes during service
  registration. Previously, `RegisterFromAssembly()` would cause DI container errors when trying to register
  non-instantiable types. LiteBus message registry continues to accept all types to support polymorphic dispatch, but
  only concrete classes are registered with the DI container.

## v2.2.1

- **Fixed**: Support for record structs as message types (commands, queries, events). Previously record structs couldn't
  be registered due to a type filtering condition that only allowed class types.
- **Improved**: Message registration to handle all non-System types, allowing for greater flexibility in message
  definitions.

## v2.2.0

- **Added**: Support for incremental registration allowing for breaking down LiteBus configuration in different parts of
  the application.

## v2.1.0

- **Added**: .NET 9 support while maintaining backward compatibility with .NET 8
- **Updated**: All dependencies to their latest .NET 9 compatible versions
- **Improved**: Multi-targeting build process for both .NET 8 and .NET 9

## v2.0.0

- **Breaking Change**: Removed nullable annotations from mediator interfaces. Nullability should now be expressed in
  message contracts instead. See [Migration Guide](https://github.com/litenova/LiteBus/wiki/Migration-Guide) for
  details.

## v1.1.0

- Add `IQueryValidator`

## v1.0.0

- Added: Comprehensive wiki documentation
- Added: Source Link support for improved debugging
- Added: Automated release workflow with GitVersion integration
- Added: Handler tags for contextual scenario handling
- Changed: Updated repository structure for modern .NET practices
- Improved: Code documentation and examples
- Fixed: Various minor issues from previous versions

## v0.25.1

- Add `ICommandValidator`

## v0.25.0

- Enable `Nullable` for all projects.

## v0.24.4

- Improve XML comments in the codebase.

## v0.24.3

- Don't throw error by default if no handlers were found for plain event message types

## v0.24.2

- Allow aborting the execution of handlers by calling `Abort` on the execution context.

## v0.24.1

- Add `Tags` to `IExecutionContext`.

## v0.24.0

- Upgraded to .NET 8.

## v0.23.1

- Add `QueryMediatorExtensions` for backward compatibility.
- Add `CommandMediatorExtensions` for backward compatibility.
- Add `EventMediatorExtensions` for backward compatibility.

## v0.23.0

- Fix the missing `Exception` parameter in `IAsyncMessageErrorHandler[TMessage, TMessageResult]` and
  `IAsyncMessageErrorHandler[TMessage]` interfaces.

## v0.22.0

- Introduce tag-based handler filtering through `HandlerTag` and `HandlerTags` attributes.
- Add `CommandMediationSettings` to `ICommandMediator` to allow configuring command mediation.
- Add `QueryMediationSettings` to `IQueryMediator` to allow configuring query mediation.

## v0.21.0

- Fixed Query, Event, and Command error handlers returning `object` instead of `Task`.

## v0.20.2

- Refined Handle Descriptors
- Removed Any Usage of Reflection in `MessageDependencies`
- Removed Some Redundant Code From Descriptors

## v0.20.1

- Rename `AddMessaging` method to `AddMessageModule`.

## v0.20.0

- Revert TargetFramework to NET 7

## v0.19.1

- Add `ThrowOnNoHandlers` to `EventMediationSettings` to allow throwing an exception when no handlers are found for an
  event.
- Fixed a bug where the pre and post handlers were being executed even when no main handlers were found.

## v0.19.0

- Upgraded to .NET 8.

## v0.18.4

- Rename `FilterHandler` to `HandlerFilter` on `EventMediationSettings` as it is more concise and directly states that
  it is a filter for handlers.

## v0.18.3

- Add `EventMediationSettings` to IEventMediator to allow configuring event mediation.
- Add `FilterHandler` to `EventMediationSettings` to allow filtering event handlers.

## v0.18.2

- Preserve the stack trace when rethrowing an exception in case there are no error handlers.

## v0.18.1

- Make execution of event handlers synchronous by default.

## v0.18.0

- All post handlers expose message result as the second parameter.
- Fixed a bug where IEventPreHandler was not asynchronous.
- Added more unit tests.

## v0.17.1

- Add `Items` property to `IExecutionContext` to allow passing data between handlers.

## v0.17.0

- Rename `AddCommands` method to `AddCommandModule`.
- Rename `AddEvents` method to `AddEventModule`.
- Rename `AddQueries` method to `AddQueryModule`.

## v0.16.0

- Introduced execution context using AsyncLocal functionality, accessible through AmbientExecutionContext.
- Renamed `RegisterFrom` to `RegisterFromAssembly` in module builders.
- Standardized namespace for all files in the `LiteBus.Messaging.Abstractions` project to
  `LiteBus.Messaging.Abstractions`, irrespective of folder path.
- Removed `HandleContext` as a parameter from post and pre handlers.

## v0.15.1

- Removed `IEvent` constraint from event handlers, allowing objects to be passed as events without implementing the
  `IEvent` interface.

## v0.15.0

- Added overload method to event publisher for passing an object as a message.
- Removed `LiteBus` prefix from module constructor names.

## v0.14.1

- Upgraded dependency packages.

## v0.14.0

- Upgraded to .NET 7.

## v0.13.0

- Replaced `ICommandBase` with `ICommand`.
- Replaced `IQueryBase` with `IQuery`.
- Renamed `ILiteBusModule` to `IModule`.
- Removed methods `RegisterPreHandler`, `RegisterHandler`, and `RegisterPostHandler`, replacing them with `Register`.
- Removed superfluous base interfaces.

## v0.12.0

- Added support to message registry for registering any class type as a message.

## v0.11.3

- Fixed bug: Execute error handlers instead of pre handlers during error phase.

## v0.11.2

- Fixed bug: Considered the count of indirect error handlers when determining if an exception should be rethrown.

## v0.11.1

- Disabled nullable reference types.
- Ensured error handlers cover errors in pre and post handlers.

## v0.11.0

- Introduced non-generic message registration overloads for events, queries, and messaging configuration.
- Removed the sample project.
- Added unit tests for events and queries.
