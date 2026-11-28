using LiteBus.Auditing;
using LiteBus.Auditing.Anchors.Timestamping;

namespace LiteBus.Auditing.UnitTests.Anchors;

/// <summary>
///     Verifies that the timestamp extension registers the RFC 3161 checkpoint signer.
/// </summary>
public sealed class AuditHashChainBuilderTimestampingExtensionsTests
{
    /// <summary>
    ///     Builds the time-stamping authority URI tests register with.
    /// </summary>
    /// <returns>The test authority URI.</returns>
    private static Uri Authority() => new("https://tsa.example.test/timestamp");

    [Fact]
    public void TimestampCheckpoints_registers_the_timestamp_signer()
    {
        var builder = new AuditingModuleBuilder();

        builder.UseHashChain(chain => chain.TimestampCheckpoints(Authority()));

        builder.Options.Chain!.SignerTypes
            .Should().ContainSingle().Which.Should().Be<Rfc3161CheckpointSigner>();
    }

    [Fact]
    public void TimestampCheckpoints_twice_registers_the_signer_once()
    {
        var builder = new AuditingModuleBuilder();

        builder.UseHashChain(chain => chain.TimestampCheckpoints(Authority()));
        builder.UseHashChain(chain => chain.TimestampCheckpoints(Authority()));

        builder.Options.Chain!.SignerTypes.Should().ContainSingle();
    }

    [Fact]
    public void Null_builder_is_rejected()
    {
        var act = () => AuditHashChainBuilderTimestampingExtensions.TimestampCheckpoints(null!, Authority());

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Null_authority_is_rejected()
    {
        var builder = new AuditingModuleBuilder();

        var act = () => builder.UseHashChain(chain => chain.TimestampCheckpoints(null!));

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Relative_authority_is_rejected()
    {
        var builder = new AuditingModuleBuilder();

        var act = () => builder.UseHashChain(
            chain => chain.TimestampCheckpoints(new Uri("/timestamp", UriKind.Relative)));

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Non_http_authority_is_rejected()
    {
        var builder = new AuditingModuleBuilder();

        var act = () => builder.UseHashChain(
            chain => chain.TimestampCheckpoints(new Uri("ftp://tsa.example.test/")));

        act.Should().Throw<ArgumentException>();
    }
}
