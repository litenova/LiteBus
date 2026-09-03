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
    ///     Verifies that an audit definition written for a marker interface covers the commands beneath it.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    /// <remarks>
    ///     Definition coverage used to be an exact type match, so a definition written for a base command or a marker
    ///     interface did not satisfy the rule for the messages it describes, even though the registry resolves it for
    ///     them at runtime. The rule and the registry have to agree about what counts as declared.
    /// </remarks>
    [Fact]
    public Task AuditDefinitionOnAMarkerInterface_CoversTheFamily()
    {
        const string source = """
                              using LiteBus.Commands.Abstractions;
                              using LiteBus.Messaging.Abstractions;

                              public interface IBillingCommand;

                              public sealed record ChargeCardCommand : ICommand, IBillingCommand;

                              public sealed class BillingCommandDefinition : IAuditDefinition<IBillingCommand>
                              {
                                  public AuditDeclaration Audit => AuditDeclaration.Audited("billing.command");
                              }
                              """;

        return AnalyzerTest.VerifyNoDiagnosticsAsync<MissingAuditDeclarationAnalyzer>(source);
    }

    /// <summary>
    ///     Verifies that a general declaration exemption satisfies the audit position requirement.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task GeneralDeclarationExemption_SatisfiesTheAuditPosition()
    {
        const string source = """
                              using LiteBus.Commands.Abstractions;
                              using LiteBus.Messaging.Abstractions;

                              [DeclarationExempt(typeof(AuditDeclaration), "a health probe touches no customer data")]
                              public sealed record PingCommand : ICommand;
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
