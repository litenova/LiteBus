using System;

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
    ///     Declares that the message requires authorization under a policy.
    /// </summary>
    /// <param name="policy">The policy the application authorizes the message against, such as <c>vaults.open</c>.</param>
    /// <returns>The collection, for chaining.</returns>
    /// <exception cref="System.ArgumentException"><paramref name="policy" /> is null, empty, or whitespace.</exception>
    /// <remarks>
    ///     LiteBus carries the policy to the application's guards and never evaluates it.
    /// </remarks>
    IMessageDeclarations Authorized(string policy);

    /// <summary>
    ///     Declares that the message is deliberately exempt from authorization, and records why.
    /// </summary>
    /// <param name="rationale">The recorded reason the message needs no authorization.</param>
    /// <returns>The collection, for chaining.</returns>
    /// <exception cref="System.ArgumentException"><paramref name="rationale" /> is null, empty, or whitespace.</exception>
    /// <remarks>
    ///     Equivalent to <c>Exempt&lt;AuthorizationDeclaration&gt;(rationale)</c>, and recorded as that exemption too, so
    ///     the position satisfies <c>RequireDeclaration&lt;AuthorizationDeclaration&gt;</c> and reads back from one place.
    /// </remarks>
    IMessageDeclarations AuthorizationExempt(string rationale);

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

    /// <summary>
    ///     Declares the refusal codes the message can produce.
    /// </summary>
    /// <param name="refusals">The refusal codes, each with its kind, title, and retryability.</param>
    /// <returns>The collection, for chaining.</returns>
    /// <exception cref="System.ArgumentNullException"><paramref name="refusals" /> is <see langword="null" />.</exception>
    /// <remarks>
    ///     <para>
    ///         At composition, every message's declarations are harvested into the refusal catalog, so transports
    ///         answer the codes without a hand-registered map. A code declared here and registered through
    ///         composition for the same meaning is an identical repeat; a conflicting repeat fails composition.
    ///     </para>
    ///     <para>
    ///         The default implementation stores a <see cref="RefusalDeclarations" /> value, so a definition that
    ///         declares refusals needs no other member.
    ///     </para>
    /// </remarks>
    IMessageDeclarations Refusals(params RefusalDeclaration[] refusals)
    {
        ArgumentNullException.ThrowIfNull(refusals);

        return Declare(new RefusalDeclarations(refusals));
    }
}
