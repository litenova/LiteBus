using Autofac;
using LiteBus.Runtime.Abstractions;
using LiteBus.Runtime.Abstractions.Exceptions;
using LiteBus.Runtime.Extensions.Autofac;

namespace LiteBus.Runtime.UnitTests;

public sealed class ReplaceAutofacDependencyRegistryAdapterTests
{
    [Fact]
    public void Replace_AfterRegister_ShouldResolveReplacement()
    {
        var builder = new ContainerBuilder();
        var adapter = new AutofacDependencyRegistryAdapter(builder);

        adapter.Register(new DependencyDescriptor(typeof(ITestService), typeof(TestServiceA)));
        adapter.Replace(new DependencyDescriptor(typeof(ITestService), typeof(TestServiceB)));

        adapter.Count.Should().Be(1);

        using var container = builder.Build();
        container.Resolve<ITestService>().Should().BeOfType<TestServiceB>();
    }

    [Fact]
    public void Register_WithConflictingBinding_ShouldThrowLiteBusConfigurationException()
    {
        var builder = new ContainerBuilder();
        var adapter = new AutofacDependencyRegistryAdapter(builder);

        adapter.Register(new DependencyDescriptor(typeof(ITestService), typeof(TestServiceA)));

        var act = () => adapter.Register(new DependencyDescriptor(typeof(ITestService), typeof(TestServiceB)));

        act.Should().Throw<LiteBusConfigurationException>()
            .WithMessage("*ITestService*");
    }

    [Fact]
    public void Register_AfterReplaceWithConflictingBinding_ShouldThrowLiteBusConfigurationException()
    {
        var builder = new ContainerBuilder();
        var adapter = new AutofacDependencyRegistryAdapter(builder);

        adapter.Register(new DependencyDescriptor(typeof(ITestService), typeof(TestServiceA)));
        adapter.Replace(new DependencyDescriptor(typeof(ITestService), typeof(TestServiceB)));

        var act = () => adapter.Register(new DependencyDescriptor(typeof(ITestService), typeof(TestServiceA)));

        act.Should().Throw<LiteBusConfigurationException>()
            .WithMessage("*ITestService*");
    }
}
