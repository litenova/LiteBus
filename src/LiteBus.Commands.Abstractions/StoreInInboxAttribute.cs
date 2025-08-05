using System;

namespace LiteBus.Commands.Abstractions;

/// <summary>
/// Marks a command to be stored in the durable inbox for deferred, guaranteed execution.
/// Commands decorated with this attribute are first persisted via an <see cref="ICommandInbox"/>
/// implementation and then processed asynchronously by the registered background processor.
/// This ensures the command will eventually be executed, even in the case of transient
/// application failures or restarts.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class StoreInInboxAttribute : Attribute;