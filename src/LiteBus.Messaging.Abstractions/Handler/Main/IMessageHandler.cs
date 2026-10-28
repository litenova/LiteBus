namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Marks a type as a main message handler, the non-generic base of every closed main handler contract.
/// </summary>
/// <remarks>
///     <para>
///         The interface has no members. The mediator calls a main handler through the closed
///         <see cref="IMessageHandler{TMessage,TMessageResult}" /> contract recorded on the handler's
///         <see cref="IMainHandlerDescriptor" />, never through an untyped entry point on the handler.
///     </para>
///     <para>
///         Because nothing here is implemented per contract, one class can implement several closed handler contracts,
///         such as <c>IEventHandler&lt;OrderPlaced&gt;</c> and <c>IEventHandler&lt;OrderCancelled&gt;</c>, and each
///         registration dispatches to the contract it names.
///     </para>
/// </remarks>
public interface IMessageHandler;
