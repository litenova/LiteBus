namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Declares every piece of metadata for messages of type <typeparamref name="TMessage" /> from one method.
/// </summary>
/// <typeparam name="TMessage">The message type this definition describes.</typeparam>
/// <remarks>
///     <para>
///         This is the shape to reach for. <see cref="IMessageDefinition{TMessage,TValue}" /> types one declaration
///         against the compiler and is the better choice when a message declares exactly one thing, which is why
///         <see cref="IAuditDefinition{TMessage}" /> and <c>IIdempotencyDefinition&lt;TMessage&gt;</c> are built on it.
///         Past one declaration it stops paying: the second and every later value has to be written as an explicit
///         interface implementation that names the message type and the value type again, and a message that declares
///         an audit position and a required permission is the normal case rather than the exception.
///     </para>
///     <para>
///         Both shapes write into the same type-keyed metadata, so a codebase may use whichever fits each message and a
///         reader looks a value up by its own type either way. Declaring the same value type twice for one message is a
///         configuration error reported at registration, across both shapes.
///     </para>
///     <para>
///         A definition applies to <typeparamref name="TMessage" /> and to every message assignable to it, so a
///         definition written for a base type or a marker interface covers the messages beneath it, and the most
///         derived declaration wins.
///     </para>
///     <para>
///         Definitions are instantiated once during registration and must expose a parameterless constructor, which
///         may be non-public. They are declarative, so they cannot take dependencies, and
///         <see cref="Describe" /> must not read configuration or reach for a container.
///     </para>
/// </remarks>
/// <example>
///     <code><![CDATA[
/// internal sealed class CloseOrganizationCommandDefinition : IMessageDefinition<CloseOrganizationCommand>
/// {
///     public void Describe(IMessageDeclarations declarations)
///     {
///         declarations.Audited("organizations.close-organization", category: "organizations", targetKind: "organization");
///         declarations.Declare(new RequiredAuthorization(PermittedAction.ManagePlatformStaff, Subject.Platform));
///     }
/// }
/// ]]></code>
/// </example>
public interface IMessageDefinition<TMessage> : IMessageDefinition
    where TMessage : notnull
{
    /// <summary>
    ///     Declares the metadata for <typeparamref name="TMessage" />.
    /// </summary>
    /// <param name="declarations">The collection that receives every declaration.</param>
    void Describe(IMessageDeclarations declarations);
}
