using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Runtime.Abstractions.Diagnostics;

namespace LiteBus.Transport.Amqp;

/// <summary>
///     Verifies that the configured AMQP broker accepts a connection.
/// </summary>
public sealed class AmqpConnectivityDiagnosticCheck : IDiagnosticCheck
{
    /// <summary>
    ///     Gets the stable probe name reported to operators.
    /// </summary>
    public string Name => "transport.amqp.connectivity";

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
        _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
    }

    /// <inheritdoc />
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
        catch (Exception exception)
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
}
