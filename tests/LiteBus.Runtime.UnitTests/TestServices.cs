namespace LiteBus.Runtime.UnitTests;

internal interface ITestService;

internal sealed class TestServiceA : ITestService;

internal sealed class TestServiceB : ITestService;

internal interface IGenericTestService<T>;

internal sealed class GenericTestService<T> : IGenericTestService<T>;
