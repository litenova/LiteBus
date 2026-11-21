using LiteBus.Extensions.AspNetCore;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Extensions.UnitTests.AspNetCore.Problems;

public sealed class ProblemTypeFormatterTests
{
    [Fact]
    public void UrnFormatter_CodedRefusal_WritesCodeWithColons()
    {
        var formatted = UrnProblemTypeFormatter.Default.Format("validation.amount.negative", MediationOutcome.Invalid);

        Assert.Equal("urn:litebus:validation:amount:negative", formatted);
    }

    [Theory]
    [InlineData(MediationOutcome.Invalid, "urn:litebus:mediation:invalid")]
    [InlineData(MediationOutcome.Denied, "urn:litebus:mediation:denied")]
    [InlineData(MediationOutcome.Failed, "urn:litebus:mediation:failed")]
    [InlineData(MediationOutcome.Canceled, "urn:litebus:mediation:canceled")]
    public void UrnFormatter_UncodedRefusal_WritesOutcomeType(MediationOutcome outcome, string expected)
    {
        Assert.Equal(expected, UrnProblemTypeFormatter.Default.Format(null, outcome));
        Assert.Equal(expected, UrnProblemTypeFormatter.Default.Format("   ", outcome));
    }

    [Fact]
    public void DocumentationUrlFormatter_CodedRefusal_SubstitutesCode()
    {
        var formatter = new DocumentationUrlProblemTypeFormatter("https://errors.example.com/{code}");

        var formatted = formatter.Format("validation.amount.negative", MediationOutcome.Invalid);

        Assert.Equal("https://errors.example.com/validation.amount.negative", formatted);
    }

    [Fact]
    public void DocumentationUrlFormatter_UncodedRefusal_SubstitutesOutcomeIdentifier()
    {
        var formatter = new DocumentationUrlProblemTypeFormatter("https://errors.example.com/{code}");

        Assert.Equal(
            "https://errors.example.com/mediation.invalid",
            formatter.Format(null, MediationOutcome.Invalid));
        Assert.Equal(
            "https://errors.example.com/mediation.denied",
            formatter.Format(null, MediationOutcome.Denied));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("https://errors.example.com/problems")]
    public void DocumentationUrlFormatter_TemplateWithoutPlaceholder_Throws(string? template)
    {
        Assert.ThrowsAny<ArgumentException>(() => new DocumentationUrlProblemTypeFormatter(template!));
    }

    [Fact]
    public void AboutBlankFormatter_Always_WritesAboutBlank()
    {
        Assert.Equal("about:blank", AboutBlankProblemTypeFormatter.Default.Format("billing.unpaid", MediationOutcome.Denied));
        Assert.Equal("about:blank", AboutBlankProblemTypeFormatter.Default.Format(null, MediationOutcome.Invalid));
    }
}
