using LiteBus.Runtime.Abstractions;

namespace LiteBus.Runtime.UnitTests;

public sealed class DependencyDescriptorTests
{
    [Fact]
    public void Equals_WithSameTypeRegistration_ShouldBeEqual()
    {
        var left = new DependencyDescriptor(typeof(ITestService), typeof(TestServiceA));
        var right = new DependencyDescriptor(typeof(ITestService), typeof(TestServiceA));

        left.Should().Be(right);
    }

    [Fact]
    public void Equals_WithDifferentImplementationTypes_ShouldNotBeEqual()
    {
        var left = new DependencyDescriptor(typeof(ITestService), typeof(TestServiceA));
        var right = new DependencyDescriptor(typeof(ITestService), typeof(TestServiceB));

        left.Should().NotBe(right);
    }

    [Fact]
    public void Equals_WithSameInstanceReference_ShouldBeEqual()
    {
        var instance = new object();
        var left = new DependencyDescriptor(typeof(ITestService), instance);
        var right = new DependencyDescriptor(typeof(ITestService), instance);

        left.Should().Be(right);
    }

    [Fact]
    public void Equals_WithDifferentInstancesForSameServiceType_ShouldNotBeEqual()
    {
        var left = new DependencyDescriptor(typeof(ITestService), new object());
        var right = new DependencyDescriptor(typeof(ITestService), new object());

        left.Should().NotBe(right);
    }

    [Fact]
    public void Equals_WithSameFactoryReference_ShouldBeEqual()
    {
        Func<IServiceProvider, object> factory = _ => new TestServiceA();
        var left = new DependencyDescriptor(typeof(ITestService), factory);
        var right = new DependencyDescriptor(typeof(ITestService), factory);

        left.Should().Be(right);
    }

    [Fact]
    public void Equals_WithDifferentFactoriesForSameServiceType_ShouldNotBeEqual()
    {
        var left = new DependencyDescriptor(typeof(ITestService), _ => new TestServiceA());
        var right = new DependencyDescriptor(typeof(ITestService), _ => new TestServiceB());

        left.Should().NotBe(right);
    }
}
