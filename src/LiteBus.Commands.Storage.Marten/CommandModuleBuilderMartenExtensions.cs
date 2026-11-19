using System;

namespace LiteBus.Commands.Storage.Marten;

/// <summary>
///     Registers the Marten unit of work and idempotency store through <see cref="CommandModuleBuilder" />.
/// </summary>
public static class CommandModuleBuilderMartenExtensions
{
    /// <summary>
    ///     Commits the scoped Marten document session when a command mediation completes successfully.
    /// </summary>
    /// <param name="builder">The command module builder.</param>
    /// <returns>The command module builder for chaining.</returns>
    /// <remarks>
    ///     <para>
    ///         Delegates to the command <c>UseUnitOfWork</c> hook with <see cref="MartenUnitOfWork" />: the instance is
    ///         resolved scoped, and the completion handler the hook registers calls <c>CommitAsync</c>, which saves the
    ///         session, only when the mediation completed successfully. The application registers Marten itself with
    ///         <c>AddMarten</c>, which supplies the scoped session; LiteBus does not register Marten.
    ///     </para>
    ///     <para>
    ///         Query mediations commit the same session by calling the query <c>UseUnitOfWork</c> hook with
    ///         <see cref="MartenUnitOfWork" /> directly. That call needs no extension in this package, and adding one
    ///         would pull the query axis onto every consumer of a command storage package.
    ///     </para>
    /// </remarks>
    public static CommandModuleBuilder UseMartenUnitOfWork(this CommandModuleBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.UseUnitOfWork<MartenUnitOfWork>();
    }

    /// <summary>
    ///     Registers the Marten idempotency store as a command child module.
    /// </summary>
    /// <param name="builder">The command module builder.</param>
    /// <param name="configure">The optional Marten idempotency store configuration action.</param>
    /// <returns>The command module builder for chaining.</returns>
    /// <remarks>
    ///     <para>
    ///         The store resolves the application-owned <c>IDocumentStore</c> and the messaging
    ///         <c>TimeProvider</c>, and is registered as a singleton <c>IIdempotencyStore</c>. An application that
    ///         registers its own <c>IIdempotencyStore</c> should not add this module. Pair with
    ///         <c>EnableIdempotency</c>, which registers the shortcut that claims and the completion handler that
    ///         settles.
    ///     </para>
    /// </remarks>
    public static CommandModuleBuilder UseMartenIdempotencyStore(
        this CommandModuleBuilder builder,
        Action<MartenIdempotencyStoreOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var options = new MartenIdempotencyStoreOptions();
        configure?.Invoke(options);

        return builder.RegisterChildModule(new MartenIdempotencyModule(options));
    }

    /// <summary>
    ///     Registers the Marten tenant session factory as a command child module.
    /// </summary>
    /// <param name="builder">The command module builder.</param>
    /// <returns>The command module builder for chaining.</returns>
    /// <remarks>
    ///     <para>
    ///         Sessions the factory opens are outside the command's unit of work: the caller owns and disposes them,
    ///         and nothing written through them commits with the scoped session. Use them for tenant-filtered reads,
    ///         and keep writing through the scoped session.
    ///     </para>
    ///     <para>
    ///         The factory resolves the application-owned <c>IDocumentStore</c> and is registered as a singleton
    ///         <see cref="MartenTenantSessionFactory" />. Handlers take it as a constructor dependency and open
    ///         sessions for the ambient tenant through it, so tenant filtering needs no application code at the call
    ///         site.
    ///     </para>
    ///     <para>
    ///         The application must configure Marten for conjoined tenancy. Without it, opening a tenant session
    ///         fails in Marten rather than filtering silently; see <see cref="MartenTenantSessionFactory" />.
    ///     </para>
    /// </remarks>
    public static CommandModuleBuilder UseMartenTenantSessions(this CommandModuleBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.RegisterChildModule(new MartenTenantSessionModule());
    }
}
