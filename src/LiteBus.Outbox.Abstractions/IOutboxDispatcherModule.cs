using LiteBus.Messaging.Abstractions.Processing;
using LiteBus.Runtime.Abstractions;

namespace LiteBus.Outbox.Abstractions;

/// <summary>
///     Marks an outbox dispatcher sub-module registered by the outbox core builder.
/// </summary>
public interface IOutboxDispatcherModule : IModule
{
    /// <summary>
    ///     Gets the default after-dispatch hook failure policy applied when
    ///     the outbox core builder did not override hook behavior after dispatcher
    ///     registration.
    /// </summary>
    /// <value>
    ///     <see cref="ProcessorHookFailurePolicy.DeadLetter" /> unless a transport dispatcher overrides it.
    /// </value>
    ProcessorHookFailurePolicy DefaultHookFailurePolicy => ProcessorHookFailurePolicy.DeadLetter;
}
