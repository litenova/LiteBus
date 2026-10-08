using System;
using System.ComponentModel;
using System.Threading.Tasks;
using Npgsql;

namespace LiteBus.Storage.PostgreSql;

/// <summary>
///     Ties the lifetime of a module's <see cref="NpgsqlDataSource" /> to the dependency container that built the module.
/// </summary>
/// <typeparam name="TModule">
///     The module that registered the data source. Each PostgreSQL module registers its own owner, so the outbox, inbox,
///     and saga modules can each own a separate data source in one container.
/// </typeparam>
/// <remarks>
///     <para>
///         A container disposes only the services it created, and Microsoft dependency injection never disposes a
///         prebuilt instance registration. A PostgreSQL module therefore registers this owner through a singleton factory
///         and resolves it from every store and work signal registration, so the container creates, captures, and later
///         disposes the owner whenever the module's services are used.
///     </para>
///     <para>
///         When the module created the data source from a connection string, disposing the owner disposes the data source
///         and closes its connection pool. When the application supplied the data source, the owner leaves it alone: the
///         application created it and disposes it.
///     </para>
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class PostgreSqlDataSourceOwner<TModule> : IAsyncDisposable, IDisposable
{
    /// <summary>
    ///     Indicates whether this owner disposes <see cref="DataSource" /> when it is disposed.
    /// </summary>
    private readonly bool _ownsDataSource;

    /// <summary>
    ///     Initializes a new instance of the <see cref="PostgreSqlDataSourceOwner{TModule}" /> class.
    /// </summary>
    /// <param name="dataSource">The data source the module's stores and work signal use.</param>
    /// <param name="ownsDataSource">
    ///     <see langword="true" /> when the module created <paramref name="dataSource" /> and must dispose it;
    ///     <see langword="false" /> when the application supplied it.
    /// </param>
    public PostgreSqlDataSourceOwner(NpgsqlDataSource dataSource, bool ownsDataSource)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        DataSource = dataSource;
        _ownsDataSource = ownsDataSource;
    }

    /// <summary>
    ///     Gets the data source the module's stores and work signal use.
    /// </summary>
    public NpgsqlDataSource DataSource { get; }

    /// <summary>
    ///     Disposes the data source when the module owns it.
    /// </summary>
    /// <returns>A task that completes when the data source is disposed.</returns>
    public ValueTask DisposeAsync()
    {
        return _ownsDataSource ? DataSource.DisposeAsync() : ValueTask.CompletedTask;
    }

    /// <summary>
    ///     Disposes the data source when the module owns it.
    /// </summary>
    public void Dispose()
    {
        if (_ownsDataSource)
        {
            DataSource.Dispose();
        }
    }
}
