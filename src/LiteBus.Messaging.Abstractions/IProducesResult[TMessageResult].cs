namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     States the result type a message produces, in a form the registry can read without knowing the axis.
/// </summary>
/// <typeparam name="TMessageResult">The result type the message produces.</typeparam>
/// <remarks>
///     <para>
///         <c>ICommand&lt;TCommandResult&gt;</c>, <c>IQuery&lt;TQueryResult&gt;</c> and
///         <c>IStreamQuery&lt;TQueryResult&gt;</c> all derive from this, so one rule reads the result type from any of
///         them. Applications do not implement it directly; deriving from the axis contract is enough.
///     </para>
///     <para>
///         It exists so an open generic handler can take a second type parameter. Closing
///         <c>AuditPostHandler&lt;TCommand, TResult&gt;</c> for a concrete command means knowing what that command
///         returns, and the messaging registry cannot reference the command or query packages to find out. Reading it
///         from the message's own contract also means the answer does not depend on a main handler having been
///         registered yet.
///     </para>
/// </remarks>
public interface IProducesResult<out TMessageResult>;
