namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Collects the declarations a message definition contributes.
/// </summary>
/// <remarks>
///     <para>
///         Handed to <see cref="IMessageDefinition{TMessage}.Describe" />. It exists because the keyed shape,
///         <see cref="IMessageDefinition{TMessage,TValue}" />, is checked by the compiler for one declaration and
///         becomes unwieldy at two: the second and every later one has to be written as an explicit interface
///         implementation naming the message type and the value type again, which is the type name three times to say
///         one thing.
///     </para>
///     <para>
///         Declarations are keyed by value type here exactly as they are there, so the two shapes are interchangeable
///         and a message may be described by either. Declaring the same value type twice for one message is a
///         configuration error reported at registration, whichever shape declared it.
///     </para>
/// </remarks>
public interface IMessageDeclarations
{
    /// <summary>
    ///     Declares one metadata value, keyed by its own type.
    /// </summary>
    /// <typeparam name="TValue">The metadata value type, which is also its key.</typeparam>
    /// <param name="value">The value to declare.</param>
    /// <returns>The collection, for chaining.</returns>
    /// <exception cref="System.ArgumentNullException"><paramref name="value" /> is <see langword="null" />.</exception>
    /// <remarks>
    ///     The type parameter is inferred from the argument, so an application's own declaration reads as one line.
    ///     Name it explicitly to declare a value under a base type the reader looks it up by.
    /// </remarks>
    IMessageDeclarations Declare<TValue>(TValue value)
        where TValue : notnull;

    /// <summary>
    ///     Declares that the message produces an audit record.
    /// </summary>
    /// <param name="action">The use-case identity written to the record, such as <c>orders.place-order</c>.</param>
    /// <param name="category">The category used to group the record for review and retention.</param>
    /// <param name="targetKind">The kind of resource the message acts on, such as <c>order</c>.</param>
    /// <param name="reasonRequired">
    ///     Whether the handler must supply a reason. A mediation that succeeds without one raises
    ///     <see cref="AuditReasonMissingException" /> before the commit, so the work is rolled back rather than
    ///     recorded without its justification.
    /// </param>
    /// <returns>The collection, for chaining.</returns>
    /// <exception cref="System.ArgumentException"><paramref name="action" /> is null, empty, or whitespace.</exception>
    IMessageDeclarations Audited(
        string action,
        string? category = null,
        string? targetKind = null,
        bool reasonRequired = false);

    /// <summary>
    ///     Declares that the message is deliberately not audited, and records why.
    /// </summary>
    /// <param name="rationale">The recorded reason the message produces no audit record.</param>
    /// <returns>The collection, for chaining.</returns>
    /// <exception cref="System.ArgumentException"><paramref name="rationale" /> is null, empty, or whitespace.</exception>
    /// <remarks>
    ///     Equivalent to <c>Exempt&lt;AuditDeclaration&gt;(rationale)</c>, and recorded as that exemption too, so the
    ///     position satisfies <c>RequireDeclaration&lt;AuditDeclaration&gt;</c> and reads back from one place.
    /// </remarks>
    IMessageDeclarations NotAudited(string rationale);

    /// <summary>
    ///     Declares that the message deliberately states nothing for one metadata type, and records why.
    /// </summary>
    /// <typeparam name="TValue">The metadata value type the message is exempt from declaring.</typeparam>
    /// <param name="rationale">The recorded reason the message is exempt.</param>
    /// <returns>The collection, for chaining.</returns>
    /// <exception cref="System.ArgumentException"><paramref name="rationale" /> is null, empty, or whitespace.</exception>
    /// <remarks>
    ///     An exemption is a decision rather than an omission, and the difference only exists because the reason is
    ///     written down. It satisfies a matching <c>RequireDeclaration&lt;TValue&gt;</c>.
    /// </remarks>
    IMessageDeclarations Exempt<TValue>(string rationale)
        where TValue : notnull;
}
