# Changelog

## v.0.24.1
- Add `Tags` to `IExecutionContext`.

## v.0.24.0
- Upgraded to .NET 8.

## v.0.23.1
- Add `QueryMediatorExtensions` for backward compatibility.
- Add `CommandMediatorExtensions` for backward compatibility.
- Add `EventMediatorExtensions` for backward compatibility.

## v.0.23.0
- Fix the missing `Exception` parameter in `IAsyncMessageErrorHandler[TMessage, TMessageResult]` and `IAsyncMessageErrorHandler[TMessage]` interfaces.

## v.0.22.0
- Introduce tag-based handler filtering through `HandlerTag` and `HandlerTags` attributes.
- Add `CommandMediationSettings` to `ICommandMediator` to allow configuring command mediation.
- Add `QueryMediationSettings` to `IQueryMediator` to allow configuring query mediation.

## v.0.21.0
- Fixed Query, Event, and Command error handlers returning `object` instead of `Task`.

## v.0.20.2
- Refined Handle Descriptors
- Removed Any Usage of Reflection in `MessageDependencies`
- Removed Some Redundant Code From Descriptors

## v.0.20.1
- Rename `AddMessaging` method to `AddMessageModule`.

## v.0.20.0
- Revert TargetFramework to NET 7

## v.0.19.1
- Add `ThrowOnNoHandlers` to `EventMediationSettings` to allow throwing an exception when no handlers are found for an event.
- Fixed a bug where the pre and post handlers were being executed even when no main handlers were found.

## v.0.19.0
- Upgraded to .NET 8.

## v.0.18.4
- Rename `FilterHandler` to `HandlerFilter` on `EventMediationSettings` as it is more concise and directly states that it is a filter for handlers.

## v.0.18.3
- Add `EventMediationSettings` to IEventMediator to allow configuring event mediation.
- Add `FilterHandler` to `EventMediationSettings` to allow filtering event handlers.

## v.0.18.2
- Preserve the stack trace when rethrowing an exception in case there are no error handlers.

## v.0.18.1
- Make execution of event handlers synchronous by default.

## v.0.18.0
- All post handlers expose message result as the second parameter.
- Fixed a bug where IEventPreHandler was not asynchronous.
- Added more unit tests.

## v.0.17.1
- Add `Items` property to `IExecutionContext` to allow passing data between handlers.

## v.0.17.0
- Rename `AddCommands` method to `AddCommandModule`.
- Rename `AddEvents` method to `AddEventModule`.
- Rename `AddQueries` method to `AddQueryModule`.

## v.0.16.0
- Introduced execution context using AsyncLocal functionality, accessible through AmbientExecutionContext.
- Renamed `RegisterFrom` to `RegisterFromAssembly` in module builders.
- Standardized namespace for all files in the `LiteBus.Messaging.Abstractions` project to `LiteBus.Messaging.Abstractions`, irrespective of folder path.
- Removed `HandleContext` as a parameter from post and pre handlers.

## v.0.15.1
- Removed `IEvent` constraint from event handlers, allowing objects to be passed as events without implementing the `IEvent` interface.

## v.0.15.0
- Added overload method to event publisher for passing an object as a message.
- Removed `LiteBus` prefix from module constructor names.

## v.0.14.1
- Upgraded dependency packages.

## v.0.14.0
- Upgraded to .NET 7.

## v.0.13.0
- Replaced `ICommandBase` with `ICommand`.
- Replaced `IQueryBase` with `IQuery`.
- Renamed `ILiteBusModule` to `IModule`.
- Removed methods `RegisterPreHandler`, `RegisterHandler`, and `RegisterPostHandler`, replacing them with `Register`.
- Removed superfluous base interfaces.

## v.0.12.0
- Added support to message registry for registering any class type as a message.

## v.0.11.3
- Fixed bug: Execute error handlers instead of pre handlers during error phase.

## v.0.11.2
- Fixed bug: Considered the count of indirect error handlers when determining if an exception should be rethrown.

## v.0.11.1
- Disabled nullable reference types.
- Ensured error handlers cover errors in pre and post handlers.

## v.0.11.0
- Introduced non-generic message registration overloads for events, queries, and messaging configuration.
- Removed the sample project.
- Added unit tests for events and queries.
