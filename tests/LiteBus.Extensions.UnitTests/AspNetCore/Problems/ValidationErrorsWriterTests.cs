using LiteBus.Extensions.AspNetCore;
using LiteBus.Messaging.Abstractions;
using Microsoft.AspNetCore.Mvc;

namespace LiteBus.Extensions.UnitTests.AspNetCore.Problems;

public sealed class ValidationErrorsWriterTests
{
    [Fact]
    public void DictionaryWriter_GroupsMessagesByMemberWithMessageLevelUnderEmptyKey()
    {
        var problem = new ProblemDetails();
        IReadOnlyList<ValidationFailure> failures =
        [
            new ValidationFailure("Amount must be positive", "Amount"),
            new ValidationFailure("Amount must be below the limit", "Amount"),
            new ValidationFailure("The request is malformed")
        ];

        DictionaryValidationErrorsWriter.Default.WriteErrors(problem, failures);

        var errors = Assert.IsType<Dictionary<string, string[]>>(problem.Extensions["errors"]);
        var amount = errors["Amount"];
        Assert.Equal(2, amount.Length);
        Assert.Equal("Amount must be positive", amount[0]);
        Assert.Equal("Amount must be below the limit", amount[1]);
        var messageLevel = errors[string.Empty];
        Assert.Single(messageLevel);
        Assert.Equal("The request is malformed", messageLevel[0]);
    }

    [Fact]
    public void DictionaryWriter_WritesCodesAlignedWithMessages()
    {
        var problem = new ProblemDetails();
        IReadOnlyList<ValidationFailure> failures =
        [
            new ValidationFailure("Amount must be positive", "Amount", "validation.amount.negative"),
            new ValidationFailure("Amount must be below the limit", "Amount"),
            new ValidationFailure("The request is malformed", code: "validation.malformed")
        ];

        DictionaryValidationErrorsWriter.Default.WriteErrors(problem, failures);

        var errors = Assert.IsType<Dictionary<string, string[]>>(problem.Extensions["errors"]);
        var codes = Assert.IsType<Dictionary<string, string?[]>>(problem.Extensions["errorCodes"]);
        Assert.Equal(errors["Amount"].Length, codes["Amount"].Length);
        Assert.Equal("validation.amount.negative", codes["Amount"][0]);
        Assert.Null(codes["Amount"][1]);
        var messageLevel = codes[string.Empty];
        Assert.Single(messageLevel);
        Assert.Equal("validation.malformed", messageLevel[0]);
    }

    [Fact]
    public void DictionaryWriter_EmptyFailures_WritesNoExtensions()
    {
        var problem = new ProblemDetails();

        DictionaryValidationErrorsWriter.Default.WriteErrors(problem, []);

        Assert.Empty(problem.Extensions);
    }

    [Fact]
    public void ArrayWriter_WritesOneEntryPerFailureWithDetailPointerAndCode()
    {
        var problem = new ProblemDetails();
        IReadOnlyList<ValidationFailure> failures =
        [
            new ValidationFailure("Amount must be positive", "Amount", "validation.amount.negative"),
            new ValidationFailure("The request is malformed")
        ];

        ArrayValidationErrorsWriter.Default.WriteErrors(problem, failures);

        var errors = Assert.IsAssignableFrom<IReadOnlyList<ValidationErrorEntry>>(problem.Extensions["errors"]);
        Assert.Equal(2, errors.Count);
        Assert.Equal("Amount must be positive", errors[0].Detail);
        Assert.Equal("/Amount", errors[0].JsonPointer);
        Assert.Equal("validation.amount.negative", errors[0].Code);
        Assert.Equal("The request is malformed", errors[1].Detail);
        Assert.Null(errors[1].JsonPointer);
        Assert.Null(errors[1].Code);
    }

    [Fact]
    public void ArrayWriter_MemberWithPointerCharacters_EscapesPointer()
    {
        var problem = new ProblemDetails();
        IReadOnlyList<ValidationFailure> failures = [new ValidationFailure("Line is invalid", "lines/0~total")];

        ArrayValidationErrorsWriter.Default.WriteErrors(problem, failures);

        var errors = Assert.IsAssignableFrom<IReadOnlyList<ValidationErrorEntry>>(problem.Extensions["errors"]);
        Assert.Equal("/lines~10~0total", errors[0].JsonPointer);
    }

    [Fact]
    public void ArrayWriter_EmptyFailures_WritesNoExtensions()
    {
        var problem = new ProblemDetails();

        ArrayValidationErrorsWriter.Default.WriteErrors(problem, []);

        Assert.Empty(problem.Extensions);
    }
}
