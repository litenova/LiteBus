using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Storage.InMemory;
using LiteBus.Runtime.Abstractions.Exceptions;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Outbox.UnitTests;

[Collection("Sequential")]
public sealed class OutboxReactionRetryCompositionTests : LiteBusTestBase
{
    /// <summary>
    ///     Verifies the default composition reports the default policy, its bound, and its curve.
    /// </summary>
    [Fact]
    public void Composition_should_report_default_reaction_retry_policy()
    {
        using var provider = CreateProvider(_ => { });

        var model = provider.GetRequiredService<ICompositionModel>();
        var port = model.Ports.Single(entry => entry.PortType == typeof(IOutboxReactionRetryPolicy));

        port.ImplementationType.Should().Be<DefaultOutboxReactionRetryPolicy>();
        port.IsDefault.Should().BeTrue();

        var data = SectionData(model);
        data["enabled"].Should().Be(true);
        data["isDefault"].Should().Be(true);
        data["policy"].Should().Be(typeof(DefaultOutboxReactionRetryPolicy).FullName);
        data["maxAttempts"].Should().Be(3);
        data["initialDelayMs"].Should().Be(100.0);
        data["maxDelayMs"].Should().Be(5000.0);
        data["useJitter"].Should().Be(true);
    }

    /// <summary>
    ///     Verifies a replacement policy is resolved and reported as replaced with the effective bound.
    /// </summary>
    [Fact]
    public void Composition_should_report_replaced_reaction_retry_policy()
    {
        var policy = new StubRetryPolicy();

        using var provider = CreateProvider(builder =>
        {
            builder.UseReactionRetry(options => options.MaxAttempts = 5);
            builder.UseReactionRetryPolicyInstance(policy);
        });

        provider.GetRequiredService<IOutboxReactionRetryPolicy>().Should().BeSameAs(policy);

        var data = SectionData(provider.GetRequiredService<ICompositionModel>());
        data["enabled"].Should().Be(true);
        data["isDefault"].Should().Be(false);
        data["policy"].Should().Be(typeof(StubRetryPolicy).FullName);
        data["maxAttempts"].Should().Be(5);
    }

    /// <summary>
    ///     Verifies a disabled retry is reported as disabled.
    /// </summary>
    [Fact]
    public void Composition_should_report_disabled_reaction_retry()
    {
        using var provider = CreateProvider(builder => builder.UseReactionRetry(options => options.Enabled = false));

        SectionData(provider.GetRequiredService<ICompositionModel>())["enabled"].Should().Be(false);
    }

    /// <summary>
    ///     Verifies an unusable bound fails composition before any dispatch runs.
    /// </summary>
    [Fact]
    public void Composition_should_reject_reaction_retry_bound_below_one()
    {
        var act = () => new ServiceCollection()
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(_ =>
                {
                });

                registry.AddOutbox(builder =>
                {
                    builder.UseInMemoryStorage();
                    builder.UseReactionRetry(options => options.MaxAttempts = 0);
                });
            })
            .BuildServiceProvider();

        act.Should().Throw<LiteBusConfigurationException>();
    }

    private static ServiceProvider CreateProvider(Action<OutboxModuleBuilder> configure)
    {
        return new ServiceCollection()
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(_ =>
                {
                });

                registry.AddOutbox(builder =>
                {
                    builder.UseInMemoryStorage();
                    configure(builder);
                });
            })
            .BuildServiceProvider();
    }

    private static IReadOnlyDictionary<string, object?> SectionData(ICompositionModel model)
    {
        var section = model.Sections.Single(contributor => contributor.SectionName == "outbox-reaction-retry");

        return section.GetData().Should().BeAssignableTo<IReadOnlyDictionary<string, object?>>().Subject;
    }

    private sealed class StubRetryPolicy : IOutboxReactionRetryPolicy
    {
        public ValueTask<OutboxReactionRetryDecision> DecideAsync(
            OutboxReactionRetryContext context,
            CancellationToken cancellationToken)
        {
            return new ValueTask<OutboxReactionRetryDecision>(new OutboxReactionRetryDecision.GiveUp());
        }
    }
}
