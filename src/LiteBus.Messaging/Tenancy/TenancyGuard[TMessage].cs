using System;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.Tenancy;

namespace LiteBus.Messaging.Tenancy;

/// <summary>
///     Guards a message by enforcing its declared tenancy position against the tenant of the mediation in flight.
/// </summary>
/// <typeparam name="TMessage">The type of message this guard runs for.</typeparam>
/// <remarks>
///     <para>
///         The guard reads the message's <see cref="TenancyDeclaration" /> through the shared
///         <see cref="DeclarationGuard{TMessage, TValue}" /> lookup, so it follows the same rules: the runtime message
///         type is read, a recorded exemption allows, and a message declaring nothing reaches
///         <see cref="DeclarationGuard{TMessage, TenancyDeclaration}.DecideUndeclaredAsync(TMessage, CancellationToken)" />.
///     </para>
///     <para>
///         A <see cref="TenantRequiredDeclaration" /> is denied with
///         <see cref="TenancyRefusalCodes.TenantRequired" /> when the execution identity of the mediation in flight has
///         no tenant, and with <see cref="TenancyRefusalCodes.Invalid" /> when the tenant is malformed under the
///         registered <see cref="ITenantIdentifierValidator" />. A <see cref="CrossTenantDeclaration" /> and a
///         <see cref="NoTenantDeclaration" /> allow, because the message already stated it runs outside one tenant.
///     </para>
///     <para>
///         Register it once as an open generic through <c>UseTenancy</c> and it covers every message. A message declaring
///         nothing is allowed, so an application adopts tenancy declaration by declaration; pair the guard with
///         <c>RequireDeclaration&lt;TenancyDeclaration&gt;()</c> so composition fails for a message that states no
///         position.
///     </para>
///     <para>
///         The guard runs at <see cref="HandlerPriorities.Tenancy" />, after the authorization guard at
///         <see cref="HandlerPriorities.Authorization" />, so a signed-out caller receives the authorization refusal
///         before the tenancy one. An application that wants the tenant checked before a permission puts a tenant
///         check inside its authorization policy instead of reordering the guards.
///     </para>
/// </remarks>
/// <example>
///     <code><![CDATA[
/// registry.AddMessaging(messaging => messaging
///     .UseTenancy()
///     .RequireDeclaration<TenancyDeclaration>());
/// ]]></code>
/// </example>
[HandlerPriority(HandlerPriorities.Tenancy)]
public sealed class TenancyGuard<TMessage> : DeclarationGuard<TMessage, TenancyDeclaration>
    where TMessage : notnull
{
    /// <summary>
    ///     The dispatch scope the decision resolves the tenant validator from.
    /// </summary>
    private readonly IServiceProvider _services;

    /// <summary>
    ///     Initializes a new instance of the <see cref="TenancyGuard{TMessage}" /> class.
    /// </summary>
    /// <param name="metadataAccessor">Reads the tenancy declaration for the message being mediated.</param>
    /// <param name="services">The dispatch scope the decision resolves the tenant validator from.</param>
    public TenancyGuard(IMessageMetadataAccessor metadataAccessor, IServiceProvider services)
        : base(metadataAccessor)
    {
        ArgumentNullException.ThrowIfNull(services);

        _services = services;
    }

    /// <summary>
    ///     Decides whether a message carrying a tenancy declaration is permitted to proceed.
    /// </summary>
    /// <param name="declaration">The tenancy declaration read from the message.</param>
    /// <param name="message">The message being mediated.</param>
    /// <param name="cancellationToken">The cancellation token supplied to the mediation operation.</param>
    /// <returns>The verdict that allows or denies the message.</returns>
    /// <remarks>
    ///     A <see cref="TenantRequiredDeclaration" /> is denied with
    ///     <see cref="TenancyRefusalCodes.TenantRequired" /> when the execution identity of the mediation in flight
    ///     has no tenant, and with <see cref="TenancyRefusalCodes.Invalid" /> when the tenant is malformed under the
    ///     registered <see cref="ITenantIdentifierValidator" />. A <see cref="CrossTenantDeclaration" /> and a
    ///     <see cref="NoTenantDeclaration" /> allow.
    /// </remarks>
    protected override Task<Verdict> DecideAsync(
        TenancyDeclaration declaration,
        TMessage message,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(declaration);
        ArgumentNullException.ThrowIfNull(message);

        if (declaration is not TenantRequiredDeclaration)
        {
            return Task.FromResult(Verdict.Allow);
        }

        var identity = AmbientExecutionContext.Current.Identity;

        if (identity.TenantId is not string tenantId || string.IsNullOrWhiteSpace(tenantId))
        {
            var reason = $"The message '{message.GetType().Name}' requires a tenant, but the current execution has none.";

            return Task.FromResult(Verdict.Deny(reason, TenancyRefusalCodes.TenantRequired));
        }

        return DecideTenantAsync(message, tenantId, cancellationToken);
    }

    /// <summary>
    ///     Decides whether a message declaring no tenancy position is permitted to proceed.
    /// </summary>
    /// <param name="message">The message being mediated.</param>
    /// <param name="cancellationToken">The cancellation token supplied to the mediation operation.</param>
    /// <returns>The verdict that allows the message.</returns>
    /// <remarks>
    ///     Allowed, so an application adopts tenancy declaration by declaration. Pair the guard with a composition
    ///     requirement over <see cref="TenancyDeclaration" /> so this override becomes unreachable once every message
    ///     declares its position.
    /// </remarks>
    protected override Task<Verdict> DecideUndeclaredAsync(TMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        return Task.FromResult(Verdict.Allow);
    }

    /// <summary>
    ///     Decides whether a tenant-required message running in a tenant is permitted to proceed.
    /// </summary>
    /// <param name="message">The message being mediated.</param>
    /// <param name="tenantId">The non-blank tenant the mediation runs in.</param>
    /// <param name="cancellationToken">The cancellation token supplied to the mediation operation.</param>
    /// <returns>The verdict that allows the message, or denies it when the tenant is malformed.</returns>
    /// <remarks>
    ///     The validator resolves from the dispatch scope at evaluation time, so the decision observes the same
    ///     validator the tenant policies and tenant sources read. With no validator registered, a present tenant is
    ///     accepted.
    /// </remarks>
    private async Task<Verdict> DecideTenantAsync(TMessage message, string tenantId, CancellationToken cancellationToken)
    {
        if (_services.GetService(typeof(ITenantIdentifierValidator)) is ITenantIdentifierValidator validator
            && !await validator.IsValidAsync(tenantId, _services, cancellationToken).ConfigureAwait(false))
        {
            var reason = $"The message '{message.GetType().Name}' runs in tenant '{tenantId}', which is malformed.";

            return Verdict.Deny(reason, TenancyRefusalCodes.Invalid);
        }

        return Verdict.Allow;
    }
}
