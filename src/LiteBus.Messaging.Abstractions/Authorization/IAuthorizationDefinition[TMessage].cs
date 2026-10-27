namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Declares the authorization position of <typeparamref name="TMessage" /> from a definition class beside the message.
/// </summary>
/// <typeparam name="TMessage">The message type this definition describes.</typeparam>
/// <remarks>
///     <para>
///         This is the compile-checked alternative to <see cref="AuthorizedAttribute" /> and
///         <see cref="AuthorizationExemptAttribute" />. Implementing it forces the message to state its position, and the
///         declaration is written in ordinary C# where constants and shared vocabulary are available.
///     </para>
///     <para>
///         A definition takes precedence over an attribute declaring the same message, because both contribute an
///         <see cref="AuthorizationDeclaration" /> and definitions are applied after attributes.
///     </para>
/// </remarks>
/// <example>
///     <code><![CDATA[
/// public sealed class OpenVaultCommandDefinition : IAuthorizationDefinition<OpenVaultCommand>
/// {
///     public AuthorizationDeclaration Authorization => AuthorizationDeclaration.Authorized(Policies.Vaults.Open);
/// }
/// ]]></code>
/// </example>
public interface IAuthorizationDefinition<TMessage> : IMessageDefinition<TMessage, AuthorizationDeclaration>
    where TMessage : notnull
{
    /// <summary>
    ///     Gets the authorization declaration for <typeparamref name="TMessage" />.
    /// </summary>
    AuthorizationDeclaration Authorization { get; }

    /// <inheritdoc />
    AuthorizationDeclaration IMessageDefinition<TMessage, AuthorizationDeclaration>.Value => Authorization;
}
