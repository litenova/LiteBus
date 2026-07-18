using System;
using System.Threading;
using System.Threading.Tasks;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;

namespace LiteBus.Transport.Amqp;

/// <summary>
///     Creates and reuses one AMQP connection for LiteBus transport adapters.
/// </summary>
public sealed class AmqpConnectionManager : IAmqpConnectionManager
{
    /// <summary>
    ///     Gets the circuit breaker that guards broker connectivity.
    /// </summary>
    private readonly TransportCircuitBreaker _circuitBreaker;

    /// <summary>
    ///     Serializes connection creation so only one shared connection is opened.
    /// </summary>
    private readonly SemaphoreSlim _connectionGate = new(1, 1);

    /// <summary>
    ///     Gets the connection settings used to connect to the broker.
    /// </summary>
    private readonly AmqpConnectionOptions _options;

    /// <summary>
    ///     Gets the lazily created shared connection, if one has been opened.
    /// </summary>
    private IConnection? _connection;

    /// <summary>
    ///     Initializes a new instance of the <see cref="AmqpConnectionManager" /> class.
    /// </summary>
    /// <param name="options">The connection settings used to connect to the broker.</param>
    public AmqpConnectionManager(AmqpConnectionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        _circuitBreaker = new TransportCircuitBreaker(options.CircuitBreaker.ToTransportOptions());
    }

    /// <inheritdoc />
    /// <remarks>
    ///     Broker connectivity exceptions are handled explicitly so connection failures increment the circuit breaker.
    ///     The final <see cref="Exception" /> handler records any other non-cancellation failure before rethrowing.
    /// </remarks>
    public async Task<IConnection> GetConnectionAsync(CancellationToken cancellationToken = default)
    {
        if (_connection is { IsOpen: true })
        {
            return _connection;
        }

        await _connectionGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (_connection is { IsOpen: true })
            {
                return _connection;
            }

            if (_connection is not null)
            {
                await _connection.DisposeAsync().ConfigureAwait(false);
                _connection = null;
            }

            var permit = _circuitBreaker.AcquirePermit();

            try
            {
                var factory = CreateConnectionFactory();
                _connection = await factory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
                _circuitBreaker.RecordSuccess(permit);
                return _connection;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (BrokerUnreachableException)
            {
                _circuitBreaker.RecordFailure(permit);
                throw;
            }
            catch (AlreadyClosedException)
            {
                _circuitBreaker.RecordFailure(permit);
                throw;
            }
            catch (OperationInterruptedException)
            {
                _circuitBreaker.RecordFailure(permit);
                throw;
            }
#pragma warning disable CA1031 // Last-resort connection boundary records circuit breaker failures before rethrowing.
            catch (Exception)
#pragma warning restore CA1031
            {
                _circuitBreaker.RecordFailure(permit);
                throw;
            }
        }
        finally
        {
            _connectionGate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<IChannel> CreateChannelAsync(CancellationToken cancellationToken = default)
    {
        var connection = await GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        return await connection.CreateChannelAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IChannel> CreatePublisherChannelAsync(CancellationToken cancellationToken = default)
    {
        var connection = await GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        return await connection.CreateChannelAsync(
                new CreateChannelOptions(true, true, null, null),
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _connectionGate.WaitAsync().ConfigureAwait(false);

        try
        {
            if (_connection is not null)
            {
                await _connection.DisposeAsync().ConfigureAwait(false);
                _connection = null;
            }
        }
        finally
        {
            _connectionGate.Release();
            _connectionGate.Dispose();
        }
    }

    /// <summary>
    ///     Creates a RabbitMQ client connection factory from the configured options.
    /// </summary>
    /// <returns>The configured connection factory.</returns>
    private ConnectionFactory CreateConnectionFactory()
    {
        var factory = new ConnectionFactory
        {
            AutomaticRecoveryEnabled = _options.AutomaticRecoveryEnabled,
            NetworkRecoveryInterval = _options.NetworkRecoveryInterval
        };

        if (_options.Uri is not null)
        {
            factory.Uri = _options.Uri;
        }
        else
        {
            factory.HostName = _options.HostName;
            factory.Port = _options.Port;
            factory.VirtualHost = _options.VirtualHost;
            factory.UserName = _options.UserName;
            factory.Password = _options.Password;
        }

        if (!string.IsNullOrWhiteSpace(_options.ClientProvidedName))
        {
            factory.ClientProvidedName = _options.ClientProvidedName;
        }

        return factory;
    }
}
