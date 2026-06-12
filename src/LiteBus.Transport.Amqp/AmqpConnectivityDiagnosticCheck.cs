using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Runtime.Abstractions.Diagnostics;
using RabbitMQ.Client.Exceptions;

namespace LiteBus.Transport.Amqp;

/// <summary>
///     Verifies that the configured AMQP broker accepts a connection.
/// </summary>
public sealed class AmqpConnectivityDiagnosticCheck : IDiagnosticCheck
{
    /// <summary>
    ///     The connection manager used to open the shared broker connection.
    /// </summary>
    private readonly IAmqpConnectionManager _connectionManager;

    /// <summary>
    ///     Initializes a new instance of the <see cref="AmqpConnectivityDiagnosticCheck" /> class.
    /// </summary>
    /// <param name="connectionManager">The connection manager used to open the shared broker connection.</param>
    public AmqpConnectivityDiagnosticCheck(IAmqpConnectionManager connectionManager)
    {
        ArgumentNullException.ThrowIfNull(connectionManager);
        _connectionManager = connectionManager;
    }

    /// <summary>
    ///     Gets the stable probe name reported to operators.
    /// </summary>
    public string Name => "transport.amqp.connectivity";

    /// <inheritdoc />
    /// <remarks>
    ///     Broker connectivity exceptions are mapped to unhealthy results explicitly. The final
    ///     <see cref="Exception" /> handler covers unexpected failures because diagnostic probes must not throw to
    ///     callers.
    /// </remarks>
    public async Task<DiagnosticResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var connection = await _connectionManager.GetConnectionAsync(cancellationToken).ConfigureAwait(false);

            return new DiagnosticResult(
                DiagnosticStatus.Healthy,
                "AMQP broker connection is open.",
                new Dictionary<string, object>
                {
                    ["endpoint"] = connection.Endpoint.ToString(),
                    ["isOpen"] = connection.IsOpen
                });
        }
        catch (OperationCanceledException exception)
        {
            return CreateUnhealthyResult(exception);
        }
        catch (BrokerUnreachableException exception)
        {
            return CreateUnhealthyResult(exception);
        }
        catch (AlreadyClosedException exception)
        {
            return CreateUnhealthyResult(exception);
        }
#pragma warning disable CA1031 // Diagnostic probes must map unexpected failures to unhealthy results without throwing.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            return CreateUnhealthyResult(exception);
        }
    }

    /// <summary>
    ///     Creates an unhealthy diagnostic result for a connection failure.
    /// </summary>
    /// <param name="exception">The exception observed while opening the broker connection.</param>
    /// <returns>The unhealthy diagnostic result.</returns>
    private static DiagnosticResult CreateUnhealthyResult(Exception exception)
    {
        return new DiagnosticResult(
            DiagnosticStatus.Unhealthy,
            $"AMQP broker connection failed: {exception.Message}",
            new Dictionary<string, object>
            {
                ["errorType"] = exception.GetType().Name
            });
    }
}
