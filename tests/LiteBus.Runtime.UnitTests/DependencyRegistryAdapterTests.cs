using Autofac;
using Autofac.Extensions.DependencyInjection;
using LiteBus.Extensions.Autofac;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Runtime.Abstractions;
using LiteBus.Runtime.Abstractions.Exceptions;
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
    public void MicrosoftAdapter_RegisterConflictingBindingForSameServiceType_ShouldThrowLiteBusConfigurationException()
    {
        var services = new ServiceCollection();
        var adapter = new MicrosoftDependencyRegistryAdapter(services);

        adapter.Register(new DependencyDescriptor(typeof(ITestService), typeof(TestServiceA)));

        var act = () => adapter.Register(new DependencyDescriptor(typeof(ITestService), typeof(TestServiceB)));

        act.Should().Throw<LiteBusConfigurationException>()
            .WithMessage("*ITestService*");
    }

    [Fact]
    public void MicrosoftAdapter_RegisterDifferentInstancesForSameServiceType_ShouldThrowLiteBusConfigurationException()
    {
        var services = new ServiceCollection();
        var adapter = new MicrosoftDependencyRegistryAdapter(services);

        adapter.Register(new DependencyDescriptor(typeof(ITestService), new TestServiceA()));

        var act = () => adapter.Register(new DependencyDescriptor(typeof(ITestService), new TestServiceB()));

        act.Should().Throw<LiteBusConfigurationException>()
            .WithMessage("*ITestService*");
    }

    [Fact]
    public void MicrosoftAdapter_RegisterScopedFactory_ShouldRegisterWithScopedLifetime()
    {
        var services = new ServiceCollection();
        var adapter = new MicrosoftDependencyRegistryAdapter(services);

        adapter.Register(new DependencyDescriptor(typeof(ITestService), _ => new TestServiceA(), InstanceLifetime.Scoped));

        var registration = services.Should().ContainSingle(service => service.ServiceType == typeof(ITestService)).Subject;
        registration.Lifetime.Should().Be(ServiceLifetime.Scoped);
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
    public void AutofacAdapter_RegisterConflictingBindingForSameServiceType_ShouldThrowLiteBusConfigurationException()
    {
        var builder = new ContainerBuilder();
        var adapter = new AutofacDependencyRegistryAdapter(builder);

        adapter.Register(new DependencyDescriptor(typeof(ITestService), typeof(TestServiceA)));

        var act = () => adapter.Register(new DependencyDescriptor(typeof(ITestService), typeof(TestServiceB)));

        act.Should().Throw<LiteBusConfigurationException>()
            .WithMessage("*ITestService*");
    }

    [Fact]
    public void AutofacAdapter_RegisterScopedFactory_ShouldResolveOncePerLifetimeScope()
    {
        var builder = new ContainerBuilder();
        var adapter = new AutofacDependencyRegistryAdapter(builder);

        adapter.Register(new DependencyDescriptor(typeof(ITestService), _ => new TestServiceA(), InstanceLifetime.Scoped));

        builder.Register(c => new AutofacServiceProvider(c.Resolve<ILifetimeScope>()))
            .As<IServiceProvider>()
            .InstancePerLifetimeScope();

        using var container = builder.Build();
        using var outerScope = container.BeginLifetimeScope();
        using var innerScope = container.BeginLifetimeScope();

        var outerFirst = outerScope.Resolve<ITestService>();
        var outerSecond = outerScope.Resolve<ITestService>();
        var inner = innerScope.Resolve<ITestService>();

        outerFirst.Should().BeSameAs(outerSecond);
        inner.Should().NotBeSameAs(outerFirst);
    }

    [Fact]
    public void MicrosoftAdapter_RegisterCollection_ShouldAllowMultipleImplementations()
    {
        var services = new ServiceCollection();
        var adapter = new MicrosoftDependencyRegistryAdapter(services);

        adapter.RegisterCollection(DependencyDescriptor.ForCollection(typeof(ITestService), typeof(TestServiceA)));
        adapter.RegisterCollection(DependencyDescriptor.ForCollection(typeof(ITestService), typeof(TestServiceB)));

        adapter.Count.Should().Be(2);
        services.Count(service => service.ServiceType == typeof(ITestService)).Should().Be(2);

        using var provider = services.BuildServiceProvider();
        provider.GetServices<ITestService>().Should().HaveCount(2);
    }

    [Fact]
    public void AutofacAdapter_RegisterCollection_ShouldAllowMultipleImplementations()
    {
        var builder = new ContainerBuilder();
        var adapter = new AutofacDependencyRegistryAdapter(builder);

        adapter.RegisterCollection(DependencyDescriptor.ForCollection(typeof(ITestService), typeof(TestServiceA)));
        adapter.RegisterCollection(DependencyDescriptor.ForCollection(typeof(ITestService), typeof(TestServiceB)));

        RegisterServiceProviderAdapterForTests(builder);

        adapter.Count.Should().Be(2);

        using var container = builder.Build();
        container.Resolve<IEnumerable<ITestService>>().Should().HaveCount(2);
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

    /// <summary>
    ///     Registers the Autofac service provider adapter used by production <c>AddLiteBus</c> configuration.
    /// </summary>
    /// <param name="builder">The Autofac container builder receiving the adapter registration.</param>
    private static void RegisterServiceProviderAdapterForTests(ContainerBuilder builder)
    {
        builder.Register(c => (IServiceProvider)new AutofacServiceProviderAdapter(c.Resolve<ILifetimeScope>()))
            .As<IServiceProvider>()
            .InstancePerLifetimeScope();
    }
}
