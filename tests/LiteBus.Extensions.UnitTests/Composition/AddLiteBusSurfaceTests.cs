using LiteBus.Runtime.Abstractions;

namespace LiteBus.Extensions.UnitTests.Composition;

public sealed class AddLiteBusSurfaceTests
{
    [Theory]
    [InlineData(typeof(global::LiteBus.Extensions.Microsoft.DependencyInjection.ServiceCollectionExtensions))]
    [InlineData(typeof(global::LiteBus.Extensions.Autofac.ContainerBuilderExtensions))]
    public void HostAdapter_ShouldExposeOneBuilderBasedAddLiteBusOverload(Type extensionType)
    {
        var methods = extensionType.GetMethods()
            .Where(method => method is { IsPublic: true, IsStatic: true, Name: "AddLiteBus" })
            .ToArray();

        methods.Should().ContainSingle();
        methods[0].GetParameters().Should().HaveCount(2);
        methods[0].GetParameters()[1].ParameterType.Should().Be(typeof(Action<ILiteBusBuilder>));
    }
}
