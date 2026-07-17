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
        var instance = new TestServiceA();
        var left = new DependencyDescriptor(typeof(ITestService), instance);
        var right = new DependencyDescriptor(typeof(ITestService), instance);

        left.Should().Be(right);
    }

    [Fact]
    public void Equals_WithDifferentInstancesForSameServiceType_ShouldNotBeEqual()
    {
        var left = new DependencyDescriptor(typeof(ITestService), new TestServiceA());
        var right = new DependencyDescriptor(typeof(ITestService), new TestServiceA());

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

    [Fact]
    public void Equals_WithSameTypesButDifferentLifetime_ShouldNotBeEqual()
    {
        var transient = new DependencyDescriptor(typeof(ITestService), typeof(TestServiceA), InstanceLifetime.Transient);
        var singleton = new DependencyDescriptor(typeof(ITestService), typeof(TestServiceA), InstanceLifetime.Singleton);

        transient.Should().NotBe(singleton);
    }

    [Fact]
    public void FactoryConstructor_WithSingletonLifetime_ShouldRetainLifetime()
    {
        var descriptor = new DependencyDescriptor(typeof(ITestService), _ => new TestServiceA(), InstanceLifetime.Singleton);

        descriptor.Lifetime.Should().Be(InstanceLifetime.Singleton);
        descriptor.Factory.Should().NotBeNull();
    }

    [Fact]
    public void TypeConstructor_WithUnrelatedImplementationType_ShouldThrowArgumentException()
    {
        var act = () => new DependencyDescriptor(typeof(ITestService), typeof(UnrelatedService));

        act.Should().Throw<ArgumentException>()
            .WithParameterName("implementationType");
    }

    [Fact]
    public void TypeConstructor_WithAbstractImplementationType_ShouldThrowArgumentException()
    {
        var act = () => new DependencyDescriptor(typeof(ITestService), typeof(AbstractTestService));

        act.Should().Throw<ArgumentException>()
            .WithParameterName("implementationType");
    }

    [Fact]
    public void TypeConstructor_WithCompatibleOpenGenericImplementation_ShouldRetainTypes()
    {
        var descriptor = new DependencyDescriptor(typeof(IGenericService<>), typeof(GenericService<>));

        descriptor.DependencyType.Should().Be(typeof(IGenericService<>));
        descriptor.ImplementationType.Should().Be(typeof(GenericService<>));
    }

    [Fact]
    public void InstanceConstructor_WithUnrelatedInstance_ShouldThrowArgumentException()
    {
        var act = () => new DependencyDescriptor(typeof(ITestService), new UnrelatedService());

        act.Should().Throw<ArgumentException>()
            .WithParameterName("instance");
    }

    [Fact]
    public void TypeConstructor_WithUndefinedLifetime_ShouldThrowArgumentOutOfRangeException()
    {
        var act = () => new DependencyDescriptor(
            typeof(ITestService),
            typeof(TestServiceA),
            (InstanceLifetime) 999);

        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("lifetime");
    }

    [Fact]
    public void Equality_WithDifferentServicesOrCollectionMetadata_ShouldReturnFalse()
    {
        var descriptor = new DependencyDescriptor(typeof(ITestService), typeof(TestServiceA));
        var differentService = new DependencyDescriptor(typeof(object), typeof(TestServiceA));
        var collection = DependencyDescriptor.ForCollection(typeof(ITestService), typeof(TestServiceA));

        descriptor.Equals(differentService).Should().BeFalse();
        descriptor.Equals(collection).Should().BeFalse();
        (descriptor == new DependencyDescriptor(typeof(ITestService), typeof(TestServiceA))).Should().BeTrue();
        (descriptor != differentService).Should().BeTrue();
    }

    [Fact]
    public void ForCollection_WithInstanceOrFactory_ShouldRetainCollectionMetadata()
    {
        var instance = new TestServiceA();
        Func<IServiceProvider, object> factory = _ => instance;

        var instanceDescriptor = DependencyDescriptor.ForCollection(typeof(ITestService), instance);
        var factoryDescriptor = DependencyDescriptor.ForCollection(
            typeof(ITestService),
            factory,
            InstanceLifetime.Scoped);

        instanceDescriptor.Instance.Should().BeSameAs(instance);
        instanceDescriptor.IsCollectionRegistration.Should().BeTrue();
        factoryDescriptor.Factory.Should().BeSameAs(factory);
        factoryDescriptor.Lifetime.Should().Be(InstanceLifetime.Scoped);
        factoryDescriptor.IsCollectionRegistration.Should().BeTrue();
    }

    [Fact]
    public void OpenGenericRegistration_ShouldValidateImplementationShapeAndBaseTypes()
    {
        var derived = new DependencyDescriptor(typeof(GenericBase<>), typeof(DerivedGeneric<>));
        var nonGenericAct = () => new DependencyDescriptor(typeof(IGenericService<>), typeof(TestServiceA));
        var unrelatedAct = () => new DependencyDescriptor(typeof(IGenericService<>), typeof(UnrelatedGeneric<>));

        derived.ImplementationType.Should().Be(typeof(DerivedGeneric<>));
        nonGenericAct.Should().Throw<ArgumentException>();
        unrelatedAct.Should().Throw<ArgumentException>();
    }

    private interface IGenericService<T>;

    private abstract class AbstractTestService : ITestService;

    private sealed class GenericService<T> : IGenericService<T>;

    private abstract class GenericBase<T>;

    private sealed class DerivedGeneric<T> : GenericBase<T>;

    private sealed class UnrelatedGeneric<T>;

    private sealed class UnrelatedService;
}
