using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.Authorization;
using LiteBus.Messaging.Registry;
using LiteBus.Runtime.Abstractions.Exceptions;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Mediator.UnitTests.Refusals;

/// <summary>
///     Verifies that per-message refusal declarations are harvested into the composed refusal catalog, that a
///     declaration and a builder registration for one code follow the one conflict rule, and that fault codes
///     cannot be registered.
/// </summary>
[Collection("Sequential")]
public sealed class RefusalDeclarationHarvestTests : LiteBusTestBase
{
    [Fact]
    public void HarvestedDefinitionRefusals_ResolveFromComposedCatalog()
    {
        using var provider = Build(
            _ => { },
            commands =>
            {
                commands.Register<HarvestBillCommand>();
                commands.Register<HarvestBillCommandHandler>();
                commands.Register<HarvestBillCommandDefinition>();
            });

        var catalog = provider.GetRequiredService<IRefusalCatalog>();

        catalog.TryResolve("billing.unpaid", out var unpaid).Should().BeTrue();
        unpaid!.Kind.Should().Be(RefusalKind.Conflict);
        unpaid.Title.Should().Be("Billing conflict");
        unpaid.Retryable.Should().BeTrue();
        unpaid.Owner.Should().Be(nameof(HarvestBillCommandDefinition));

        catalog.TryResolve("billing.closed", out var closed).Should().BeTrue();
        closed!.Kind.Should().Be(RefusalKind.Denied);
        closed.Title.Should().Be("Billing closed");
        closed.Retryable.Should().BeFalse();
    }

    [Fact]
    public void HarvestedRefusals_AppearInRefusalsSection()
    {
        var model = Capture(
            _ => { },
            commands =>
            {
                commands.Register<HarvestBillCommand>();
                commands.Register<HarvestBillCommandHandler>();
                commands.Register<HarvestBillCommandDefinition>();
            });

        var section = model.Sections.Single(contributor => contributor.SectionName == "refusals");
        var data = section.GetData().Should().BeOfType<SortedDictionary<string, object?>>().Subject;
        var entries = data["entries"].Should().BeAssignableTo<IReadOnlyList<SortedDictionary<string, object?>>>().Subject;

        var billing = entries.Single(entry => (string) entry["pattern"]! == "billing.unpaid");
        billing["kind"].Should().Be("Conflict");
        billing["title"].Should().Be("Billing conflict");
        billing["retryable"].Should().Be(true);
        billing["owner"].Should().Be(nameof(HarvestBillCommandDefinition));
    }

    [Fact]
    public void DeclaredDefaultRefusals_AreHarvested()
    {
        using var provider = Build(
            messaging => messaging.DeclareDefault<HarvestDefaultCommand, RefusalDeclarations>(
                new RefusalDeclarations(
                [
                    new RefusalDeclaration("billing.unpaid", RefusalKind.Conflict, "Billing conflict", Retryable: true),
                ])),
            commands =>
            {
                commands.Register<HarvestDefaultCommand>();
                commands.Register<HarvestDefaultCommandHandler>();
            });

        var catalog = provider.GetRequiredService<IRefusalCatalog>();

        catalog.TryResolve("billing.unpaid", out var entry).Should().BeTrue();
        entry!.Title.Should().Be("Billing conflict");
        entry.Owner.Should().Be(nameof(HarvestDefaultCommand));
    }

    [Fact]
    public void CatalogSourceRefusals_AreHarvested()
    {
        using var provider = Build(
            messaging => messaging.Register<HarvestRefusalSource>(),
            commands =>
            {
                commands.Register<HarvestBillCommand>();
                commands.Register<HarvestBillCommandHandler>();
            });

        var catalog = provider.GetRequiredService<IRefusalCatalog>();

        catalog.TryResolve("billing.unpaid", out var entry).Should().BeTrue();
        entry!.Kind.Should().Be(RefusalKind.Conflict);
        entry.Title.Should().Be("Billing conflict");
        entry.Retryable.Should().BeTrue();
    }

    [Fact]
    public void ConflictingDeclarationAndRegistration_FailsCompositionNamingBoth()
    {
        var act = () => Build(
            messaging => messaging.Refusals.Map(
                "billing.unpaid",
                RefusalKind.Denied,
                "Billing denied",
                retryable: false,
                owner: "billing"),
            commands =>
            {
                commands.Register<HarvestBillCommand>();
                commands.Register<HarvestBillCommandHandler>();
                commands.Register<HarvestBillCommandDefinition>();
            });

        act.Should().Throw<LiteBusConfigurationException>()
            .WithMessage("*billing.unpaid*billing*HarvestBillCommandDefinition*Replace*");
    }

    [Fact]
    public void IdenticalDeclarationAndRegistration_ComposeKeepingFirstOwner()
    {
        using var provider = Build(
            messaging => messaging.Refusals.Map(
                "billing.unpaid",
                RefusalKind.Conflict,
                "Billing conflict",
                retryable: true,
                owner: "billing"),
            commands =>
            {
                commands.Register<HarvestBillCommand>();
                commands.Register<HarvestBillCommandHandler>();
                commands.Register<HarvestBillCommandDefinition>();
            });

        var catalog = provider.GetRequiredService<IRefusalCatalog>();

        catalog.TryResolve("billing.unpaid", out var entry).Should().BeTrue();
        entry!.Owner.Should().Be("billing");
    }

    [Fact]
    public void Map_FaultCode_Throws()
    {
        var catalog = RefusalCatalog.CreateDefault();

        var act = () => catalog.Map(
            AuthorizationPolicyRefusalCodes.UnknownPolicy,
            RefusalKind.Denied,
            "Unknown policy",
            retryable: false);

        act.Should().Throw<LiteBusConfigurationException>()
            .WithMessage("*authorization.unknown-policy*MessageFaultException*");
    }

    [Fact]
    public void Replace_FaultCode_Throws()
    {
        var catalog = RefusalCatalog.CreateDefault();

        var act = () => catalog.Replace(
            DomainEventFaultCodes.TranslatorMissing,
            RefusalKind.Unavailable,
            "Translator missing",
            retryable: false);

        act.Should().Throw<LiteBusConfigurationException>()
            .WithMessage("*domain-events.translator-missing*MessageFaultException*");
    }

    [Fact]
    public void RefusalDeclarations_ConflictingDuplicates_Throw()
    {
        var act = () => new RefusalDeclarations(
        [
            new RefusalDeclaration("billing.unpaid", RefusalKind.Conflict, "Billing conflict", Retryable: true),
            new RefusalDeclaration("billing.unpaid", RefusalKind.Denied, "Billing denied"),
        ]);

        act.Should().Throw<ArgumentException>().WithMessage("*billing.unpaid*");
    }

    [Fact]
    public void RefusalDeclarations_IdenticalDuplicates_KeepFirst()
    {
        var declarations = new RefusalDeclarations(
        [
            new RefusalDeclaration("billing.unpaid", RefusalKind.Conflict, "Billing conflict", Retryable: true),
            new RefusalDeclaration("billing.unpaid", RefusalKind.Conflict, "Billing conflict", Retryable: true),
        ]);

        declarations.Values.Should().ContainSingle();
    }

    /// <summary>
    ///     Builds a provider over the given messaging and command configuration.
    /// </summary>
    /// <param name="messaging">The messaging configuration under test.</param>
    /// <param name="commands">The command registrations under test.</param>
    /// <returns>The configured service provider.</returns>
    private static ServiceProvider Build(Action<MessageModuleBuilder> messaging, Action<CommandModuleBuilder> commands)
    {
        return new ServiceCollection()
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(messaging);
                registry.AddCommands(commands);
            })
            .BuildServiceProvider();
    }

    /// <summary>
    ///     Captures the composition model handed to a composition check.
    /// </summary>
    /// <param name="messaging">The messaging configuration under test.</param>
    /// <param name="commands">The command registrations under test.</param>
    /// <returns>The captured model.</returns>
    private static ICompositionModel Capture(Action<MessageModuleBuilder> messaging, Action<CommandModuleBuilder> commands)
    {
        ICompositionModel? captured = null;

        Build(
            builder =>
            {
                messaging(builder);
                builder.ValidateComposition(model => captured = model);
            },
            commands);

        captured.Should().NotBeNull();
        return captured!;
    }
}

/// <summary>
///     A command declaring the refusal codes it can produce through its definition.
/// </summary>
internal sealed class HarvestBillCommand : ICommand;

/// <summary>
///     Handles <see cref="HarvestBillCommand" />.
/// </summary>
internal sealed class HarvestBillCommandHandler : ICommandHandler<HarvestBillCommand>
{
    /// <inheritdoc />
    public Task HandleAsync(HarvestBillCommand message, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}

/// <summary>
///     Declares the refusal codes of <see cref="HarvestBillCommand" />.
/// </summary>
internal sealed class HarvestBillCommandDefinition : IMessageDefinition<HarvestBillCommand>
{
    /// <inheritdoc />
    public void Describe(IMessageDeclarations declarations)
    {
        declarations.Refusals(
            new RefusalDeclaration("billing.unpaid", RefusalKind.Conflict, "Billing conflict", Retryable: true),
            new RefusalDeclaration("billing.closed", RefusalKind.Denied, "Billing closed"));
    }
}

/// <summary>
///     A command declaring no refusals of its own, so a family default applies.
/// </summary>
internal sealed class HarvestDefaultCommand : ICommand;

/// <summary>
///     Handles <see cref="HarvestDefaultCommand" />.
/// </summary>
internal sealed class HarvestDefaultCommandHandler : ICommandHandler<HarvestDefaultCommand>
{
    /// <inheritdoc />
    public Task HandleAsync(HarvestDefaultCommand message, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}

/// <summary>
///     Declares the refusal codes of <see cref="HarvestBillCommand" /> from a multi-message source.
/// </summary>
internal sealed class HarvestRefusalSource : IMultiMessageDefinition
{
    /// <inheritdoc />
    public void Describe(IMessageDeclarationCatalog catalog)
    {
        catalog.Describe<HarvestBillCommand>(declarations => declarations.Refusals(
            new RefusalDeclaration("billing.unpaid", RefusalKind.Conflict, "Billing conflict", Retryable: true)));
    }
}
