using Autofac;
using LiteBus.Messaging;
using LiteBus.Runtime.Abstractions.Hosting;
using LiteBus.Runtime.Extensions.Autofac;
using LiteBus.Testing;

namespace LiteBus.Extensions.Autofac.UnitTests;

/// <summary>
///     Verifies Autofac hosting registers the LiteBus host manifest.
/// </summary>
public sealed class AutofacHostManifestTests : LiteBusTestBase
{
    /// <summary>
    ///     Verifies AddLiteBus registers <see cref="LiteBusHostManifest" /> for Autofac hosts.
    /// </summary>
    [Fact]
    public void AddLiteBus_should_register_lite_bus_host_manifest()
    {
        var builder = new ContainerBuilder();

        builder.AddLiteBus(registry =>
        {
            registry.AddMessageModule(_ =>
            {
            });
        });

        using var container = builder.Build();

        container.IsRegistered<LiteBusHostManifest>().Should().BeTrue();
    }
}
