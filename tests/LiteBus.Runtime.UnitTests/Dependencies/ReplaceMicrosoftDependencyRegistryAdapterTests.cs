using LiteBus.Runtime.Abstractions;
using LiteBus.Runtime.Abstractions.Exceptions;
using LiteBus.Runtime.Extensions.Microsoft.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Runtime.UnitTests;

public sealed class ReplaceMicrosoftDependencyRegistryAdapterTests
{
    [Fact]
    public void Replace_AfterRegister_ShouldResolveReplacement()
    {
        var services = new ServiceCollection();
        var adapter = new MicrosoftDependencyRegistryAdapter(services);

        adapter.Register(new DependencyDescriptor(typeof(ITestService), typeof(TestServiceA)));
        adapter.Replace(new DependencyDescriptor(typeof(ITestService), typeof(TestServiceB)));

        adapter.Count.Should().Be(1);
        services.Count(service => service.ServiceType == typeof(ITestService)).Should().Be(1);
        services.Should().ContainSingle(service => service.ServiceType == typeof(ITestService))
            .Which.ImplementationType.Should().Be<TestServiceB>();

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<ITestService>().Should().BeOfType<TestServiceB>();
    }

    [Fact]
    public void Register_WithConflictingBinding_ShouldThrowLiteBusConfigurationException()
    {
        var services = new ServiceCollection();
        var adapter = new MicrosoftDependencyRegistryAdapter(services);

        adapter.Register(new DependencyDescriptor(typeof(ITestService), typeof(TestServiceA)));

        var act = () => adapter.Register(new DependencyDescriptor(typeof(ITestService), typeof(TestServiceB)));

        act.Should().Throw<LiteBusConfigurationException>()
            .WithMessage("*ITestService*");
    }

    [Fact]
    public void Register_AfterReplaceWithConflictingBinding_ShouldThrowLiteBusConfigurationException()
    {
        var services = new ServiceCollection();
        var adapter = new MicrosoftDependencyRegistryAdapter(services);

        adapter.Register(new DependencyDescriptor(typeof(ITestService), typeof(TestServiceA)));
        adapter.Replace(new DependencyDescriptor(typeof(ITestService), typeof(TestServiceB)));

        var act = () => adapter.Register(new DependencyDescriptor(typeof(ITestService), typeof(TestServiceA)));

        act.Should().Throw<LiteBusConfigurationException>()
            .WithMessage("*ITestService*");
    }
}
