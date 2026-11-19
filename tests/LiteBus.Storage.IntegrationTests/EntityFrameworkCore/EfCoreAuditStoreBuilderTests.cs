using LiteBus.Auditing;
using LiteBus.Auditing.Storage.EntityFrameworkCore;
using LiteBus.Runtime.Abstractions.Exceptions;

namespace LiteBus.Storage.IntegrationTests.EntityFrameworkCore;

/// <summary>
///     Tests for the Entity Framework Core audit store registration: the recorded trail, chain store,
///     capabilities, and defaults.
/// </summary>
public sealed class EfCoreAuditStoreBuilderTests
{
    [Fact]
    public void UseEntityFrameworkCoreStore_registers_the_trail_chain_store_and_capabilities()
    {
        var builder = new AuditingModuleBuilder().UseEntityFrameworkCoreStore<EfCoreAuditTestDbContext>();
        var options = builder.Options;

        options.TrailType.Should().Be(typeof(EfCoreAuditTrail<>));
        options.ChainStoreType.Should().Be(typeof(EfCoreAuditChainStore<>));
        options.StoreCapabilities.Should().NotBeNull();
        options.StoreCapabilities!.JoinsBusinessTransaction.Should().BeTrue();
    }

    [Fact]
    public void UseEntityFrameworkCoreStore_records_the_document_mapper_and_segmenter_defaults()
    {
        var options = new AuditingModuleBuilder().UseEntityFrameworkCoreStore<EfCoreAuditTestDbContext>().Options;

        options.StoreDefaultDocumentType.Should().Be<EfCoreAuditRecord>();
        options.StoreDefaultMapperType.Should().Be<EfCoreAuditRecordMapper>();
        options.StoreDefaultSegmenterType.Should().Be<EfCoreAuditRecordSegmenter>();
    }

    [Fact]
    public void UseEntityFrameworkCoreStore_twice_is_a_noop()
    {
        var builder = new AuditingModuleBuilder().UseEntityFrameworkCoreStore<EfCoreAuditTestDbContext>();

        var repeat = () => builder.UseEntityFrameworkCoreStore<EfCoreAuditTestDbContext>();

        repeat.Should().NotThrow();
        builder.Options.TrailType.Should().Be(typeof(EfCoreAuditTrail<>));
        builder.Options.ChainStoreType.Should().Be(typeof(EfCoreAuditChainStore<>));
    }

    [Fact]
    public void UseEntityFrameworkCoreStore_after_another_store_throws_and_names_both()
    {
        var builder = new AuditingModuleBuilder();
        builder.Options.TrailType = typeof(object);
        builder.Options.ChainStoreType = typeof(string);

        var use = () => builder.UseEntityFrameworkCoreStore<EfCoreAuditTestDbContext>();

        var thrown = use.Should().Throw<LiteBusConfigurationException>();
        thrown.WithMessage("*System.Object*System.String*Entity Framework Core*");
    }

    [Fact]
    public void UseEntityFrameworkCoreStore_rejects_a_null_builder()
    {
        var use = () => ((AuditingModuleBuilder) null!).UseEntityFrameworkCoreStore<EfCoreAuditTestDbContext>();

        use.Should().Throw<ArgumentNullException>();
    }
}
