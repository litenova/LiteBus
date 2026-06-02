using Autofac;
using LiteBus.Extensions.Autofac;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Runtime.Abstractions;
using LiteBus.Runtime.Dependencies;
using LiteBus.Runtime.Modules;
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
    public void MicrosoftAddLiteBus_RegisterBackgroundServiceTwice_ShouldRegisterSingleHostedService()
    {
        var services = new ServiceCollection();

        services.AddLiteBus(registry =>
        {
            registry.Register(new RegistrationModule(configuration =>
            {
                configuration.RegisterBackgroundService(typeof(TestBackgroundService));
                configuration.RegisterBackgroundService(typeof(TestBackgroundService));
            }));
        });

        services.Count(service => service.ServiceType == typeof(IHostedService)).Should().Be(1);
    }

    [Fact]
    public void MicrosoftAddLiteBus_RegisterBackgroundServiceWithDifferentTypes_ShouldRegisterBothHostedServices()
    {
        var services = new ServiceCollection();

        services.AddLiteBus(registry =>
        {
            registry.Register(new BackgroundServiceRegistrationModule(typeof(TestBackgroundService), typeof(OtherBackgroundService)));
        });

        services.Count(service => service.ServiceType == typeof(IHostedService)).Should().Be(2);
    }

    [Fact]
    public void MicrosoftModuleConfiguration_RegisterBackgroundServiceWithNonBackgroundServiceType_ShouldThrowArgumentException()
    {
        var configuration = new ModuleConfiguration(new DependencyRegistry());

        var act = () => configuration.RegisterBackgroundService(typeof(object));

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
    public void AutofacAddLiteBus_RegisterBackgroundServiceTwice_ShouldResolveSingleHostedService()
    {
        var builder = new ContainerBuilder();

        builder.AddLiteBus(registry =>
        {
            registry.Register(new RegistrationModule(configuration =>
            {
                configuration.RegisterBackgroundService(typeof(TestBackgroundService));
                configuration.RegisterBackgroundService(typeof(TestBackgroundService));
            }));
        });

        using var container = builder.Build();
        container.Resolve<IEnumerable<IHostedService>>().Should().HaveCount(1);
    }
}
