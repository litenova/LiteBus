using LiteBus.Messaging.Abstractions.Processing;
using LiteBus.Runtime.Abstractions;

namespace LiteBus.Outbox.Abstractions;

/// <summary>
///     Marker for outbox dispatcher sub-modules registered through <see cref="OutboxModuleBuilder.RegisterDispatcher" />.
/// </summary>
public interface IOutboxDispatcherModule : IModule
{
    /// <summary>
    ///     Gets the default after-dispatch hook failure policy applied when
    ///     <see cref="OutboxModuleBuilder.UseProcessorOptions" /> did not override hook behavior after dispatcher
    ///     registration.
    /// </summary>
    /// <value>
    ///     <see cref="ProcessorHookFailurePolicy.DeadLetter" /> unless a transport dispatcher overrides it.
    /// </value>
    ProcessorHookFailurePolicy DefaultHookFailurePolicy => ProcessorHookFailurePolicy.DeadLetter;
}
