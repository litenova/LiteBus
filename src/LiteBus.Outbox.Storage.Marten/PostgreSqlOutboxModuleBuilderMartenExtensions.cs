using System;
using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Storage.PostgreSql;
using LiteBus.Runtime.Abstractions.Exceptions;
using Marten;

namespace LiteBus.Outbox.Storage.Marten;

/// <summary>
///     Registers the Marten queued transactional outbox through <see cref="PostgreSqlOutboxModuleBuilder" />.
/// </summary>
public static class PostgreSqlOutboxModuleBuilderMartenExtensions
{
    /// <summary>
    ///     Registers scoped <see cref="ITransactionalOutbox" /> backed by the current Marten document session.
    /// </summary>
    /// <param name="builder">The PostgreSQL outbox module builder.</param>
    /// <returns>The PostgreSQL outbox module builder for chaining.</returns>
    /// <remarks>
    ///     <para>
    ///         The application registers Marten itself with <c>AddMarten</c> against the same database as the outbox;
    ///         LiteBus does not register Marten. Staged envelopes are queued on the scoped
    ///         <see cref="IDocumentSession" /> and commit with its <c>SaveChangesAsync</c> alongside documents.
    ///     </para>
    ///     <para>
    ///         This cannot be combined with <c>EnableAmbientTransactionProvider()</c>: both register
    ///         <see cref="ITransactionalOutbox" />, so module build throws when both are configured.
    ///     </para>
    /// </remarks>
    public static PostgreSqlOutboxModuleBuilder UseMartenTransactionalOutbox(this PostgreSqlOutboxModuleBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.TransactionalOutboxFactory = serviceProvider => new MartenTransactionalOutbox(
            (IDocumentSession) (serviceProvider.GetService(typeof(IDocumentSession)) ?? throw new LiteBusDependencyResolutionException(typeof(IDocumentSession))),
            (IOutboxEnvelopeFactory) (serviceProvider.GetService(typeof(IOutboxEnvelopeFactory)) ?? throw new LiteBusDependencyResolutionException(typeof(IOutboxEnvelopeFactory))),
            (PostgreSqlOutboxStoreRegistration) (serviceProvider.GetService(typeof(PostgreSqlOutboxStoreRegistration)) ?? throw new LiteBusDependencyResolutionException(typeof(PostgreSqlOutboxStoreRegistration))));

        return builder;
    }
}
