namespace LiteBus.Analyzers.UnitTests;

/// <summary>
///     Tests for the <see cref="MissingDeclarationAnalyzer" /> rule, the general form of LB1018.
/// </summary>
public sealed class MissingDeclarationAnalyzerTests
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
                                  using LiteBus.Queries.Abstractions;

                                  """;

    /// <summary>
    ///     The application-side declaration types every case shares, appended after the message types.
    /// </summary>
    private const string Declarations = """

                                        namespace App
                                        {
                                            public sealed record RequiredPermission(string Name);

                                            [MessageDeclaration(typeof(RequiredPermission))]
                                            [System.AttributeUsage(System.AttributeTargets.Class, Inherited = false)]
                                            public sealed class RequiresPermissionAttribute : System.Attribute, IMessageDeclarationSource
                                            {
                                                public RequiresPermissionAttribute(string permission) => Permission = permission;

                                                public string Permission { get; }

                                                public System.Type DeclarationType => typeof(RequiredPermission);

                                                public object CreateDeclaration() => new RequiredPermission(Permission);
                                            }
                                        }
                                        """;

    /// <summary>
    ///     Builds a full compilation from the shared header, the case's message types, and the shared declarations.
    /// </summary>
    /// <param name="body">The message and definition types under test.</param>
    /// <returns>The source to analyze.</returns>
    private static string Source(string body)
    {
        return Header + body + Declarations;
    }

    /// <summary>
    ///     Verifies that no configuration means no diagnostics, so the rule is inert until asked for.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task WithoutConfiguration_ProducesNoDiagnostic()
    {
        var source = Source("public sealed record CreateUserCommand(string Name) : ICommand;");

        return AnalyzerTest.VerifyNoDiagnosticsAsync<MissingDeclarationAnalyzer>(source);
    }

    /// <summary>
    ///     Verifies that a command declaring the required value through an annotated attribute passes.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task AttributeDeclaredCommand_ProducesNoDiagnostic()
    {
        var source = Source("""
                            [RequiresPermission("users.create")]
                            public sealed record CreateUserCommand(string Name) : ICommand;
                            """);

        return AnalyzerTest.VerifyWithEditorConfigAsync<MissingDeclarationAnalyzer>(source, RequirePermission);
    }

    /// <summary>
    ///     Verifies that a definition class declaring the required value passes.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task DefinitionDeclaredCommand_ProducesNoDiagnostic()
    {
        var source = Source("""
                            public sealed record ShipOrderCommand : ICommand;

                            public sealed class ShipOrderCommandDefinition
                                : IMessageDefinition<ShipOrderCommand, RequiredPermission>
                            {
                                public RequiredPermission Value => new("orders.ship");
                            }
                            """);

        return AnalyzerTest.VerifyWithEditorConfigAsync<MissingDeclarationAnalyzer>(source, RequirePermission);
    }

    /// <summary>
    ///     Verifies that a declaration written for a marker interface covers the messages beneath it.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task DeclarationOnAMarkerInterface_CoversTheFamily()
    {
        var source = Source("""
                            public interface IPublicCommand;

                            public sealed record PingCommand : ICommand, IPublicCommand;

                            public sealed class PublicCommandDefinition
                                : IMessageDefinition<IPublicCommand, RequiredPermission>
                            {
                                public RequiredPermission Value => new("public");
                            }
                            """);

        return AnalyzerTest.VerifyWithEditorConfigAsync<MissingDeclarationAnalyzer>(source, RequirePermission);
    }

    /// <summary>
    ///     Verifies that a recorded exemption satisfies the requirement.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task ExemptCommand_ProducesNoDiagnostic()
    {
        var source = Source("""
                            [DeclarationExempt(typeof(RequiredPermission), "the storefront is public")]
                            public sealed record BrowseStorefrontCommand : ICommand;
                            """);

        return AnalyzerTest.VerifyWithEditorConfigAsync<MissingDeclarationAnalyzer>(source, RequirePermission);
    }

    /// <summary>
    ///     Verifies that an exemption for a different value type does not satisfy this requirement.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task ExemptionForAnotherValue_StillProducesDiagnostic()
    {
        var source = Source("""
                            [DeclarationExempt(typeof(AuditDeclaration), "not a sensitive action")]
                            public sealed record {|#0:BrowseStorefrontCommand|} : ICommand;
                            """);

        return AnalyzerTest.VerifyWithEditorConfigAsync<MissingDeclarationAnalyzer>(
            source,
            RequirePermission,
            (DiagnosticDescriptors.MissingDeclaration, 0, ["BrowseStorefrontCommand", "RequiredPermission"]));
    }

    /// <summary>
    ///     Verifies that a command stating no position produces LB1020 naming the message and the value type.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task UndeclaredCommand_ProducesDiagnostic()
    {
        var source = Source("public sealed record {|#0:CreateUserCommand|}(string Name) : ICommand;");

        return AnalyzerTest.VerifyWithEditorConfigAsync<MissingDeclarationAnalyzer>(
            source,
            RequirePermission,
            (DiagnosticDescriptors.MissingDeclaration, 0, ["CreateUserCommand", "RequiredPermission"]));
    }

    /// <summary>
    ///     Verifies that a query is held to the same requirement as a command.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task UndeclaredQuery_ProducesDiagnostic()
    {
        var source = Source("public sealed record {|#0:ReadUserQuery|} : IQuery<string>;");

        return AnalyzerTest.VerifyWithEditorConfigAsync<MissingDeclarationAnalyzer>(
            source,
            RequirePermission,
            (DiagnosticDescriptors.MissingDeclaration, 0, ["ReadUserQuery", "RequiredPermission"]));
    }

    /// <summary>
    ///     Verifies that a name which does not resolve is reported rather than silently disabling the requirement.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task UnresolvableRequiredType_ProducesConfigurationDiagnostic()
    {
        const string source = """
                              using LiteBus.Commands.Abstractions;

                              public sealed record CreateUserCommand(string Name) : ICommand;
                              """;

        const string editorConfig = """
                                    is_global = true
                                    litebus_required_declarations = App.TypoedPermision
                                    """;

        return AnalyzerTest.VerifyUnlocatedWithEditorConfigAsync<MissingDeclarationAnalyzer>(
            source,
            editorConfig,
            DiagnosticDescriptors.UnresolvedRequiredDeclaration,
            "App.TypoedPermision");
    }
}
