using Microsoft.CodeAnalysis;

namespace LiteBus.Analyzers.UnitTests;

/// <summary>
///     Tests for the <see cref="DefinitionMixesConcernsAnalyzer" /> rule.
/// </summary>
public sealed class DefinitionMixesConcernsAnalyzerTests
{
    /// <summary>
    ///     Verifies that a definition class declaring one concern produces no diagnostic.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task SingleConcern_ProducesNoDiagnostic()
    {
        const string source = """
                              using LiteBus.Commands.Abstractions;
                              using LiteBus.Messaging.Abstractions;

                              public sealed record PlaceOrderCommand : ICommand;

                              public sealed class PlaceOrderCommandAuthorizationDefinition : IAuthorizationDefinition<PlaceOrderCommand>
                              {
                                  public AuthorizationDeclaration Authorization => AuthorizationDeclaration.Exempt("test");
                              }
                              """;

        return AnalyzerTest.VerifyNoDiagnosticsAsync<DefinitionMixesConcernsAnalyzer>(source);
    }

    /// <summary>
    ///     Verifies that a definition class declaring authorization and audit produces LB1028.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task TwoConcerns_ProducesDiagnostic()
    {
        const string source = """
                              using LiteBus.Commands.Abstractions;
                              using LiteBus.Messaging.Abstractions;
                              using LiteBus.Messaging.Abstractions.Auditing;

                              public sealed record PlaceOrderCommand : ICommand;

                              public sealed class {|#0:PlaceOrderCommandDefinition|} : IAuthorizationDefinition<PlaceOrderCommand>, IAuditDefinition<PlaceOrderCommand>
                              {
                                  public AuthorizationDeclaration Authorization => AuthorizationDeclaration.Exempt("test");

                                  public AuditDeclaration Audit => AuditDeclaration.Exempt("test");
                              }
                              """;

        return AnalyzerTest.VerifyDiagnosticAsync<DefinitionMixesConcernsAnalyzer>(
            source,
            DiagnosticDescriptors.DefinitionMixesConcerns,
            0,
            "PlaceOrderCommandDefinition",
            "authorization and audit",
            "PlaceOrderCommandAuthorizationDefinition");
    }

    /// <summary>
    ///     Verifies that a definition class declaring three concerns lists every concern in the diagnostic.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task ThreeConcerns_ProducesDiagnostic()
    {
        const string source = """
                              using LiteBus.Commands.Abstractions;
                              using LiteBus.Messaging.Abstractions;
                              using LiteBus.Messaging.Abstractions.Auditing;
                              using LiteBus.Messaging.Abstractions.Tenancy;

                              public sealed record PlaceOrderCommand : ICommand;

                              public sealed class {|#0:PlaceOrderCommandDefinition|}
                                  : IAuditDefinition<PlaceOrderCommand>,
                                  ITenancyDefinition<PlaceOrderCommand>,
                                  IAuthorizationDefinition<PlaceOrderCommand>
                              {
                                  public AuditDeclaration Audit => AuditDeclaration.Exempt("test");

                                  public TenancyDeclaration Tenancy => TenancyDeclaration.Required();

                                  public AuthorizationDeclaration Authorization => AuthorizationDeclaration.Exempt("test");
                              }
                              """;

        return AnalyzerTest.VerifyDiagnosticAsync<DefinitionMixesConcernsAnalyzer>(
            source,
            DiagnosticDescriptors.DefinitionMixesConcerns,
            0,
            "PlaceOrderCommandDefinition",
            "authorization, audit, and tenancy",
            "PlaceOrderCommandAuthorizationDefinition");
    }

    /// <summary>
    ///     Verifies that one concern declared for two messages produces no diagnostic.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task SameConcernForDifferentMessages_ProducesNoDiagnostic()
    {
        const string source = """
                              using LiteBus.Commands.Abstractions;
                              using LiteBus.Messaging.Abstractions.Auditing;

                              public sealed record CreateUserCommand : ICommand;

                              public sealed record DeleteUserCommand : ICommand;

                              public sealed class UserAuditDefinition : IAuditDefinition<CreateUserCommand>, IAuditDefinition<DeleteUserCommand>
                              {
                                  public AuditDeclaration Audit => AuditDeclaration.Exempt("test");
                              }
                              """;

        return AnalyzerTest.VerifyNoDiagnosticsAsync<DefinitionMixesConcernsAnalyzer>(source);
    }

    /// <summary>
    ///     Verifies that one value declared through both its named contract and the open generic contract produces no
    ///     diagnostic.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task SameValueThroughNamedAndDirectContracts_ProducesNoDiagnostic()
    {
        const string source = """
                              using LiteBus.Commands.Abstractions;
                              using LiteBus.Messaging.Abstractions;
                              using LiteBus.Messaging.Abstractions.Auditing;

                              public sealed record PlaceOrderCommand : ICommand;

                              public sealed class PlaceOrderCommandAuditDefinition
                                  : IAuditDefinition<PlaceOrderCommand>, IMessageDefinition<PlaceOrderCommand, AuditDeclaration>
                              {
                                  public AuditDeclaration Audit => AuditDeclaration.Exempt("test");
                              }
                              """;

        return AnalyzerTest.VerifyNoDiagnosticsAsync<DefinitionMixesConcernsAnalyzer>(source);
    }

    /// <summary>
    ///     Verifies that a definition class mixing a named concern with a directly implemented custom value produces
    ///     LB1028.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task DirectCustomValueMix_ProducesDiagnostic()
    {
        const string source = """
                              using LiteBus.Commands.Abstractions;
                              using LiteBus.Messaging.Abstractions;
                              using LiteBus.Messaging.Abstractions.Auditing;

                              public sealed record PlaceOrderCommand : ICommand;

                              public sealed record RequiredPermission(string Name);

                              public sealed class {|#0:PlaceOrderCommandDefinition|}
                                  : IAuditDefinition<PlaceOrderCommand>, IMessageDefinition<PlaceOrderCommand, RequiredPermission>
                              {
                                  public AuditDeclaration Audit => AuditDeclaration.Exempt("test");

                                  public RequiredPermission Value => new("orders.place");
                              }
                              """;

        return AnalyzerTest.VerifyDiagnosticAsync<DefinitionMixesConcernsAnalyzer>(
            source,
            DiagnosticDescriptors.DefinitionMixesConcerns,
            0,
            "PlaceOrderCommandDefinition",
            "audit and RequiredPermission",
            "PlaceOrderCommandAuditDefinition");
    }

    /// <summary>
    ///     Verifies that a class implementing no definition contract produces no diagnostic.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task PlainClass_ProducesNoDiagnostic()
    {
        const string source = """
                              using LiteBus.Commands.Abstractions;

                              public sealed record PlaceOrderCommand : ICommand;

                              public sealed class OrderTotals
                              {
                                  public decimal Total { get; init; }
                              }
                              """;

        return AnalyzerTest.VerifyNoDiagnosticsAsync<DefinitionMixesConcernsAnalyzer>(source);
    }

    /// <summary>
    ///     Verifies that the rule is an informational note in <c>LiteBus.Declarations</c> that stays on by default.
    /// </summary>
    [Fact]
    public void Descriptor_IsInfoInDeclarationsCategoryAndEnabledByDefault()
    {
        Assert.Equal("LiteBus.Declarations", DiagnosticDescriptors.DefinitionMixesConcerns.Category);
        Assert.Equal(DiagnosticSeverity.Info, DiagnosticDescriptors.DefinitionMixesConcerns.DefaultSeverity);
        Assert.True(DiagnosticDescriptors.DefinitionMixesConcerns.IsEnabledByDefault);
    }
}
