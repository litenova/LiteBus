using LiteBus.Runtime.Abstractions;
using LiteBus.Runtime.Dependencies;

namespace LiteBus.Runtime.UnitTests;

public sealed class DependencyRegistryTests
{
    [Fact]
    public void Register_WithDuplicateTypeDescriptor_ShouldIgnoreDuplicate()
    {
        var registry = new DependencyRegistry();
        var descriptor = new DependencyDescriptor(typeof(ITestService), typeof(TestServiceA));

        registry.Register(descriptor);
        registry.Register(new DependencyDescriptor(typeof(ITestService), typeof(TestServiceA)));

        registry.Count.Should().Be(1);
    }

    [Fact]
    public void Register_WithDifferentInstancesForSameServiceType_ShouldKeepBoth()
    {
        var registry = new DependencyRegistry();

        registry.Register(new DependencyDescriptor(typeof(ITestService), new TestServiceA()));
        registry.Register(new DependencyDescriptor(typeof(ITestService), new TestServiceB()));

        registry.Count.Should().Be(2);
    }

    [Fact]
    public void RegisterBackgroundWork_ShouldThrowInvalidOperationException()
    {
        var registry = new DependencyRegistry();

        var act = () => registry.RegisterBackgroundWork(typeof(TestBackgroundWork));

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*AddLiteBus*");
    }

    [Fact]
    public void Register_WithNullDescriptor_ShouldThrowArgumentNullException()
    {
        var registry = new DependencyRegistry();

        var act = () => registry.Register(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
