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

    /// <summary>
    ///     Verifies that a definition declaring the audit position through <c>Describe</c> produces no diagnostic.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    /// <remarks>
    ///     The documentation calls <c>Describe</c> the shape to reach for, and the rule looked only for the attributes
    ///     and the keyed <c>IAuditDefinition</c>, so it reported every message declared the recommended way. The
    ///     analyzer and the recommended shape have to agree, or the rule can only be turned off.
    /// </remarks>
    [Fact]
    public Task CommandDescribedThroughDescribe_ProducesNoDiagnostic()
    {
        const string source = """
                              using LiteBus.Commands.Abstractions;
                              using LiteBus.Messaging.Abstractions;

                              public sealed record ShipOrderCommand : ICommand;

                              public sealed class ShipOrderCommandDefinition : IMessageDefinition<ShipOrderCommand>
                              {
                                  public void Describe(IMessageDeclarations declarations)
                                  {
                                      declarations.Audited("orders.ship-order", category: "lifecycle");
                                  }
                              }
                              """;

        return AnalyzerTest.VerifyNoDiagnosticsAsync<MissingAuditDeclarationAnalyzer>(source);
    }

    /// <summary>
    ///     Verifies that <c>NotAudited</c> answers the rule the same way <c>Audited</c> does.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    /// <remarks>
    ///     Both key their declaration by <c>AuditDeclaration</c>, because both state an audit position. The rule asks
    ///     whether the message answered the question, not which answer it gave.
    /// </remarks>
    [Fact]
    public Task CommandDescribedAsNotAudited_ProducesNoDiagnostic()
    {
        const string source = """
                              using LiteBus.Commands.Abstractions;
                              using LiteBus.Messaging.Abstractions;

                              public sealed record PingCommand : ICommand;

                              public sealed class PingCommandDefinition : IMessageDefinition<PingCommand>
                              {
                                  public void Describe(IMessageDeclarations declarations)
                                  {
                                      declarations.NotAudited("a liveness probe touches no data");
                                  }
                              }
                              """;

        return AnalyzerTest.VerifyNoDiagnosticsAsync<MissingAuditDeclarationAnalyzer>(source);
    }

    /// <summary>
    ///     Verifies that a definition written for a marker covers the family through <c>Describe</c> too.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task DescribeOnAMarkerInterface_CoversTheFamily()
    {
        const string source = """
                              using LiteBus.Commands.Abstractions;
                              using LiteBus.Messaging.Abstractions;

                              public interface IBillingCommand;

                              public sealed record ChargeCardCommand : ICommand, IBillingCommand;

                              public sealed class BillingCommandDefinition : IMessageDefinition<IBillingCommand>
                              {
                                  public void Describe(IMessageDeclarations declarations)
                                  {
                                      declarations.Audited("billing.command");
                                  }
                              }
                              """;

        return AnalyzerTest.VerifyNoDiagnosticsAsync<MissingAuditDeclarationAnalyzer>(source);
    }

    /// <summary>
    ///     Verifies that a body handing the collector to a helper is treated as covering the message.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    /// <remarks>
    ///     What the helper declares is out of the analyzer's sight. Reporting the message would fail a build over a
    ///     declaration that is there, and the composition check still enforces the rule at startup either way, so the
    ///     unreadable case resolves in favour of the build.
    /// </remarks>
    [Fact]
    public Task DescribeThatDelegatesToAHelper_ProducesNoDiagnostic()
    {
        const string source = """
                              using LiteBus.Commands.Abstractions;
                              using LiteBus.Messaging.Abstractions;

                              public sealed record ArchiveOrderCommand : ICommand;

                              public static class BillingConventions
                              {
                                  public static void Apply(IMessageDeclarations declarations)
                                  {
                                      declarations.Audited("orders.archive-order");
                                  }
                              }

                              public sealed class ArchiveOrderCommandDefinition : IMessageDefinition<ArchiveOrderCommand>
                              {
                                  public void Describe(IMessageDeclarations declarations)
                                  {
                                      BillingConventions.Apply(declarations);
                                  }
                              }
                              """;

        return AnalyzerTest.VerifyNoDiagnosticsAsync<MissingAuditDeclarationAnalyzer>(source);
    }

    /// <summary>
    ///     Verifies that a <c>Describe</c> body declaring something else still produces LB1018.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    /// <remarks>
    ///     The point of reading the body rather than accepting the contract: a definition that exists is not evidence
    ///     that it states an audit position.
    /// </remarks>
    [Fact]
    public Task DescribeThatDeclaresSomethingElse_ProducesDiagnostic()
    {
        const string source = """
                              using LiteBus.Commands.Abstractions;
                              using LiteBus.Messaging.Abstractions;

                              public sealed record RequiredPermission(string Name);

                              public sealed record {|#0:RefundOrderCommand|} : ICommand;

                              public sealed class RefundOrderCommandDefinition : IMessageDefinition<RefundOrderCommand>
                              {
                                  public void Describe(IMessageDeclarations declarations)
                                  {
                                      declarations.Declare(new RequiredPermission("orders.refund"));
                                  }
                              }
                              """;

        return AnalyzerTest.VerifyDiagnosticAsync<MissingAuditDeclarationAnalyzer>(
            source,
            DiagnosticDescriptors.MissingAuditDeclaration,
            0,
            "RefundOrderCommand");
    }
}
