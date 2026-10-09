using LiteBus.Runtime.Abstractions.Diagnostics;
using LiteBus.Runtime.Abstractions.Exceptions;

namespace LiteBus.Runtime.UnitTests;

public sealed class DiagnosticExceptionConstructorTests
{
    [Fact]
    public void LiteBusDependencyResolutionException_WithNullServiceType_ShouldThrowArgumentNullException()
    {
        var act = () => new LiteBusDependencyResolutionException(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("serviceType");
    }

    [Fact]
    public void LiteBusDependencyResolutionException_WithServiceType_ShouldNameIt()
    {
        var exception = new LiteBusDependencyResolutionException(typeof(string));

        exception.ServiceType.Should().Be<string>();
        exception.Message.Should().Contain(typeof(string).FullName!);
    }

    [Fact]
    public void DiagnosticCheckNameMismatchException_WithNullImplementationType_ShouldThrowArgumentNullException()
    {
        var act = () => new DiagnosticCheckNameMismatchException(null!, "manifest.name", "check.name");

        act.Should().Throw<ArgumentNullException>().WithParameterName("implementationType");
    }

    [Fact]
    public void DiagnosticCheckNameMismatchException_WithNullDescriptorName_ShouldThrowArgumentNullException()
    {
        var act = () => new DiagnosticCheckNameMismatchException(typeof(string), null!, "check.name");

        act.Should().Throw<ArgumentNullException>().WithParameterName("descriptorName");
    }

    [Fact]
    public void DiagnosticCheckNameMismatchException_WithNullCheckName_ShouldThrowArgumentNullException()
    {
        var act = () => new DiagnosticCheckNameMismatchException(typeof(string), "manifest.name", null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("checkName");
    }

    [Fact]
    public void DiagnosticCheckNameMismatchException_WithNames_ShouldDescribeBoth()
    {
        var exception = new DiagnosticCheckNameMismatchException(typeof(string), "manifest.name", "check.name");

        exception.ImplementationType.Should().Be<string>();
        exception.DescriptorName.Should().Be("manifest.name");
        exception.CheckName.Should().Be("check.name");
        exception.Message.Should().Contain("manifest.name").And.Contain("check.name");
    }
}
