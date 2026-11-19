using LiteBus.Runtime.Abstractions;
using LiteBus.Runtime.Abstractions.Exceptions;
using LiteBus.Runtime.Dependencies;

namespace LiteBus.Runtime.UnitTests;

public sealed class DependencyRegistryReplaceTests
{
    [Fact]
    public void Replace_WithNewServiceType_ShouldAddDescriptor()
    {
        var registry = new DependencyRegistry();
        var descriptor = new DependencyDescriptor(typeof(ITestService), typeof(TestServiceA));

        registry.Replace(descriptor);

        registry.Count.Should().Be(1);
        registry.Should().ContainSingle().Which.Should().Be(descriptor);
    }

    [Fact]
    public void Replace_WithIdenticalDescriptor_ShouldBeNoOp()
    {
        var registry = new DependencyRegistry();

        registry.Register(new DependencyDescriptor(typeof(ITestService), typeof(TestServiceA)));
        registry.Replace(new DependencyDescriptor(typeof(ITestService), typeof(TestServiceA)));

        registry.Count.Should().Be(1);
    }

    [Fact]
    public void Replace_WithConflictingDescriptor_ShouldOverride()
    {
        var registry = new DependencyRegistry();

        registry.Register(new DependencyDescriptor(typeof(ITestService), typeof(TestServiceA)));
        registry.Replace(new DependencyDescriptor(typeof(ITestService), typeof(TestServiceB)));

        registry.Count.Should().Be(1);
        registry.Should().ContainSingle()
            .Which.ImplementationType.Should().Be<TestServiceB>();
    }

    [Fact]
    public void Replace_WithExplicitOverDefaultPort_ShouldMarkReplacedForCompositionModel()
    {
        var registry = new DependencyRegistry();

        registry.Register(DependencyDescriptor.ForDefaultPort(typeof(ITestService), typeof(TestServiceA)));

        registry.Should().ContainSingle().Which.IsLiteBusDefault.Should().BeTrue();

        registry.Replace(new DependencyDescriptor(typeof(ITestService), typeof(TestServiceB)));

        var effective = registry.Should().ContainSingle().Subject;
        effective.ImplementationType.Should().Be<TestServiceB>();
        effective.IsLiteBusDefault.Should().BeFalse();
    }

    [Fact]
    public void Register_WithIdenticalDefaultAndExplicitBinding_ShouldBeNoOpKeepingFirst()
    {
        var registry = new DependencyRegistry();
        var defaultDescriptor = DependencyDescriptor.ForDefaultPort(typeof(ITestService), typeof(TestServiceA));

        registry.Register(defaultDescriptor);
        registry.Register(new DependencyDescriptor(typeof(ITestService), typeof(TestServiceA)));

        registry.Count.Should().Be(1);
        registry.Should().ContainSingle().Which.Should().BeSameAs(defaultDescriptor);
    }

    [Fact]
    public void Register_DefaultAfterExplicitBinding_ShouldKeepExplicitBinding()
    {
        var registry = new DependencyRegistry();
        var explicitDescriptor = new DependencyDescriptor(typeof(ITestService), typeof(TestServiceB));

        registry.Register(explicitDescriptor);
        registry.Register(DependencyDescriptor.ForDefaultPort(typeof(ITestService), typeof(TestServiceA)));

        registry.Should().ContainSingle().Which.Should().BeSameAs(explicitDescriptor);
    }

    [Fact]
    public void Register_WithConflictingBinding_ShouldThrowNamingBothSources()
    {
        var registry = new DependencyRegistry();

        registry.Register(DependencyDescriptor.ForDefaultPort(typeof(ITestService), typeof(TestServiceA)));

        var act = () => registry.Register(new DependencyDescriptor(typeof(ITestService), typeof(TestServiceB)));

        act.Should().Throw<LiteBusConfigurationException>()
            .WithMessage("*ITestService*LiteBus default*explicit registration*Replace*");
    }

    [Fact]
    public void Replace_ShouldKeepOriginalRegistrationPosition()
    {
        var registry = new DependencyRegistry();

        registry.Register(new DependencyDescriptor(typeof(ITestService), typeof(TestServiceA)));
        registry.Register(new DependencyDescriptor(typeof(IGenericTestService<>), typeof(GenericTestService<>)));
        registry.Replace(new DependencyDescriptor(typeof(ITestService), typeof(TestServiceB)));

        registry.Select(descriptor => descriptor.DependencyType)
            .Should()
            .Equal(typeof(ITestService), typeof(IGenericTestService<>));
        registry.First(descriptor => descriptor.DependencyType == typeof(ITestService))
            .ImplementationType.Should().Be<TestServiceB>();
    }

    [Fact]
    public void Replace_WithCollectionDescriptor_ShouldThrowModuleCompositionException()
    {
        var registry = new DependencyRegistry();

        var act = () => registry.Replace(DependencyDescriptor.ForCollection(typeof(ITestService), typeof(TestServiceA)));

        act.Should().Throw<ModuleCompositionException>();
    }

    [Fact]
    public void Replace_AfterCollectionRegistrationForSameServiceType_ShouldThrowModuleCompositionException()
    {
        var registry = new DependencyRegistry();

        registry.RegisterCollection(DependencyDescriptor.ForCollection(typeof(ITestService), typeof(TestServiceA)));

        var act = () => registry.Replace(new DependencyDescriptor(typeof(ITestService), typeof(TestServiceB)));

        act.Should().Throw<ModuleCompositionException>()
            .WithMessage("*ITestService*");
    }

    [Fact]
    public void Replace_WithNullDescriptor_ShouldThrowArgumentNullException()
    {
        var registry = new DependencyRegistry();

        var act = () => registry.Replace(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
