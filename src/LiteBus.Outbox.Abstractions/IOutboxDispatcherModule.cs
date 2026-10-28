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

    /// <summary>
    ///     Gets a value indicating whether the dispatcher publishes the full event once per envelope.
    /// </summary>
    /// <value>
    ///     <see langword="false" /> for in-process dispatch, which publishes each per-handler envelope to its recorded
    ///     handler alone. Transport dispatchers return <see langword="true" /> because they publish the full event per
    ///     envelope, so per-handler envelopes would duplicate broker publication.
    /// </value>
    bool PublishesFullEventPerEnvelope => false;
}
