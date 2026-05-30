using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Inbox.Abstractions;

/// <summary>
///     Executes a leased inbox envelope by dispatching its deserialized message to a handler.
/// </summary>
/// <remarks>
///     <para>
///         The inbox processor calls this role after leasing an envelope from the store. The dispatcher is responsible
///         for deserializing the payload, resolving the handler, and executing it. When execution fails, the dispatcher
///         should throw so the processor can record retry or dead-letter state.
///     </para>
///     <para>
///         The default implementation in <c>LiteBus.Inbox</c> dispatches through <c>ICommandMediator</c>. Register a
///         custom implementation when the inbox needs to execute messages through a different pipeline.
///     </para>
/// </remarks>
public interface IInboxDispatcher
{
    /// <summary>
    ///     Dispatches one leased inbox envelope.
    /// </summary>
    /// <param name="envelope">The leased envelope containing the serialized payload and contract metadata.</param>
    /// <param name="cancellationToken">A token used to cancel deserialization or handler execution.</param>
    /// <returns>A task that represents the asynchronous dispatch operation.</returns>
    Task DispatchAsync(InboxEnvelope envelope, CancellationToken cancellationToken = default);
}
