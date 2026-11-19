using LiteBus.Auditing;
using LiteBus.Auditing.Storage.Marten;
using LiteBus.Runtime.Abstractions.Exceptions;

namespace LiteBus.Storage.IntegrationTests.Marten;

/// <summary>
///     Tests for the Marten audit store registration: the recorded trail, chain store, capabilities, and defaults.
/// </summary>
public sealed class MartenAuditStoreBuilderTests
{
    [Fact]
    public void UseMartenStore_registers_the_marten_trail_chain_store_and_capabilities()
    {
        var builder = new AuditingModuleBuilder().UseMartenStore();
        var options = builder.Options;

        options.TrailType.Should().Be(typeof(MartenAuditTrail<>));
        options.ChainStoreType.Should().Be(typeof(MartenAuditChainStore<>));
        options.StoreCapabilities.Should().NotBeNull();
        options.StoreCapabilities!.JoinsBusinessTransaction.Should().BeTrue();
    }

    [Fact]
    public void UseMartenStore_records_the_marten_document_mapper_and_segmenter_defaults()
    {
        var options = new AuditingModuleBuilder().UseMartenStore().Options;

        options.StoreDefaultDocumentType.Should().Be<MartenAuditRecord>();
        options.StoreDefaultMapperType.Should().Be<MartenAuditRecordMapper>();
        options.StoreDefaultSegmenterType.Should().Be<MartenAuditRecordSegmenter>();
    }

    [Fact]
    public void UseMartenStore_twice_is_a_noop()
    {
        var builder = new AuditingModuleBuilder().UseMartenStore();

        var repeat = () => builder.UseMartenStore();

        repeat.Should().NotThrow();
        builder.Options.TrailType.Should().Be(typeof(MartenAuditTrail<>));
        builder.Options.ChainStoreType.Should().Be(typeof(MartenAuditChainStore<>));
    }

    [Fact]
    public void UseMartenStore_after_another_store_throws_and_names_both()
    {
        var builder = new AuditingModuleBuilder();
        builder.Options.TrailType = typeof(object);
        builder.Options.ChainStoreType = typeof(string);

        var use = () => builder.UseMartenStore();

        var thrown = use.Should().Throw<LiteBusConfigurationException>();
        thrown.WithMessage("*System.Object*System.String*Marten*");
    }

    [Fact]
    public void UseMartenStore_rejects_a_null_builder()
    {
        var use = () => ((AuditingModuleBuilder) null!).UseMartenStore();

        use.Should().Throw<ArgumentNullException>();
    }
}
