using LiteBus.Runtime.Abstractions;

namespace LiteBus.Runtime.UnitTests;

public sealed class DependencyDescriptorDefaultPortTests
{
    [Fact]
    public void NewDescriptor_ShouldNotBeLiteBusDefault()
    {
        var typeDescriptor = new DependencyDescriptor(typeof(ITestService), typeof(TestServiceA));
        var instanceDescriptor = new DependencyDescriptor(typeof(ITestService), new TestServiceA());
        var factoryDescriptor = new DependencyDescriptor(typeof(ITestService), _ => new TestServiceA());

        typeDescriptor.IsLiteBusDefault.Should().BeFalse();
        instanceDescriptor.IsLiteBusDefault.Should().BeFalse();
        factoryDescriptor.IsLiteBusDefault.Should().BeFalse();
    }

    [Fact]
    public void ForCollection_ShouldNotBeLiteBusDefault()
    {
        var descriptor = DependencyDescriptor.ForCollection(typeof(ITestService), typeof(TestServiceA));

        descriptor.IsLiteBusDefault.Should().BeFalse();
        descriptor.IsCollectionRegistration.Should().BeTrue();
    }

    [Fact]
    public void ForDefaultPort_WithType_ShouldMarkDescriptor()
    {
        var descriptor = DependencyDescriptor.ForDefaultPort(
            typeof(ITestService),
            typeof(TestServiceA),
            InstanceLifetime.Scoped);

        descriptor.DependencyType.Should().Be<ITestService>();
        descriptor.ImplementationType.Should().Be<TestServiceA>();
        descriptor.Lifetime.Should().Be(InstanceLifetime.Scoped);
        descriptor.IsLiteBusDefault.Should().BeTrue();
        descriptor.IsCollectionRegistration.Should().BeFalse();
    }

    [Fact]
    public void ForDefaultPort_WithInstance_ShouldMarkDescriptor()
    {
        var instance = new TestServiceA();

        var descriptor = DependencyDescriptor.ForDefaultPort(typeof(ITestService), instance);

        descriptor.Instance.Should().BeSameAs(instance);
        descriptor.Lifetime.Should().Be(InstanceLifetime.Singleton);
        descriptor.IsLiteBusDefault.Should().BeTrue();
        descriptor.IsCollectionRegistration.Should().BeFalse();
    }

    [Fact]
    public void ForDefaultPort_WithFactory_ShouldMarkDescriptor()
    {
        Func<IServiceProvider, object> factory = _ => new TestServiceA();

        var descriptor = DependencyDescriptor.ForDefaultPort(typeof(ITestService), factory);

        descriptor.Factory.Should().BeSameAs(factory);
        descriptor.IsLiteBusDefault.Should().BeTrue();
        descriptor.IsCollectionRegistration.Should().BeFalse();
    }

    [Fact]
    public void AsLiteBusDefault_OnUnmarkedDescriptor_ShouldReturnMarkedCopyPreservingBinding()
    {
        var factory = (Func<IServiceProvider, object>) (_ => new TestServiceA());
        var typeSource = new DependencyDescriptor(typeof(ITestService), typeof(TestServiceA), InstanceLifetime.Scoped);
        var instance = new TestServiceA();
        var instanceSource = new DependencyDescriptor(typeof(ITestService), instance);
        var factorySource = new DependencyDescriptor(typeof(ITestService), factory, InstanceLifetime.Singleton);

        var typeMarked = typeSource.AsLiteBusDefault();
        var instanceMarked = instanceSource.AsLiteBusDefault();
        var factoryMarked = factorySource.AsLiteBusDefault();

        typeMarked.Should().NotBeSameAs(typeSource);
        typeMarked.Should().Be(typeSource);
        typeMarked.IsLiteBusDefault.Should().BeTrue();
        typeMarked.Lifetime.Should().Be(InstanceLifetime.Scoped);

        instanceMarked.Instance.Should().BeSameAs(instance);
        instanceMarked.IsLiteBusDefault.Should().BeTrue();

        factoryMarked.Factory.Should().BeSameAs(factory);
        factoryMarked.Lifetime.Should().Be(InstanceLifetime.Singleton);
        factoryMarked.IsLiteBusDefault.Should().BeTrue();
    }

    [Fact]
    public void AsLiteBusDefault_OnMarkedDescriptor_ShouldReturnSameInstance()
    {
        var descriptor = DependencyDescriptor.ForDefaultPort(typeof(ITestService), typeof(TestServiceA));

        descriptor.AsLiteBusDefault().Should().BeSameAs(descriptor);
    }

    [Fact]
    public void Equals_WithSameBindingButDifferentDefaultMarker_ShouldBeEqual()
    {
        var instance = new TestServiceA();
        Func<IServiceProvider, object> factory = _ => new TestServiceA();

        var defaultType = DependencyDescriptor.ForDefaultPort(typeof(ITestService), typeof(TestServiceA));
        var explicitType = new DependencyDescriptor(typeof(ITestService), typeof(TestServiceA));
        var defaultInstance = DependencyDescriptor.ForDefaultPort(typeof(ITestService), instance);
        var explicitInstance = new DependencyDescriptor(typeof(ITestService), instance);
        var defaultFactory = DependencyDescriptor.ForDefaultPort(typeof(ITestService), factory);
        var explicitFactory = new DependencyDescriptor(typeof(ITestService), factory);

        defaultType.Should().Be(explicitType);
        defaultType.GetHashCode().Should().Be(explicitType.GetHashCode());
        defaultInstance.Should().Be(explicitInstance);
        defaultFactory.Should().Be(explicitFactory);
    }
}
