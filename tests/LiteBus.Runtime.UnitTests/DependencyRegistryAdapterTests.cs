using Autofac;
using LiteBus.Extensions.Autofac;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Runtime.Abstractions;
using LiteBus.Runtime.Extensions.Autofac;
using LiteBus.Runtime.Extensions.Microsoft.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace LiteBus.Runtime.UnitTests;

public sealed class DependencyRegistryAdapterTests
{
    [Fact]
    public void MicrosoftAdapter_RegisterDuplicateTypeDescriptor_ShouldRegisterOnce()
    {
        var services = new ServiceCollection();
        var adapter = new MicrosoftDependencyRegistryAdapter(services);
        var descriptor = new DependencyDescriptor(typeof(ITestService), typeof(TestServiceA));

        adapter.Register(descriptor);
        adapter.Register(new DependencyDescriptor(typeof(ITestService), typeof(TestServiceA)));

        adapter.Count.Should().Be(1);
        services.Should().ContainSingle(service => service.ServiceType == typeof(ITestService));
    }

    [Fact]
    public void MicrosoftAdapter_RegisterDifferentInstancesForSameServiceType_ShouldRegisterBoth()
    {
        var services = new ServiceCollection();
        var adapter = new MicrosoftDependencyRegistryAdapter(services);

        adapter.Register(new DependencyDescriptor(typeof(ITestService), new TestServiceA()));
        adapter.Register(new DependencyDescriptor(typeof(ITestService), new TestServiceB()));

        adapter.Count.Should().Be(2);
        services.Count(service => service.ServiceType == typeof(ITestService)).Should().Be(2);
    }

    [Fact]
    public void MicrosoftAdapter_RegisterBackgroundWorkTwice_ShouldRegisterOnce()
    {
        var services = new ServiceCollection();
        var adapter = new MicrosoftDependencyRegistryAdapter(services);

        adapter.RegisterBackgroundWork(typeof(TestBackgroundWork));
        adapter.RegisterBackgroundWork(typeof(TestBackgroundWork));

        services.Count(service => service.ServiceType == typeof(IHostedService)).Should().Be(1);
    }

    [Fact]
    public void MicrosoftAdapter_RegisterBackgroundWorkWithDifferentTypes_ShouldRegisterBoth()
    {
        var services = new ServiceCollection();
        var adapter = new MicrosoftDependencyRegistryAdapter(services);

        adapter.RegisterBackgroundWork(typeof(TestBackgroundWork));
        adapter.RegisterBackgroundWork(typeof(OtherBackgroundWork));

        services.Count(service => service.ServiceType == typeof(IHostedService)).Should().Be(2);
    }

    [Fact]
    public void MicrosoftAdapter_RegisterBackgroundWorkWithNonWorkType_ShouldThrowArgumentException()
    {
        var adapter = new MicrosoftDependencyRegistryAdapter(new ServiceCollection());

        var act = () => adapter.RegisterBackgroundWork(typeof(object));

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void AutofacAdapter_RegisterDuplicateTypeDescriptor_ShouldRegisterOnce()
    {
        var builder = new ContainerBuilder();
        var adapter = new AutofacDependencyRegistryAdapter(builder);

        adapter.Register(new DependencyDescriptor(typeof(ITestService), typeof(TestServiceA)));
        adapter.Register(new DependencyDescriptor(typeof(ITestService), typeof(TestServiceA)));

        adapter.Count.Should().Be(1);
    }

    [Fact]
    public void AutofacAdapter_RegisterBackgroundWorkTwice_ShouldResolveSingleHostedService()
    {
        var builder = new ContainerBuilder();
        var adapter = new AutofacDependencyRegistryAdapter(builder);

        adapter.RegisterBackgroundWork(typeof(TestBackgroundWork));
        adapter.RegisterBackgroundWork(typeof(TestBackgroundWork));

        using var container = builder.Build();
        container.Resolve<IEnumerable<IHostedService>>().Should().ContainSingle();
    }
}

public sealed class AddLiteBusIntegrationTests
{
    [Fact]
    public void MicrosoftAddLiteBus_ShouldBuildModulesInDependencyOrder()
    {
        var buildOrder = new List<Type>();
        var services = new ServiceCollection();

        services.AddLiteBus(registry =>
        {
            registry.Register(new OrderRecordingDependentModule(buildOrder));
            registry.Register(new OrderRecordingFoundationModule(buildOrder));
        });

        buildOrder.Should().Equal(typeof(FoundationModule), typeof(DependentModule));
    }

    [Fact]
    public void MicrosoftAddLiteBus_WithRequiredModule_ShouldInitializeWithoutError()
    {
        var services = new ServiceCollection();

        var act = () => services.AddLiteBus(registry =>
        {
            registry.Register(new DependentModule());
            registry.Register(new FoundationModule());
        });

        act.Should().NotThrow();
    }

    [Fact]
    public void MicrosoftAddLiteBus_ShouldResolveRegisteredSingletonInstance()
    {
        var services = new ServiceCollection();
        var instance = new TestServiceA();

        services.AddLiteBus(registry =>
        {
            registry.Register(new RegistrationModule(configuration =>
            {
                configuration.DependencyRegistry.Register(new DependencyDescriptor(typeof(ITestService), instance));
            }));
        });

        var provider = services.BuildServiceProvider();
        provider.GetRequiredService<ITestService>().Should().BeSameAs(instance);
    }

    [Fact]
    public void AutofacAddLiteBus_ShouldResolveRegisteredSingletonInstance()
    {
        var builder = new ContainerBuilder();
        var instance = new TestServiceA();

        builder.AddLiteBus(registry =>
        {
            registry.Register(new RegistrationModule(configuration =>
            {
                configuration.DependencyRegistry.Register(new DependencyDescriptor(typeof(ITestService), instance));
            }));
        });

        using var container = builder.Build();
        container.Resolve<ITestService>().Should().BeSameAs(instance);
    }
}
