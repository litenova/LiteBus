namespace LiteBus.Analyzers.UnitTests;

/// <summary>
///     Tests that <see cref="MissingDeclarationAnalyzer" /> (LB1020) configured for
///     <c>AuthorizationDeclaration</c> sees every shape an authorization position can take.
/// </summary>
public sealed class AuthorizationDeclarationAnalyzerTests
{
    /// <summary>
    ///     An <c>.editorconfig</c> requiring the authorization declaration of every message.
    /// </summary>
    private const string RequireAuthorization = """
                                                is_global = true
                                                litebus_required_declarations = LiteBus.Messaging.Abstractions.AuthorizationDeclaration
                                                """;

    /// <summary>
    ///     The using directives every case shares, kept first so appended source stays compilable.
    /// </summary>
    private const string Header = """
                                  using LiteBus.Commands.Abstractions;
                                  using LiteBus.Messaging.Abstractions;

                                  """;

    /// <summary>
    ///     Verifies that a command stating no authorization position produces LB1020 naming the message and the value.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task UndeclaredCommand_ProducesDiagnostic()
    {
        const string source = Header + "public sealed record {|#0:CreateUserCommand|}(string Name) : ICommand;";

        return AnalyzerTest.VerifyWithEditorConfigAsync<MissingDeclarationAnalyzer>(
            source,
            RequireAuthorization,
            (DiagnosticDescriptors.MissingDeclaration, 0, ["CreateUserCommand", "AuthorizationDeclaration"]));
    }

    /// <summary>
    ///     Verifies that a command carrying <c>[Authorized]</c> produces no diagnostic.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task AuthorizedAttribute_ProducesNoDiagnostic()
    {
        const string source = Header + """
                                        [Authorized("users.create")]
                                        public sealed record CreateUserCommand(string Name) : ICommand;
                                        """;

        return AnalyzerTest.VerifyWithEditorConfigAsync<MissingDeclarationAnalyzer>(source, RequireAuthorization);
    }

    /// <summary>
    ///     Verifies that a command carrying <c>[AuthorizationExempt]</c> produces no diagnostic.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task AuthorizationExemptAttribute_ProducesNoDiagnostic()
    {
        const string source = Header + """
                                        [AuthorizationExempt("the storefront is public")]
                                        public sealed record BrowseStorefrontCommand : ICommand;
                                        """;

        return AnalyzerTest.VerifyWithEditorConfigAsync<MissingDeclarationAnalyzer>(source, RequireAuthorization);
    }

    /// <summary>
    ///     Verifies that a command covered by a keyed authorization definition produces no diagnostic.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task KeyedAuthorizationDefinition_ProducesNoDiagnostic()
    {
        const string source = Header + """
                                        public sealed record ShipOrderCommand : ICommand;

                                        public sealed class ShipOrderCommandDefinition : IAuthorizationDefinition<ShipOrderCommand>
                                        {
                                            public AuthorizationDeclaration Authorization => AuthorizationDeclaration.Authorized("orders.ship");
                                        }
                                        """;

        return AnalyzerTest.VerifyWithEditorConfigAsync<MissingDeclarationAnalyzer>(source, RequireAuthorization);
    }

    /// <summary>
    ///     Verifies that a definition declaring the position through <c>Describe</c> with <c>Authorized</c> passes.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task DescribeAuthorized_ProducesNoDiagnostic()
    {
        const string source = Header + """
                                        public sealed record ShipOrderCommand : ICommand;

                                        public sealed class ShipOrderCommandDefinition : IMessageDefinition<ShipOrderCommand>
                                        {
                                            public void Describe(IMessageDeclarations declarations)
                                            {
                                                declarations.Authorized("orders.ship");
                                            }
                                        }
                                        """;

        return AnalyzerTest.VerifyWithEditorConfigAsync<MissingDeclarationAnalyzer>(source, RequireAuthorization);
    }

    /// <summary>
    ///     Verifies that <c>AuthorizationExempt</c> answers the rule the same way <c>Authorized</c> does.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    /// <remarks>
    ///     Both key their declaration by <c>AuthorizationDeclaration</c>, because both state an authorization position.
    ///     The rule asks whether the message answered the question, not which answer it gave.
    /// </remarks>
    [Fact]
    public Task DescribeAuthorizationExempt_ProducesNoDiagnostic()
    {
        const string source = Header + """
                                        public sealed record PingCommand : ICommand;

                                        public sealed class PingCommandDefinition : IMessageDefinition<PingCommand>
                                        {
                                            public void Describe(IMessageDeclarations declarations)
                                            {
                                                declarations.AuthorizationExempt("a liveness probe has no actor to authorize");
                                            }
                                        }
                                        """;

        return AnalyzerTest.VerifyWithEditorConfigAsync<MissingDeclarationAnalyzer>(source, RequireAuthorization);
    }

    /// <summary>
    ///     Verifies that a <c>Describe</c> body declaring something else still produces LB1020.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    /// <remarks>
    ///     The point of reading the body rather than accepting the contract: a definition that exists is not evidence
    ///     that it states an authorization position.
    /// </remarks>
    [Fact]
    public Task DescribeThatDeclaresSomethingElse_ProducesDiagnostic()
    {
        const string source = Header + """
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

        return AnalyzerTest.VerifyWithEditorConfigAsync<MissingDeclarationAnalyzer>(
            source,
            RequireAuthorization,
            (DiagnosticDescriptors.MissingDeclaration, 0, ["RefundOrderCommand", "AuthorizationDeclaration"]));
    }
}
