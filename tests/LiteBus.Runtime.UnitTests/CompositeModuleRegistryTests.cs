using System.Linq;
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

        var order = registry.BuildOrder().Select(descriptor => descriptor.ModuleType).ToList();

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

    [Fact]
    public void Register_WhenCompositeExpansionFails_ShouldNotCommitPartialGraph()
    {
        var registry = new ModuleRegistry();
        registry.Register(new ChildModuleB());

        var act = () => registry.Register(new PartiallyInvalidCompositeModule());

        act.Should().Throw<LiteBusConfigurationException>();
        registry.IsModuleRegistered<PartiallyInvalidCompositeModule>().Should().BeFalse();
        registry.IsModuleRegistered<ChildModuleA>().Should().BeFalse();
        registry.IsModuleRegistered<ChildModuleB>().Should().BeTrue();
        registry.BuildOrder().Select(descriptor => descriptor.ModuleType).Should().Equal(typeof(ChildModuleB));
    }

    [Fact]
    public void BuildOrder_WithChildrenFirstComposite_ShouldBuildChildBeforeParent()
    {
        var registry = new ModuleRegistry();
        registry.Register(new ChildrenFirstCompositeModule());

        var order = registry.BuildOrder().Select(descriptor => descriptor.ModuleType);

        order.Should().Equal(typeof(ChildrenFirstChildModule), typeof(ChildrenFirstCompositeModule));
    }

    [Fact]
    public void BuildOrder_WithNestedOppositeRelationships_ShouldHonorEveryCompositeEdge()
    {
        var registry = new ModuleRegistry();
        registry.Register(new OuterParentFirstCompositeModule());

        var order = registry.BuildOrder().Select(descriptor => descriptor.ModuleType);

        order.Should().Equal(
            typeof(OuterParentFirstCompositeModule),
            typeof(ChildrenFirstChildModule),
            typeof(ChildrenFirstCompositeModule));
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

    private sealed class PartiallyInvalidCompositeModule : ICompositeModule
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

    private sealed class ChildrenFirstCompositeModule : ICompositeModule
    {
        public CompositeModuleBuildOrder BuildOrder => CompositeModuleBuildOrder.ChildrenFirst;

        public void DeclareChildren(Action<IModule> registerChild)
        {
            registerChild(new ChildrenFirstChildModule());
        }

        public void Build(IModuleConfiguration configuration)
        {
        }
    }

    private sealed class ChildrenFirstChildModule : IModule
    {
        public void Build(IModuleConfiguration configuration)
        {
        }
    }

    private sealed class OuterParentFirstCompositeModule : ICompositeModule
    {
        public void DeclareChildren(Action<IModule> registerChild)
        {
            registerChild(new ChildrenFirstCompositeModule());
        }

        public void Build(IModuleConfiguration configuration)
        {
        }
    }
}
