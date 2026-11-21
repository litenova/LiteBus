namespace LiteBus.Messaging.Abstractions.Tenancy;

/// <summary>
///     Declares the tenancy position of <typeparamref name="TMessage" /> from a definition class beside the message.
/// </summary>
/// <typeparam name="TMessage">The message type this definition describes.</typeparam>
/// <remarks>
///     Keep one definition class per concern, such as <c>PostManualEntryCommandTenancyDefinition</c>, beside the
///     message's authorization and audit definitions.
/// </remarks>
/// <example>
///     <code><![CDATA[
/// internal sealed class PostManualEntryCommandTenancyDefinition : ITenancyDefinition<PostManualEntryCommand>
/// {
///     public TenancyDeclaration Tenancy => TenancyDeclaration.Required();
/// }
/// ]]></code>
/// </example>
public interface ITenancyDefinition<TMessage> : IMessageDefinition<TMessage, TenancyDeclaration>
    where TMessage : notnull
{
    /// <summary>
    ///     Gets the tenancy declaration for <typeparamref name="TMessage" />.
    /// </summary>
    TenancyDeclaration Tenancy { get; }

    /// <inheritdoc />
    TenancyDeclaration IMessageDefinition<TMessage, TenancyDeclaration>.Value => Tenancy;
}
