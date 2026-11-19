using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Mediator.UnitTests.Authorization;
using LiteBus.Mediator.UnitTests.Metadata;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.Auditing;
using LiteBus.Messaging.Abstractions.Authorization;
using LiteBus.Messaging.Abstractions.Tenancy;
using LiteBus.Messaging.Audit;
using LiteBus.Messaging.Authorization;
using LiteBus.Messaging.Tenancy;
using LiteBus.Runtime.Abstractions;
using LiteBus.Runtime.Abstractions.Exceptions;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Mediator.UnitTests.Registry.Composition;

/// <summary>
///     Verifies that a composition check receives everything the host composed: the message catalog, the
///     declaration provenance per message, the declared and registered policy names, the contracts, the resolved
///     port implementations, and the contributed feature sections.
/// </summary>
[Collection("Sequential")]
public sealed class CompositionModelTests : LiteBusTestBase
{
    [Fact]
    public void ValidateComposition_receives_model_with_message_catalog()
    {
        var model = Capture(
            _ => { },
            commands =>
            {
                commands.Register<PublishScheduleCommand>();
                commands.Register<PublishScheduleCommandHandler>();
                commands.Register<PublishScheduleCommandDefinition>();
                commands.Register<TouchScheduleCommand>();
                commands.Register<TouchScheduleCommandHandler>();
            });

        model.Messages.Count.Should().Be(2);
        model.Messages.Select(entry => entry.MessageType).Should()
            .Contain(typeof(PublishScheduleCommand)).And.Contain(typeof(TouchScheduleCommand));
    }

    [Fact]
    public void DeclarationsFor_reports_definition_source()
    {
        var model = Capture(
            _ => { },
            commands =>
            {
                commands.Register<PublishScheduleCommand>();
                commands.Register<PublishScheduleCommandHandler>();
                commands.Register<PublishScheduleCommandDefinition>();
            });

        var record = model.DeclarationsFor(typeof(PublishScheduleCommand))
            .Single(candidate => candidate.DeclarationType == typeof(RequiredPermission));

        record.Source.Should().Be(DeclarationSourceKind.Definition);
        record.DeclaredFor.Should().Be<PublishScheduleCommand>();
        record.SourceType.Should().Be<PublishScheduleCommandDefinition>();
        record.Value.Should().Be(new RequiredPermission("schedules.publish"));
    }

    [Fact]
    public void DeclarationsFor_reports_attribute_source()
    {
        var model = Capture(
            _ => { },
            commands =>
            {
                commands.Register<TouchScheduleCommand>();
                commands.Register<TouchScheduleCommandHandler>();
            });

        var record = model.DeclarationsFor(typeof(TouchScheduleCommand))
            .Single(candidate => candidate.DeclarationType == typeof(RequiredPermission));

        record.Source.Should().Be(DeclarationSourceKind.Attribute);
        record.DeclaredFor.Should().Be<TouchScheduleCommand>();
        record.SourceType.Should().Be<RequiresPermissionAttribute>();
        record.Value.Should().Be(new RequiredPermission("schedules.touch"));
    }

    [Fact]
    public void DeclarationsFor_reports_default_source()
    {
        var model = Capture(
            messaging => messaging.DeclareDefault<ICommand, RequiredPermission>(
                new RequiredPermission("family.default")),
            commands =>
            {
                commands.Register<TouchScheduleCommand>();
                commands.Register<TouchScheduleCommandHandler>();
                commands.Register<DraftScheduleCommand>();
                commands.Register<DraftScheduleCommandHandler>();
            });

        var inherited = model.DeclarationsFor(typeof(DraftScheduleCommand))
            .Single(record => record.DeclarationType == typeof(RequiredPermission));

        inherited.Source.Should().Be(DeclarationSourceKind.Default);
        inherited.DeclaredFor.Should().Be<ICommand>();
        inherited.SourceType.Should().BeNull();
        inherited.Value.Should().Be(new RequiredPermission("family.default"));

        // The default covers the family; a message stating its own position keeps the attribute source.
        var own = model.DeclarationsFor(typeof(TouchScheduleCommand))
            .Single(record => record.DeclarationType == typeof(RequiredPermission));

        own.Source.Should().Be(DeclarationSourceKind.Attribute);
        own.Value.Should().Be(new RequiredPermission("schedules.touch"));
    }

    [Fact]
    public void DeclarationsFor_reports_convention_source()
    {
        var model = Capture(
            messaging => messaging.DeclareConvention<RequiredPermission>(
                messageType => new RequiredPermission("convention." + messageType.Name)),
            commands =>
            {
                commands.Register<DraftScheduleCommand>();
                commands.Register<DraftScheduleCommandHandler>();
            });

        var record = model.DeclarationsFor(typeof(DraftScheduleCommand))
            .Single(candidate => candidate.DeclarationType == typeof(RequiredPermission));

        record.Source.Should().Be(DeclarationSourceKind.Convention);
        record.DeclaredFor.Should().Be<DraftScheduleCommand>();
        record.SourceType.Should().BeNull();
        record.Value.Should().Be(new RequiredPermission("convention.DraftScheduleCommand"));
    }

    [Fact]
    public void DeclarationsFor_expands_exemptions()
    {
        var model = Capture(
            _ => { },
            commands =>
            {
                commands.Register<BrowseScheduleCommand>();
                commands.Register<BrowseScheduleCommandHandler>();
                commands.Register<ArchiveScheduleCommand>();
                commands.Register<ArchiveScheduleCommandHandler>();
            });

        var exemption = model.DeclarationsFor(typeof(BrowseScheduleCommand))
            .Single(record => record.DeclarationType == typeof(RequiredPermission));

        exemption.Source.Should().Be(DeclarationSourceKind.Exemption);
        exemption.DeclaredFor.Should().Be<BrowseScheduleCommand>();
        exemption.SourceType.Should().Be<DeclarationExemptAttribute>();
        exemption.Rationale.Should().Be("the schedule list is public");
        exemption.Value.Should().Be(new DeclarationExemption(typeof(RequiredPermission), "the schedule list is public"));

        // The exemption set is the envelope, so it is expanded rather than reported itself.
        model.DeclarationsFor(typeof(BrowseScheduleCommand)).Should()
            .NotContain(record => record.DeclarationType == typeof(DeclarationExemptions));

        // Two exemptions on one message aggregate into one set and expand back into two records.
        model.DeclarationsFor(typeof(ArchiveScheduleCommand))
            .Where(record => record.Source == DeclarationSourceKind.Exemption)
            .Select(record => record.DeclarationType)
            .Should().BeEquivalentTo([typeof(RequiredPermission), typeof(RetentionClass)]);
    }

    [Fact]
    public void DeclarationsFor_returns_empty_for_unknown_message()
    {
        var model = Capture(
            _ => { },
            commands =>
            {
                commands.Register<DraftScheduleCommand>();
                commands.Register<DraftScheduleCommandHandler>();
            });

        model.DeclarationsFor(typeof(string)).Should().BeEmpty();
    }

    [Fact]
    public void DeclarationsFor_rejects_null_message_type()
    {
        var model = Capture(
            _ => { },
            commands =>
            {
                commands.Register<DraftScheduleCommand>();
                commands.Register<DraftScheduleCommandHandler>();
            });

        var act = () => model.DeclarationsFor(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Declared_and_registered_policy_names_are_visible()
    {
        var model = Capture(
            messaging => messaging.AddAuthorizationPolicies(policies => policies
                .Add("vaults.open", MessagePolicy.Permission("vaults.open"))),
            commands =>
            {
                commands.Register<OpenVaultCommand>();
                commands.Register<OpenVaultCommandHandler>();
            });

        model.DeclaredPolicyNames.Should().ContainSingle("vaults.open");
        model.RegisteredPolicyNames.Should().ContainSingle("vaults.open");
    }

    [Fact]
    public void Declared_names_without_registered_names_are_visible()
    {
        var model = Capture(
            _ => { },
            commands =>
            {
                commands.Register<OpenVaultCommand>();
                commands.Register<OpenVaultCommandHandler>();
            });

        model.DeclaredPolicyNames.Should().ContainSingle("vaults.open");
        model.RegisteredPolicyNames.Should().BeEmpty();
    }

    [Fact]
    public void Unknown_policy_names_fail_composition_through_the_model()
    {
        var act = () => Build(
            messaging => messaging.AddAuthorizationPolicies(policies => policies
                .Add("vaults.open", MessagePolicy.Permission("vaults.open"))),
            commands =>
            {
                commands.Register<RotateVaultKeyCommand>();
                commands.Register<RotateVaultKeyCommandHandler>();
                commands.Register<RotateVaultKeyCommandDefinition>();
            });

        act.Should().Throw<LiteBusConfigurationException>()
            .WithMessage("*vaults.rotate*RotateVaultKeyCommand*");
    }

    [Fact]
    public void Contracts_list_registrations_with_owning_assemblies()
    {
        var model = Capture(
            messaging =>
            {
                messaging.Contracts.Register<PublishScheduleCommand>("schedules.publish", 2);
                messaging.Contracts.Register<TouchScheduleCommand>("schedules.touch");
            },
            commands =>
            {
                commands.Register<PublishScheduleCommand>();
                commands.Register<PublishScheduleCommandHandler>();
                commands.Register<PublishScheduleCommandDefinition>();
                commands.Register<TouchScheduleCommand>();
                commands.Register<TouchScheduleCommandHandler>();
            });

        var assemblyName = typeof(PublishScheduleCommand).Assembly.GetName().Name!;

        model.Contracts.Should().HaveCount(2);
        model.Contracts[0].Should().Be(new ContractRegistration(
            "schedules.publish",
            2,
            typeof(PublishScheduleCommand),
            assemblyName));
        model.Contracts[1].Should().Be(new ContractRegistration(
            "schedules.touch",
            1,
            typeof(TouchScheduleCommand),
            assemblyName));
    }

    [Fact]
    public void Ports_report_framework_defaults()
    {
        var model = Capture(
            messaging => messaging.UseTenancy(),
            commands =>
            {
                commands.Register<DraftScheduleCommand>();
                commands.Register<DraftScheduleCommandHandler>();
            });

        model.Ports.Should().ContainSingle(port => port.PortType == typeof(ITenantMetadataProvider))
            .Which.Should().Be(new CompositionPortEntry(
                typeof(ITenantMetadataProvider),
                typeof(DefaultTenantMetadataProvider),
                true));
        model.Ports.Should().ContainSingle(port => port.PortType == typeof(ITenantIdentifierValidator))
            .Which.Should().Be(new CompositionPortEntry(
                typeof(ITenantIdentifierValidator),
                typeof(DefaultTenantIdentifierValidator),
                true));
        model.Ports.Should().ContainSingle(port => port.PortType == typeof(IMessageSerializer))
            .Which.Should().Be(new CompositionPortEntry(
                typeof(IMessageSerializer),
                typeof(SystemTextJsonMessageSerializer),
                true));
        model.Ports.Should().ContainSingle(port => port.PortType == typeof(IAuditOutcomeMapper))
            .Which.Should().Be(new CompositionPortEntry(
                typeof(IAuditOutcomeMapper),
                typeof(DefaultAuditOutcomeMapper),
                true));
    }

    [Fact]
    public void Ports_report_application_replacements()
    {
        var model = Capture(
            messaging => messaging
                .UseTenancy(tenancy => tenancy.ValidateTenant(tenantId => tenantId.Length > 0))
                .UseSerializerInstance(new TestSerializer()),
            commands =>
            {
                commands.Register<DraftScheduleCommand>();
                commands.Register<DraftScheduleCommandHandler>();
            });

        model.Ports.Should().ContainSingle(port => port.PortType == typeof(ITenantIdentifierValidator))
            .Which.Should().Be(new CompositionPortEntry(
                typeof(ITenantIdentifierValidator),
                typeof(PredicateTenantIdentifierValidator),
                false));
        model.Ports.Should().ContainSingle(port => port.PortType == typeof(IMessageSerializer))
            .Which.Should().Be(new CompositionPortEntry(
                typeof(IMessageSerializer),
                typeof(TestSerializer),
                false));

        // The metadata provider keeps its default while the validator is replaced beside it.
        model.Ports.Should().ContainSingle(port => port.PortType == typeof(ITenantMetadataProvider))
            .Which.IsDefault.Should().BeTrue();
    }

    [Fact]
    public void Sections_collect_contributors()
    {
        var contributor = new TestSectionContributor();

        var model = Capture(
            messaging => messaging.AddBuildStep(configuration => configuration.DependencyRegistry
                .RegisterCollection(DependencyDescriptor.ForCollection(
                    typeof(ICompositionSectionContributor),
                    contributor))),
            commands =>
            {
                commands.Register<DraftScheduleCommand>();
                commands.Register<DraftScheduleCommandHandler>();
            });

        model.Sections.Should().ContainSingle().Which.Should().BeSameAs(contributor);
    }

    [Fact]
    public void Duplicate_section_name_fails_composition()
    {
        var act = () => Build(
            messaging => messaging.AddBuildStep(configuration =>
            {
                configuration.DependencyRegistry.RegisterCollection(DependencyDescriptor.ForCollection(
                    typeof(ICompositionSectionContributor),
                    new TestSectionContributor()));
                configuration.DependencyRegistry.RegisterCollection(DependencyDescriptor.ForCollection(
                    typeof(ICompositionSectionContributor),
                    new SecondTestSectionContributor()));
            }),
            commands =>
            {
                commands.Register<DraftScheduleCommand>();
                commands.Register<DraftScheduleCommandHandler>();
            });

        act.Should().Throw<LiteBusConfigurationException>()
            .WithMessage("*TestSectionContributor*SecondTestSectionContributor*test*");
    }

    [Fact]
    public void Same_contributor_twice_registers_once()
    {
        var contributor = new TestSectionContributor();

        var model = Capture(
            messaging => messaging.AddBuildStep(configuration =>
            {
                configuration.DependencyRegistry.RegisterCollection(DependencyDescriptor.ForCollection(
                    typeof(ICompositionSectionContributor),
                    contributor));
                configuration.DependencyRegistry.RegisterCollection(DependencyDescriptor.ForCollection(
                    typeof(ICompositionSectionContributor),
                    contributor));
            }),
            commands =>
            {
                commands.Register<DraftScheduleCommand>();
                commands.Register<DraftScheduleCommandHandler>();
            });

        model.Sections.Should().ContainSingle().Which.Should().BeSameAs(contributor);
    }

    [Fact]
    public void Resolved_model_records_container_replacement()
    {
        var services = new ServiceCollection();

        services.AddLiteBus(registry =>
        {
            registry.AddMessaging(messaging => messaging.UseTenancy());
            registry.AddCommands(commands =>
            {
                commands.Register<TouchScheduleCommand>();
                commands.Register<TouchScheduleCommandHandler>();
            });
        });

        // Registered with the container after LiteBus composed, so the descriptor list never sees it.
        services.AddSingleton<ITenantMetadataProvider, CustomMetadataProvider>();

        var provider = services.BuildServiceProvider();

        var model = provider.GetRequiredService<ICompositionModel>();

        model.Ports.Should().ContainSingle(port => port.PortType == typeof(ITenantMetadataProvider))
            .Which.Should().Be(new CompositionPortEntry(
                typeof(ITenantMetadataProvider),
                typeof(CustomMetadataProvider),
                false));
    }

    [Fact]
    public void Resolved_model_matches_checks_model_without_container_changes()
    {
        var contributor = new TestSectionContributor();
        ICompositionModel? fromCheck = null;

        var provider = Build(
            messaging =>
            {
                messaging.UseTenancy();
                messaging.Contracts.Register<TouchScheduleCommand>("schedules.touch");
                messaging.AddBuildStep(configuration => configuration.DependencyRegistry
                    .RegisterCollection(DependencyDescriptor.ForCollection(
                        typeof(ICompositionSectionContributor),
                        contributor)));
                messaging.ValidateComposition(model => fromCheck = model);
            },
            commands =>
            {
                commands.Register<TouchScheduleCommand>();
                commands.Register<TouchScheduleCommandHandler>();
                commands.Register<BrowseScheduleCommand>();
                commands.Register<BrowseScheduleCommandHandler>();
            });

        fromCheck.Should().NotBeNull();

        var resolved = provider.GetRequiredService<ICompositionModel>();

        resolved.ToJson().Should().Be(fromCheck!.ToJson());
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
