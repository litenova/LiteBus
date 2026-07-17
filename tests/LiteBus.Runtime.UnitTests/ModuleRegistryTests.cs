using System.Linq;
using LiteBus.Runtime.Abstractions;
using LiteBus.Runtime.Abstractions.Exceptions;
using LiteBus.Runtime.Modules;

namespace LiteBus.Runtime.UnitTests;

public sealed class ModuleRegistryTests
{
    [Fact]
    public void Register_DuplicateModuleType_ShouldThrowLiteBusConfigurationException()
    {
        var registry = new ModuleRegistry();
        registry.Register(new IndependentModule());

        var act = () => registry.Register(new IndependentModule());

        act.Should()
            .Throw<LiteBusConfigurationException>()
            .WithMessage("*IndependentModule*already registered*");
    }

    [Fact]
    public void Register_WithNullModule_ShouldThrowArgumentNullException()
    {
        var registry = new ModuleRegistry();

        var act = () => registry.Register(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void IsModuleRegistered_WithExactTypeMatch_ShouldReturnTrue()
    {
        var registry = new ModuleRegistry();
        registry.Register(new IndependentModule());

        registry.IsModuleRegistered<IndependentModule>().Should().BeTrue();
        registry.IsModuleRegistered<FoundationModule>().Should().BeFalse();
    }

    [Fact]
    public void Enumerate_WithDependencyChain_ShouldOrderDependenciesFirst()
    {
        var registry = new ModuleRegistry();
        registry.Register(new ChainAModule());
        registry.Register(new ChainBModule());
        registry.Register(new ChainCModule());

        var order = registry.BuildOrder().Select(descriptor => descriptor.ModuleType).ToList();

        order.IndexOf(typeof(ChainCModule)).Should().BeLessThan(order.IndexOf(typeof(ChainBModule)));
        order.IndexOf(typeof(ChainBModule)).Should().BeLessThan(order.IndexOf(typeof(ChainAModule)));
    }

    [Fact]
    public void Enumerate_WithCircularDependency_ShouldThrowLiteBusConfigurationException()
    {
        var registry = new ModuleRegistry();
        registry.Register(new CycleAModule());
        registry.Register(new CycleBModule());

        var act = () => registry.BuildOrder();

        act.Should()
            .Throw<LiteBusConfigurationException>()
            .WithMessage("*CycleAModule -> CycleBModule -> CycleAModule*");
    }

    [Fact]
    public void Enumerate_WithMissingRequiredModule_ShouldThrowLiteBusConfigurationException()
    {
        var registry = new ModuleRegistry();
        registry.Register(new MissingDependencyModule());

        var act = () => registry.BuildOrder();

        act.Should()
            .Throw<LiteBusConfigurationException>()
            .WithMessage("*requires*FoundationModule*not registered*");
    }

    [Fact]
    public void Register_AfterBuildOrder_ShouldThrowLiteBusConfigurationException()
    {
        var registry = new ModuleRegistry();
        registry.Register(new FoundationModule());
        _ = registry.BuildOrder();

        var act = () => registry.Register(new DependentModule());

        act.Should()
            .Throw<LiteBusConfigurationException>()
            .WithMessage("*Cannot register modules after BuildOrder()*");
    }

    [Fact]
    public void BuildOrder_ShouldFreezeFurtherRegistration()
    {
        var registry = new ModuleRegistry();
        registry.Register(new FoundationModule());
        registry.Register(new DependentModule());

        var order = registry.BuildOrder().Select(descriptor => descriptor.ModuleType).ToList();
        order.IndexOf(typeof(FoundationModule)).Should().BeLessThan(order.IndexOf(typeof(DependentModule)));

        var act = () => registry.Register(new IndependentModule());

        act.Should()
            .Throw<LiteBusConfigurationException>()
            .WithMessage("*Cannot register modules after BuildOrder()*");
    }

    [Fact]
    public void ModuleDescriptor_Create_ShouldCollectIRequiresDependencies()
    {
        var descriptor = ModuleDescriptor.Create(new DependentModule());

        descriptor.DependsOn(typeof(FoundationModule)).Should().BeTrue();
    }
}
