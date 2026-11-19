using System.Text.Json;
using LiteBus.Commands;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Mediator.UnitTests.Authorization;
using LiteBus.Mediator.UnitTests.Metadata;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.Authorization;
using LiteBus.Messaging.Authorization;
using LiteBus.Runtime.Abstractions;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Mediator.UnitTests.Registry.Composition;

/// <summary>
///     Verifies that the composition snapshot exports deterministic JSON for CI diffs and compliance records.
/// </summary>
[Collection("Sequential")]
public sealed class CompositionModelJsonTests : LiteBusTestBase
{
    [Fact]
    public void ToJson_carries_schema_version()
    {
        using var document = JsonDocument.Parse(CaptureModel().ToJson());

        document.RootElement.GetProperty("schemaVersion").GetInt32().Should().Be(1);
    }

    [Fact]
    public void ToJson_orders_keys_ordinally()
    {
        var json = CaptureModel().ToJson();

        int[] positions =
        [
            json.IndexOf("\"contracts\":", StringComparison.Ordinal),
            json.IndexOf("\"declaredPolicies\":", StringComparison.Ordinal),
            json.IndexOf("\"messages\":", StringComparison.Ordinal),
            json.IndexOf("\"ports\":", StringComparison.Ordinal),
            json.IndexOf("\"registeredPolicies\":", StringComparison.Ordinal),
            json.IndexOf("\"schemaVersion\":", StringComparison.Ordinal),
            json.IndexOf("\"sections\":", StringComparison.Ordinal),
        ];

        positions[0].Should().BeGreaterThanOrEqualTo(0);
        positions.Should().BeInAscendingOrder();

        using var document = JsonDocument.Parse(json);

        document.RootElement.GetProperty("messages").EnumerateArray()
            .Select(entry => entry.GetProperty("message").GetString()!)
            .Should().Equal(
                typeof(OpenVaultCommand).FullName!,
                typeof(RotateVaultKeyCommand).FullName!,
                typeof(BrowseScheduleCommand).FullName!,
                typeof(DraftScheduleCommand).FullName!,
                typeof(PublishScheduleCommand).FullName!,
                typeof(TouchScheduleCommand).FullName!);

        var portNames = document.RootElement.GetProperty("ports").EnumerateArray()
            .Select(entry => entry.GetProperty("port").GetString()!)
            .ToList();

        portNames.Should().Equal(portNames
            .OrderBy(name => name[(name.LastIndexOf('.') + 1)..], StringComparer.Ordinal)
            .ThenBy(name => name, StringComparer.Ordinal));
    }

    [Fact]
    public void ToJson_is_byte_identical_across_builds()
    {
        CaptureModel().ToJson().Should().Be(CaptureModel().ToJson());
    }

    [Fact]
    public void ToJson_carries_declarations_policies_contracts_ports_sections()
    {
        using var document = JsonDocument.Parse(CaptureModel().ToJson());

        var root = document.RootElement;

        root.GetProperty("declaredPolicies").EnumerateArray()
            .Select(entry => entry.GetString()!).Should().Equal("vaults.open", "vaults.rotate");
        root.GetProperty("registeredPolicies").EnumerateArray()
            .Select(entry => entry.GetString()!).Should().Equal("vaults.open", "vaults.rotate");

        var contracts = root.GetProperty("contracts").EnumerateArray().ToList();
        contracts.Should().HaveCount(2);
        contracts[0].GetProperty("name").GetString().Should().Be("schedules.publish");
        contracts[0].GetProperty("version").GetInt32().Should().Be(2);
        contracts[0].GetProperty("messageType").GetString().Should().Be(typeof(PublishScheduleCommand).FullName!);
        contracts[0].GetProperty("assembly").GetString().Should()
            .Be(typeof(PublishScheduleCommand).Assembly.GetName().Name!);

        var publish = FindMessage(root, typeof(PublishScheduleCommand));
        var permission = publish.GetProperty("declarations").EnumerateArray()
            .Single(entry => entry.GetProperty("declarationType").GetString()
                == typeof(RequiredPermission).FullName!);

        permission.GetProperty("source").GetString().Should().Be("Definition");
        permission.GetProperty("value").GetString().Should().Contain("schedules.publish");
        permission.GetProperty("declaredFor").GetString().Should().Be(typeof(PublishScheduleCommand).FullName!);
        permission.GetProperty("sourceType").GetString().Should()
            .Be(typeof(PublishScheduleCommandDefinition).FullName!);

        var touch = FindMessage(root, typeof(TouchScheduleCommand));
        touch.GetProperty("declarations").EnumerateArray()
            .Single(entry => entry.GetProperty("declarationType").GetString()
                == typeof(RequiredPermission).FullName!)
            .GetProperty("source").GetString().Should().Be("Attribute");

        var draft = FindMessage(root, typeof(DraftScheduleCommand));
        draft.GetProperty("declarations").EnumerateArray().Should().BeEmpty();

        var browse = FindMessage(root, typeof(BrowseScheduleCommand));
        var exemption = browse.GetProperty("declarations").EnumerateArray()
            .Single(entry => entry.GetProperty("declarationType").GetString()
                == typeof(RequiredPermission).FullName!);

        exemption.GetProperty("source").GetString().Should().Be("Exemption");
        exemption.GetProperty("rationale").GetString().Should().Be("the schedule list is public");

        var serializer = root.GetProperty("ports").EnumerateArray()
            .Single(entry => entry.GetProperty("port").GetString() == typeof(IMessageSerializer).FullName!);

        serializer.GetProperty("implementation").GetString().Should()
            .Be(typeof(SystemTextJsonMessageSerializer).FullName!);
        serializer.GetProperty("isDefault").GetBoolean().Should().BeTrue();

        root.GetProperty("sections").GetProperty("test").GetProperty("jobs").GetInt32().Should().Be(2);
    }

    /// <summary>
    ///     Finds one message object in the snapshot document.
    /// </summary>
    /// <param name="root">The snapshot document root.</param>
    /// <param name="messageType">The message type to find.</param>
    /// <returns>The message object.</returns>
    private static JsonElement FindMessage(JsonElement root, Type messageType)
    {
        return root.GetProperty("messages").EnumerateArray()
            .Single(entry => entry.GetProperty("message").GetString() == messageType.FullName!);
    }

    /// <summary>
    ///     Captures the composition model for a composition exercising every snapshot section.
    /// </summary>
    /// <returns>The captured model.</returns>
    private static ICompositionModel CaptureModel()
    {
        ICompositionModel? captured = null;
        var contributor = new TestSectionContributor();

        new ServiceCollection()
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(messaging =>
                {
                    messaging.UseTenancy();
                    messaging.Contracts.Register<PublishScheduleCommand>("schedules.publish", 2);
                    messaging.Contracts.Register<TouchScheduleCommand>("schedules.touch");
                    messaging.AddAuthorizationPolicies(policies => policies
                        .Add("vaults.open", MessagePolicy.Permission("vaults.open"))
                        .Add("vaults.rotate", MessagePolicy.Permission("vaults.rotate")));
                    messaging.AddBuildStep(configuration => configuration.DependencyRegistry
                        .RegisterCollection(DependencyDescriptor.ForCollection(
                            typeof(ICompositionSectionContributor),
                            contributor)));
                    messaging.ValidateComposition(model => captured = model);
                });
                registry.AddCommands(commands =>
                {
                    commands.Register<PublishScheduleCommand>();
                    commands.Register<PublishScheduleCommandHandler>();
                    commands.Register<PublishScheduleCommandDefinition>();
                    commands.Register<TouchScheduleCommand>();
                    commands.Register<TouchScheduleCommandHandler>();
                    commands.Register<DraftScheduleCommand>();
                    commands.Register<DraftScheduleCommandHandler>();
                    commands.Register<BrowseScheduleCommand>();
                    commands.Register<BrowseScheduleCommandHandler>();
                    commands.Register<OpenVaultCommand>();
                    commands.Register<OpenVaultCommandHandler>();
                    commands.Register<RotateVaultKeyCommand>();
                    commands.Register<RotateVaultKeyCommandHandler>();
                    commands.Register<RotateVaultKeyCommandDefinition>();
                });
            })
            .BuildServiceProvider();

        captured.Should().NotBeNull();
        return captured!;
    }
}
