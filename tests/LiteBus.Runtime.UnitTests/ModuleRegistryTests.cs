using LiteBus.Runtime.Abstractions;
using LiteBus.Runtime.Modules;

namespace LiteBus.Runtime.UnitTests;

public sealed class ModuleRegistryTests
{
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

        var order = registry.Select(descriptor => descriptor.ModuleType).ToList();

        order.IndexOf(typeof(ChainCModule)).Should().BeLessThan(order.IndexOf(typeof(ChainBModule)));
        order.IndexOf(typeof(ChainBModule)).Should().BeLessThan(order.IndexOf(typeof(ChainAModule)));
    }

    [Fact]
    public void Enumerate_WithCircularDependency_ShouldThrowInvalidOperationException()
    {
        var registry = new ModuleRegistry();
        registry.Register(new CycleAModule());
        registry.Register(new CycleBModule());

        var act = () => registry.ToList();

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*Circular dependency*");
    }

    [Fact]
    public void Enumerate_WithMissingRequiredModule_ShouldThrowInvalidOperationException()
    {
        var registry = new ModuleRegistry();
        registry.Register(new MissingDependencyModule());

        var act = () => registry.ToList();

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*requires*FoundationModule*not registered*");
    }

    [Fact]
    public void Register_AfterEnumeration_ShouldRebuildDependencyOrder()
    {
        var registry = new ModuleRegistry();
        registry.Register(new FoundationModule());
        _ = registry.ToList();

        registry.Register(new DependentModule());

        var order = registry.Select(descriptor => descriptor.ModuleType).ToList();
        order.IndexOf(typeof(FoundationModule)).Should().BeLessThan(order.IndexOf(typeof(DependentModule)));
    }

    [Fact]
    public void ModuleDescriptor_Create_ShouldCollectIRequiresDependencies()
    {
        var descriptor = ModuleDescriptor.Create(new DependentModule());

        descriptor.DependsOn(typeof(FoundationModule)).Should().BeTrue();
    }
}
