using LiteBus.Runtime.Abstractions;
using LiteBus.Runtime.Abstractions.Exceptions;
using LiteBus.Runtime.Modules;

namespace LiteBus.Runtime.UnitTests;

public sealed class CompositeModuleRegistryTests
{
    [Fact]
    public void Register_CompositeModule_ShouldExpandChildrenImmediatelyAfterParent()
    {
        var registry = new ModuleRegistry();
        registry.Register(new ParentCompositeModule());

        var order = registry.Select(descriptor => descriptor.ModuleType).ToList();

        order.Should().Equal(
            typeof(ParentCompositeModule),
            typeof(ChildModuleA),
            typeof(ChildModuleB));
    }

    [Fact]
    public void Register_SameModuleTypeTwice_ShouldThrowConfigurationException()
    {
        var registry = new ModuleRegistry();
        registry.Register(new ChildModuleA());

        var act = () => registry.Register(new ChildModuleA());

        act.Should().Throw<LiteBusConfigurationException>();
    }

    [Fact]
    public void Register_CompositeChildAlsoRegisteredAtTopLevel_ShouldThrowConfigurationException()
    {
        var registry = new ModuleRegistry();
        registry.Register(new ParentCompositeModule());

        var act = () => registry.Register(new ChildModuleA());

        act.Should().Throw<LiteBusConfigurationException>();
    }

    private sealed class ParentCompositeModule : ICompositeModule
    {
        public void DeclareChildren(Action<IModule> registerChild)
        {
            registerChild(new ChildModuleA());
            registerChild(new ChildModuleB());
        }

        public void Build(IModuleConfiguration configuration)
        {
        }
    }

    private sealed class ChildModuleA : IModule
    {
        public void Build(IModuleConfiguration configuration)
        {
        }
    }

    private sealed class ChildModuleB : IModule
    {
        public void Build(IModuleConfiguration configuration)
        {
        }
    }
}