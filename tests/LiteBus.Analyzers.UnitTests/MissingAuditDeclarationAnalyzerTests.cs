namespace LiteBus.Analyzers.UnitTests;

/// <summary>
///     Tests for the <see cref="MissingAuditDeclarationAnalyzer" /> rule.
/// </summary>
public sealed class MissingAuditDeclarationAnalyzerTests
{
    /// <summary>
    ///     Verifies that a command declaring itself audited produces no diagnostic.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task AuditedCommand_ProducesNoDiagnostic()
    {
        const string source = """
                              using LiteBus.Commands.Abstractions;
                              using LiteBus.Messaging.Abstractions;

                              [Audited("users.create-user")]
                              public sealed record CreateUserCommand(string Name) : ICommand;
                              """;

        return AnalyzerTest.VerifyNoDiagnosticsAsync<MissingAuditDeclarationAnalyzer>(source);
    }

    /// <summary>
    ///     Verifies that a command declaring itself exempt produces no diagnostic.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task ExemptCommand_ProducesNoDiagnostic()
    {
        const string source = """
                              using LiteBus.Commands.Abstractions;
                              using LiteBus.Messaging.Abstractions;

                              [AuditExempt("no sensitive data is touched")]
                              public sealed record PingCommand : ICommand;
                              """;

        return AnalyzerTest.VerifyNoDiagnosticsAsync<MissingAuditDeclarationAnalyzer>(source);
    }

    /// <summary>
    ///     Verifies that a command covered by an audit definition produces no diagnostic.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task CommandWithAuditDefinition_ProducesNoDiagnostic()
    {
        const string source = """
                              using LiteBus.Commands.Abstractions;
                              using LiteBus.Messaging.Abstractions;

                              public sealed record ShipOrderCommand : ICommand;

                              public sealed class ShipOrderCommandDefinition : IAuditDefinition<ShipOrderCommand>
                              {
                                  public AuditDeclaration Audit => AuditDeclaration.Audited("orders.ship-order");
                              }
                              """;

        return AnalyzerTest.VerifyNoDiagnosticsAsync<MissingAuditDeclarationAnalyzer>(source);
    }

    /// <summary>
    ///     Verifies that a command stating no audit position produces LB1018.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task UndeclaredCommand_ProducesDiagnostic()
    {
        const string source = """
                              using LiteBus.Commands.Abstractions;

                              public sealed record {|#0:CreateUserCommand|}(string Name) : ICommand;
                              """;

        return AnalyzerTest.VerifyDiagnosticAsync<MissingAuditDeclarationAnalyzer>(
            source,
            DiagnosticDescriptors.MissingAuditDeclaration,
            0,
            "CreateUserCommand");
    }

    /// <summary>
    ///     Verifies that a query stating no audit position produces LB1018.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task UndeclaredQuery_ProducesDiagnostic()
    {
        const string source = """
                              using LiteBus.Queries.Abstractions;

                              public sealed record {|#0:GetUserQuery|}(string Id) : IQuery<string>;
                              """;

        return AnalyzerTest.VerifyDiagnosticAsync<MissingAuditDeclarationAnalyzer>(
            source,
            DiagnosticDescriptors.MissingAuditDeclaration,
            0,
            "GetUserQuery");
    }
}
