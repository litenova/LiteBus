using LiteBus.Runtime.Abstractions;
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
    public void Register_SameModuleTypeTwice_ShouldIgnoreSecondRegistration()
    {
        var registry = new ModuleRegistry();
        registry.Register(new ChildModuleA());
        registry.Register(new ChildModuleA());

        registry.Select(descriptor => descriptor.ModuleType).Should().HaveCount(1);
    }

    [Fact]
    public void Register_CompositeChildAlsoRegisteredAtTopLevel_ShouldNotDuplicateChild()
    {
        var registry = new ModuleRegistry();
        registry.Register(new ParentCompositeModule());
        registry.Register(new ChildModuleA());

        registry.Select(descriptor => descriptor.ModuleType)
            .Count(type => type == typeof(ChildModuleA))
            .Should()
            .Be(1);
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
