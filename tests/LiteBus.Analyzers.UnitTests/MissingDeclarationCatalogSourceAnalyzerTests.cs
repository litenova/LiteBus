namespace LiteBus.Analyzers.UnitTests;

/// <summary>
///     Tests that <see cref="MissingDeclarationAnalyzer" /> and <see cref="MissingAuditDeclarationAnalyzer" /> accept
///     declarations contributed through a multi-message declaration source.
/// </summary>
public sealed class MissingDeclarationCatalogSourceAnalyzerTests
{
    /// <summary>
    ///     An <c>.editorconfig</c> requiring the application's own declaration.
    /// </summary>
    private const string RequirePermission = """
                                             is_global = true
                                             litebus_required_declarations = App.RequiredPermission
                                             """;

    /// <summary>
    ///     The using directives every case shares, kept first so appended source stays compilable.
    /// </summary>
    private const string Header = """
                                  using App;
                                  using LiteBus.Commands.Abstractions;
                                  using LiteBus.Messaging.Abstractions;
                                  using LiteBus.Auditing.Abstractions;
                                  using LiteBus.Queries.Abstractions;

                                  """;

    /// <summary>
    ///     The application-side declaration types every case shares, appended after the message types.
    /// </summary>
    private const string Declarations = """

                                        namespace App
                                        {
                                            public sealed record RequiredPermission(string Name);
                                        }
                                        """;

    /// <summary>
    ///     Builds a full compilation from the shared header, the case's message types, and the shared declarations.
    /// </summary>
    /// <param name="body">The message and source types under test.</param>
    /// <returns>The source to analyze.</returns>
    private static string Source(string body)
    {
        return Header + body + Declarations;
    }

    /// <summary>
    ///     Verifies that a command declared through a catalog source passes.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task SourceDeclaredCommand_ProducesNoDiagnostic()
    {
        var source = Source("""
                            public sealed record ShipOrderCommand : ICommand;

                            public sealed class BillingDeclarations : IMultiMessageDefinition
                            {
                                public void Describe(IMessageDeclarationCatalog catalog)
                                {
                                    catalog.Describe<ShipOrderCommand>(declarations => declarations.Declare(new RequiredPermission("orders.ship")));
                                }
                            }
                            """);

        return AnalyzerTest.VerifyWithEditorConfigAsync<MissingDeclarationAnalyzer>(source, RequirePermission);
    }

    /// <summary>
    ///     Verifies that a command declared through a catalog <c>Replace</c> passes.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task SourceReplacedCommand_ProducesNoDiagnostic()
    {
        var source = Source("""
                            public sealed record ShipOrderCommand : ICommand;

                            public sealed class BillingDeclarations : IMultiMessageDefinition
                            {
                                public void Describe(IMessageDeclarationCatalog catalog)
                                {
                                    catalog.Replace<ShipOrderCommand>(declarations => declarations.Declare(new RequiredPermission("orders.ship")));
                                }
                            }
                            """);

        return AnalyzerTest.VerifyWithEditorConfigAsync<MissingDeclarationAnalyzer>(source, RequirePermission);
    }

    /// <summary>
    ///     Verifies that a command declared through the type-valued <c>Describe</c> overload passes.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task SourceDeclaredByType_ProducesNoDiagnostic()
    {
        var source = Source("""
                            public sealed record ShipOrderCommand : ICommand;

                            public sealed class BillingDeclarations : IMultiMessageDefinition
                            {
                                public void Describe(IMessageDeclarationCatalog catalog)
                                {
                                    catalog.Describe(typeof(ShipOrderCommand), declarations => declarations.Declare(new RequiredPermission("orders.ship")));
                                }
                            }
                            """);

        return AnalyzerTest.VerifyWithEditorConfigAsync<MissingDeclarationAnalyzer>(source, RequirePermission);
    }

    /// <summary>
    ///     Verifies that an exemption recorded through a catalog source passes.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task SourceExemptedCommand_ProducesNoDiagnostic()
    {
        var source = Source("""
                            public sealed record PingCommand : ICommand;

                            public sealed class BillingDeclarations : IMultiMessageDefinition
                            {
                                public void Describe(IMessageDeclarationCatalog catalog)
                                {
                                    catalog.Describe<PingCommand>(declarations => declarations.Exempt<RequiredPermission>("a liveness probe needs no permission"));
                                }
                            }
                            """);

        return AnalyzerTest.VerifyWithEditorConfigAsync<MissingDeclarationAnalyzer>(source, RequirePermission);
    }

    /// <summary>
    ///     Verifies that a source declaring one message does not cover another.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task SourceForAnotherMessage_StillProducesDiagnostic()
    {
        var source = Source("""
                            public sealed record ShipOrderCommand : ICommand;

                            public sealed record {|#0:RefundCommand|} : ICommand;

                            public sealed class BillingDeclarations : IMultiMessageDefinition
                            {
                                public void Describe(IMessageDeclarationCatalog catalog)
                                {
                                    catalog.Describe<ShipOrderCommand>(declarations => declarations.Declare(new RequiredPermission("orders.ship")));
                                }
                            }
                            """);

        return AnalyzerTest.VerifyWithEditorConfigAsync<MissingDeclarationAnalyzer>(
            source,
            RequirePermission,
            (DiagnosticDescriptors.MissingDeclaration, 0, ["RefundCommand", "RequiredPermission"]));
    }

    /// <summary>
    ///     Verifies that a source declaration for a marker interface covers the family beneath it.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task SourceDeclarationOnAMarkerInterface_CoversTheFamily()
    {
        var source = Source("""
                            public interface IPublicCommand;

                            public sealed record PingCommand : ICommand, IPublicCommand;

                            public sealed class BillingDeclarations : IMultiMessageDefinition
                            {
                                public void Describe(IMessageDeclarationCatalog catalog)
                                {
                                    catalog.Describe<IPublicCommand>(declarations => declarations.Declare(new RequiredPermission("public")));
                                }
                            }
                            """);

        return AnalyzerTest.VerifyWithEditorConfigAsync<MissingDeclarationAnalyzer>(source, RequirePermission);
    }

    /// <summary>
    ///     Verifies that an audit position declared through a catalog source satisfies the audit rule.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task SourceAuditedCommand_ProducesNoAuditDiagnostic()
    {
        var source = Source("""
                            public sealed record ShipOrderCommand : ICommand;

                            public sealed class BillingDeclarations : IMultiMessageDefinition
                            {
                                public void Describe(IMessageDeclarationCatalog catalog)
                                {
                                    catalog.Describe<ShipOrderCommand>(declarations => declarations.Audited("orders.ship-order"));
                                }
                            }
                            """);

        return AnalyzerTest.VerifyNoDiagnosticsAsync<MissingAuditDeclarationAnalyzer>(source);
    }
}
