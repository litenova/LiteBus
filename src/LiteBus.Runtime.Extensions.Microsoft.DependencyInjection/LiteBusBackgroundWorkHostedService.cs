using System;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Runtime.Abstractions;
using Microsoft.Extensions.Hosting;

namespace LiteBus.Runtime.Extensions.Microsoft.DependencyInjection;

/// <summary>
///     Adapts <see cref="ILiteBusBackgroundWork" /> to the generic host <see cref="IHostedService" /> contract.
/// </summary>
internal sealed class LiteBusBackgroundWorkHostedService : BackgroundService
{
    /// <summary>
    ///     The background work executed by the host.
    /// </summary>
    private readonly ILiteBusBackgroundWork _work;

    /// <summary>
    ///     Initializes a new instance of the <see cref="LiteBusBackgroundWorkHostedService" /> class.
    /// </summary>
    /// <param name="work">The background work executed by the host.</param>
    public LiteBusBackgroundWorkHostedService(ILiteBusBackgroundWork work)
    {
        _work = work ?? throw new ArgumentNullException(nameof(work));
    }

    /// <inheritdoc />
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        return _work.RunAsync(stoppingToken);
    }
}
