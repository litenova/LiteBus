using LiteBus.Auditing.Hashing;
using LiteBus.Auditing.Abstractions;
using LiteBus.Runtime.Abstractions.Exceptions;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Auditing.UnitTests.Hashing;

/// <summary>
///     Verifies that the audit hash scheme registry builds the shipped schemes and honors explicit registrations.
/// </summary>
public sealed class AuditHashSchemeRegistryTests
{
    [Fact]
    public void The_v2_scheme_frames_the_position_and_hashes_with_plain_SHA256()
    {
        using ServiceProvider provider = HashingTestServices.ProviderWith<BusinessOnlyCanonicalizer>();
        var registry = new AuditHashSchemeRegistry(provider);

        var scheme = registry.Resolve<HashTestDocument>(AuditHashSchemeNames.Sha256V2);

        scheme.Name.Should().Be(AuditHashSchemeNames.Sha256V2);
        scheme.Canonicalizer.Should().BeOfType<BusinessOnlyCanonicalizer>();
        scheme.LinkRule.Should().BeOfType<FramedLinkRule>();
        ((FramedLinkRule) scheme.LinkRule).SchemeName.Should().Be(AuditHashSchemeNames.Sha256V2);
        scheme.Hasher.Should().BeOfType<Sha256AuditHasher>();
    }

    [Fact]
    public void The_v1_scheme_passes_the_canonical_bytes_through()
    {
        using ServiceProvider provider = HashingTestServices.ProviderWith<PositionBindingCanonicalizer>();
        var registry = new AuditHashSchemeRegistry(provider);

        var scheme = registry.Resolve<HashTestDocument>(AuditHashSchemeNames.Sha256V1);

        scheme.Name.Should().Be(AuditHashSchemeNames.Sha256V1);
        scheme.Canonicalizer.Should().BeOfType<PositionBindingCanonicalizer>();
        scheme.LinkRule.Should().BeOfType<LiteBus.Auditing.Hashing.PreviousHashLinkRule>();
        scheme.Hasher.Should().BeOfType<Sha256AuditHasher>();
    }

    [Fact]
    public void Resolving_twice_returns_the_same_scheme()
    {
        using ServiceProvider provider = HashingTestServices.ProviderWith<BusinessOnlyCanonicalizer>();
        var registry = new AuditHashSchemeRegistry(provider);

        var first = registry.Resolve<HashTestDocument>(AuditHashSchemeNames.Sha256V2);
        var second = registry.Resolve<HashTestDocument>(AuditHashSchemeNames.Sha256V2);

        second.Should().BeSameAs(first);
    }

    [Fact]
    public void An_unknown_scheme_is_rejected_and_names_the_scheme()
    {
        using ServiceProvider provider = HashingTestServices.ProviderWith<BusinessOnlyCanonicalizer>();
        var registry = new AuditHashSchemeRegistry(provider);

        var act = () => registry.Resolve<HashTestDocument>("sha256-v9");

        act.Should().Throw<LiteBusConfigurationException>().WithMessage("*sha256-v9*");
    }

    [Fact]
    public void A_blank_scheme_name_is_rejected()
    {
        using ServiceProvider provider = HashingTestServices.ProviderWith<BusinessOnlyCanonicalizer>();
        var registry = new AuditHashSchemeRegistry(provider);

        var act = () => registry.Resolve<HashTestDocument>("  ");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void A_missing_canonicalizer_is_rejected()
    {
        using ServiceProvider provider = new ServiceCollection().BuildServiceProvider();
        var registry = new AuditHashSchemeRegistry(provider);

        var act = () => registry.Resolve<HashTestDocument>(AuditHashSchemeNames.Sha256V2);

        act.Should().Throw<LiteBusConfigurationException>().WithMessage("*IAuditCanonicalizer*");
    }

    [Fact]
    public void A_null_service_provider_is_rejected()
    {
        var act = () => new AuditHashSchemeRegistry(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void An_explicit_scheme_resolves_by_name()
    {
        using ServiceProvider provider = HashingTestServices.ProviderWith<BusinessOnlyCanonicalizer>();
        var registry = new AuditHashSchemeRegistry(provider);
        var scheme = CustomScheme(new BusinessOnlyCanonicalizer());
        registry.Register(scheme, "the application");

        var resolved = registry.Resolve<HashTestDocument>(scheme.Name);

        resolved.Should().BeSameAs(scheme);
    }

    [Fact]
    public void Registering_the_same_scheme_twice_is_a_no_op()
    {
        using ServiceProvider provider = HashingTestServices.ProviderWith<BusinessOnlyCanonicalizer>();
        var registry = new AuditHashSchemeRegistry(provider);
        var scheme = CustomScheme(new BusinessOnlyCanonicalizer());
        registry.Register(scheme, "the application");

        var act = () => registry.Register(scheme, "the application");

        act.Should().NotThrow();
        registry.Resolve<HashTestDocument>(scheme.Name).Should().BeSameAs(scheme);
    }

    [Fact]
    public void A_conflicting_scheme_is_rejected_and_names_both_sources()
    {
        using ServiceProvider provider = HashingTestServices.ProviderWith<BusinessOnlyCanonicalizer>();
        var registry = new AuditHashSchemeRegistry(provider);
        var scheme = CustomScheme(new BusinessOnlyCanonicalizer());
        registry.Register(scheme, "the application");

        var act = () => registry.Register(CustomScheme(new PositionBindingCanonicalizer()), "the test module");

        act.Should().Throw<LiteBusConfigurationException>()
            .WithMessage("*the application*")
            .WithMessage("*the test module*");
    }

    [Fact]
    public void A_built_shipped_scheme_conflicts_with_an_explicit_registration()
    {
        using ServiceProvider provider = HashingTestServices.ProviderWith<BusinessOnlyCanonicalizer>();
        var registry = new AuditHashSchemeRegistry(provider);
        registry.Resolve<HashTestDocument>(AuditHashSchemeNames.Sha256V2);

        var replacement = new AuditHashScheme<HashTestDocument>(
            AuditHashSchemeNames.Sha256V2,
            new PositionBindingCanonicalizer(),
            new FramedLinkRule(AuditHashSchemeNames.Sha256V2),
            new Sha256AuditHasher());
        var act = () => registry.Register(replacement, "the application");

        act.Should().Throw<LiteBusConfigurationException>().WithMessage("*shipped*");
    }

    [Fact]
    public void Replace_overwrites_a_conflicting_scheme()
    {
        using ServiceProvider provider = HashingTestServices.ProviderWith<BusinessOnlyCanonicalizer>();
        var registry = new AuditHashSchemeRegistry(provider);
        registry.Register(CustomScheme(new BusinessOnlyCanonicalizer()), "the application");
        var replacement = CustomScheme(new PositionBindingCanonicalizer());

        registry.Replace(replacement, "the test module");

        registry.Resolve<HashTestDocument>(replacement.Name).Should().BeSameAs(replacement);
    }

    /// <summary>
    ///     Builds an explicit scheme over the given canonicalizer.
    /// </summary>
    /// <param name="canonicalizer">The canonical form.</param>
    /// <returns>The scheme.</returns>
    private static AuditHashScheme<HashTestDocument> CustomScheme(IAuditCanonicalizer<HashTestDocument> canonicalizer)
    {
        return new AuditHashScheme<HashTestDocument>(
            "custom-v1",
            canonicalizer,
            new FramedLinkRule("custom-v1"),
            new Sha256AuditHasher());
    }
}

