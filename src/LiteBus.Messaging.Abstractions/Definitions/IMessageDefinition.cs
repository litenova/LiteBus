namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Marks a type that declares metadata for a message, discovered when the message registry is built.
/// </summary>
/// <remarks>
///     <para>
///         This non-generic marker exists for discovery. Implement one or more closed
///         <see cref="IMessageDefinition{TMessage,TValue}" /> facets instead of implementing this interface directly.
///     </para>
/// </remarks>
public interface IMessageDefinition;
