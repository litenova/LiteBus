using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Dispatch.InProcess;
using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Dispatch.InProcess;
using LiteBus.Samples.V6;
using LiteBus.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace LiteBus.Composition.UnitTests;

/// <summary>
///     Smoke tests for the LiteBus v6 sample composition.
/// </summary>
public sealed class LiteBusV6CompositionSmokeTests : LiteBusTestBase
{
    /// <summary>
    ///     Verifies that the sample v6 composition registers inbox, outbox, dispatchers, and hosted processors.
    /// </summary>
    [Fact]
    public void AddLiteBusV6_ShouldRegisterCoreServicesAndHostedProcessors()
    {
        var services = new ServiceCollection();
        services.AddLiteBusV6(new ConfigurationBuilder().Build());

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IInbox>().Should().NotBeNull();
        provider.GetRequiredService<IOutbox>().Should().NotBeNull();
        provider.GetRequiredService<IInboxDispatcher>().Should().BeOfType<CommandInboxDispatcher>();
        provider.GetRequiredService<IOutboxDispatcher>().Should().BeOfType<EventOutboxDispatcher>();

        provider.GetServices<IHostedService>().Should().HaveCountGreaterThanOrEqualTo(2);
    }
}
