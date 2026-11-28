using LiteBus.Runtime.Abstractions;
using LiteBus.Runtime.Abstractions.Exceptions;

namespace LiteBus.Runtime.UnitTests;

public sealed class RegistrationConflictTests
{
    [Fact]
    public void IsIdentical_WithEqualRecords_ShouldReturnTrue()
    {
        var existing = new TestMapping("code", 200);
        var incoming = new TestMapping("code", 200);

        RegistrationConflict.IsIdentical(existing, incoming).Should().BeTrue();
    }

    [Fact]
    public void IsIdentical_WithDifferentValues_ShouldReturnFalse()
    {
        var existing = new TestMapping("code", 200);
        var incoming = new TestMapping("code", 404);

        RegistrationConflict.IsIdentical(existing, incoming).Should().BeFalse();
    }

    [Fact]
    public void IsIdentical_WithReferenceTypeWithoutValueEquality_ShouldFallBackToReferenceEquality()
    {
        var instance = new TestReference();
        var same = instance;
        var other = new TestReference();

        RegistrationConflict.IsIdentical(instance, same).Should().BeTrue();
        RegistrationConflict.IsIdentical(instance, other).Should().BeFalse();
    }

    [Fact]
    public void ThrowIfConflicting_WithIdenticalValues_ShouldNotThrow()
    {
        var existing = new TestMapping("code", 200);
        var incoming = new TestMapping("code", 200);

        var act = () => RegistrationConflict.ThrowIfConflicting(
            existing,
            incoming,
            "Test mapping 'code'",
            "first source",
            "second source");

        act.Should().NotThrow();
    }

    [Fact]
    public void ThrowIfConflicting_WithConflictingValues_ShouldThrowNamingBothSources()
    {
        var existing = new TestMapping("code", 200);
        var incoming = new TestMapping("code", 404);

        var act = () => RegistrationConflict.ThrowIfConflicting(
            existing,
            incoming,
            "Test mapping 'code'",
            "first source",
            "second source");

        act.Should().Throw<LiteBusConfigurationException>()
            .WithMessage("*Test mapping 'code'*first source*second source*Replace*");
    }

    [Fact]
    public void ThrowIfConflicting_WithNullRegistrationName_ShouldThrowArgumentException()
    {
        var act = () => RegistrationConflict.ThrowIfConflicting(
            new TestMapping("code", 200),
            new TestMapping("code", 404),
            null!,
            "first source",
            "second source");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ThrowIfConflicting_WithBlankSource_ShouldThrowArgumentException()
    {
        var act = () => RegistrationConflict.ThrowIfConflicting(
            new TestMapping("code", 200),
            new TestMapping("code", 404),
            "Test mapping 'code'",
            "first source",
            "   ");

        act.Should().Throw<ArgumentException>();
    }

    private sealed record TestMapping(string Code, int StatusCode);

    private sealed class TestReference;
}
