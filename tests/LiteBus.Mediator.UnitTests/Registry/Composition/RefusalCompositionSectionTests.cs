using System.Text.Json;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Runtime.Abstractions.Exceptions;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Mediator.UnitTests.Registry.Composition;

/// <summary>
///     Verifies that refusal mappings registered on the messaging builder reach the composed catalog and the
///     composition model, and that conflicting mappings fail composition.
/// </summary>
[Collection("Sequential")]
public sealed class RefusalCompositionSectionTests : LiteBusTestBase
{
    [Fact]
    public void ComposedCatalog_ResolvesProductMapping()
    {
        using var provider = Build(messaging => messaging.Refusals.Map(
            "billing.unpaid",
            RefusalKind.Conflict,
            "Billing conflict",
            retryable: true,
            owner: "billing"));

        var catalog = provider.GetRequiredService<IRefusalCatalog>();

        catalog.TryResolve("billing.unpaid", out var entry).Should().BeTrue();
        entry!.Kind.Should().Be(RefusalKind.Conflict);
        entry.Title.Should().Be("Billing conflict");
        entry.Retryable.Should().BeTrue();
        entry.Owner.Should().Be("billing");
    }

    [Fact]
    public void ValidateComposition_ModelCarriesRefusalsSection()
    {
        var model = Capture(messaging => messaging.Refusals.Map(
            "billing.unpaid",
            RefusalKind.Conflict,
            "Billing conflict",
            retryable: true,
            owner: "billing"));

        var section = model.Sections.Single(contributor => contributor.SectionName == "refusals");
        var data = section.GetData().Should().BeOfType<SortedDictionary<string, object?>>().Subject;
        var entries = data["entries"].Should().BeAssignableTo<IReadOnlyList<SortedDictionary<string, object?>>>().Subject;

        var billing = entries.Single(entry => (string) entry["pattern"]! == "billing.unpaid");
        billing["kind"].Should().Be("Conflict");
        billing["title"].Should().Be("Billing conflict");
        billing["retryable"].Should().Be(true);
        billing["owner"].Should().Be("billing");
        entries.Should().Contain(entry => (string) entry["pattern"]! == "concurrency.conflict");
    }

    [Fact]
    public void ToJson_CarriesRefusalsSection()
    {
        using var document = JsonDocument.Parse(Capture(_ => { }).ToJson());

        var entries = document.RootElement.GetProperty("sections").GetProperty("refusals").GetProperty("entries");

        entries.EnumerateArray().Should().HaveCount(10);
        var conflict = entries.EnumerateArray()
            .Single(entry => entry.GetProperty("pattern").GetString() == "concurrency.conflict");
        conflict.GetProperty("kind").GetString().Should().Be("Conflict");
        conflict.GetProperty("title").GetString().Should().Be("Concurrency conflict");
        conflict.GetProperty("retryable").GetBoolean().Should().BeTrue();
        conflict.GetProperty("owner").GetString().Should().Be("LiteBus");
    }

    [Fact]
    public void ConflictingMappingsAcrossCallbacks_FailCompositionNamingBoth()
    {
        var act = () => new ServiceCollection()
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(messaging => messaging.Refusals.Map(
                    "billing.unpaid",
                    RefusalKind.Conflict,
                    "Billing conflict",
                    retryable: true,
                    owner: "billing"));
                registry.AddMessaging(messaging => messaging.Refusals.Map(
                    "billing.unpaid",
                    RefusalKind.Denied,
                    "Billing denied",
                    retryable: false,
                    owner: "checkout"));
            })
            .BuildServiceProvider();

        act.Should().Throw<LiteBusConfigurationException>()
            .WithMessage("*billing.unpaid*billing*checkout*Replace*");
    }

    [Fact]
    public void IdenticalMappingsAcrossCallbacks_ComposeKeepingFirstOwner()
    {
        using var provider = new ServiceCollection()
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(messaging => messaging.Refusals.Map(
                    "billing.unpaid",
                    RefusalKind.Conflict,
                    "Billing conflict",
                    retryable: true,
                    owner: "billing"));
                registry.AddMessaging(messaging => messaging.Refusals.Map(
                    "billing.unpaid",
                    RefusalKind.Conflict,
                    "Billing conflict",
                    retryable: true,
                    owner: "checkout"));
            })
            .BuildServiceProvider();

        var catalog = provider.GetRequiredService<IRefusalCatalog>();

        catalog.TryResolve("billing.unpaid", out var entry).Should().BeTrue();
        entry!.Owner.Should().Be("billing");
    }

    /// <summary>
    ///     Builds a provider over a messaging-only composition with the given messaging configuration.
    /// </summary>
    /// <param name="messaging">The messaging configuration under test.</param>
    /// <returns>The configured service provider.</returns>
    private static ServiceProvider Build(Action<MessageModuleBuilder> messaging)
    {
        return new ServiceCollection()
            .AddLiteBus(registry => registry.AddMessaging(messaging))
            .BuildServiceProvider();
    }

    /// <summary>
    ///     Captures the composition model handed to a composition check.
    /// </summary>
    /// <param name="messaging">The messaging configuration under test.</param>
    /// <returns>The captured model.</returns>
    private static ICompositionModel Capture(Action<MessageModuleBuilder> messaging)
    {
        ICompositionModel? captured = null;

        Build(builder =>
        {
            messaging(builder);
            builder.ValidateComposition(model => captured = model);
        });

        captured.Should().NotBeNull();
        return captured!;
    }
}
