using System;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Collects the declarations a multi-message definition contributes for many message types.
/// </summary>
/// <remarks>
///     <para>
///         Handed to <see cref="IMultiMessageDefinition.Describe" />. Each <see cref="Describe{TMessage}" /> call
///         names one message type and receives the same <see cref="IMessageDeclarations" /> a definition class uses,
///         so a source declares authorization, audit, idempotency, tenancy, and custom values with the same
///         vocabulary and the same per-message conflict rule.
///     </para>
///     <para>
///         A source describes explicit per-message values, which is what separates it from
///         <c>DeclareDefault</c> and <c>DeclareConvention</c>: a default states one constant for a scope in composition
///         code and is invisible to the analyzers, and a convention derives a value from the message type with no
///         conflict detection. A source is discoverable, conflict-checked, and analyzer-visible like a definition.
///     </para>
/// </remarks>
/// <example>
///     <code><![CDATA[
/// internal sealed class BillingDeclarations : IMultiMessageDefinition
/// {
///     public void Describe(IMessageDeclarationCatalog catalog)
///     {
///         catalog
///             .Describe<ChargeCardCommand>(declarations => declarations.Declare(Permissions.Billing.Charge))
///             .Describe<RefundCommand>(declarations => declarations.Declare(Permissions.Billing.Refund));
///     }
/// }
/// ]]></code>
/// </example>
public interface IMessageDeclarationCatalog
{
    /// <summary>
    ///     Describes the metadata for one message type.
    /// </summary>
    /// <typeparam name="TMessage">The message type the declarations apply to.</typeparam>
    /// <param name="describe">Receives the collection that records every declaration for the message.</param>
    /// <returns>The catalog, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="describe" /> is <see langword="null" />.</exception>
    IMessageDeclarationCatalog Describe<TMessage>(Action<IMessageDeclarations> describe)
        where TMessage : notnull;

    /// <summary>
    ///     Describes the metadata for one message type named as a value.
    /// </summary>
    /// <param name="messageType">The message type the declarations apply to.</param>
    /// <param name="describe">Receives the collection that records every declaration for the message.</param>
    /// <returns>The catalog, for chaining.</returns>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="messageType" /> or <paramref name="describe" /> is <see langword="null" />.
    /// </exception>
    /// <remarks>
    ///     The generic overload is the one to reach for. This exists for generated and configuration-driven sources
    ///     where the message type is a value rather than something the call site can name.
    /// </remarks>
    IMessageDeclarationCatalog Describe(Type messageType, Action<IMessageDeclarations> describe);

    /// <summary>
    ///     Describes the metadata for one message type, overwriting any declaration already recorded for the same
    ///     message and value type.
    /// </summary>
    /// <typeparam name="TMessage">The message type the declarations apply to.</typeparam>
    /// <param name="describe">Receives the collection that records every declaration for the message.</param>
    /// <returns>The catalog, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="describe" /> is <see langword="null" />.</exception>
    /// <remarks>
    ///     This is the deliberate override for the registration conflict rule. A conflicting repeat through
    ///     <see cref="Describe{TMessage}" /> fails composition naming both sources, while this method overwrites the
    ///     recorded value.
    /// </remarks>
    IMessageDeclarationCatalog Replace<TMessage>(Action<IMessageDeclarations> describe)
        where TMessage : notnull;

    /// <summary>
    ///     Describes the metadata for one message type named as a value, overwriting any declaration already recorded
    ///     for the same message and value type.
    /// </summary>
    /// <param name="messageType">The message type the declarations apply to.</param>
    /// <param name="describe">Receives the collection that records every declaration for the message.</param>
    /// <returns>The catalog, for chaining.</returns>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="messageType" /> or <paramref name="describe" /> is <see langword="null" />.
    /// </exception>
    /// <remarks>
    ///     The generic overload is the one to reach for. This exists for generated and configuration-driven sources
    ///     where the message type is a value rather than something the call site can name.
    /// </remarks>
    IMessageDeclarationCatalog Replace(Type messageType, Action<IMessageDeclarations> describe);
}
