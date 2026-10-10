using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace LiteBus.Analyzers.UnitTests;

/// <summary>
///     Compilation-end aggregation tests for the declaration analyzers.
/// </summary>
public sealed class DeclarationCompilationEndTests
{
    /// <summary>
    ///     The configured required declaration for the LB1020 cases.
    /// </summary>
    private const string RequiredPermissionName = "App.RequiredPermission";

    /// <summary>
    ///     Verifies that LB1018 still fires when the message and the <c>Describe</c> body live in different files.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    /// <remarks>
    ///     The body declares something other than the audit position. An unreadable body counts as declared, so the
    ///     diagnostic proves the body in the other file was actually read.
    /// </remarks>
    [Fact]
    public Task AuditDeclaration_DiagnosticReportedAcrossTwoFiles()
    {
        IReadOnlyList<string> sources =
        [
            """
            using LiteBus.Commands.Abstractions;

            public sealed record {|#0:CreateUserCommand|}(string Name) : ICommand;
            """,
            """
            using LiteBus.Commands.Abstractions;
            using LiteBus.Messaging.Abstractions;

            public sealed record RequiredPermission(string Name);

            public sealed class CreateUserCommandDefinition : IMessageDefinition<CreateUserCommand>
            {
                public void Describe(IMessageDeclarations declarations)
                {
                    declarations.Declare(new RequiredPermission("users.create"));
                }
            }
            """
        ];

        return AnalyzerTest.VerifyMultiFileAsync<MissingAuditDeclarationAnalyzer>(
            sources,
            (DiagnosticDescriptors.MissingAuditDeclaration, 0, ["CreateUserCommand"]));
    }

    /// <summary>
    ///     Verifies that an audit definition in another file covers the message.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public Task AuditDeclaration_DefinitionInAnotherFileCoversTheMessage()
    {
        IReadOnlyList<string> sources =
        [
            """
            using LiteBus.Commands.Abstractions;

            public sealed record CreateUserCommand(string Name) : ICommand;
            """,
            """
            using LiteBus.Commands.Abstractions;
            using LiteBus.Messaging.Abstractions;

            public sealed class CreateUserCommandDefinition : IMessageDefinition<CreateUserCommand>
            {
                public void Describe(IMessageDeclarations declarations)
                {
                    declarations.Audited("users.create-user");
                }
            }
            """
        ];

        return AnalyzerTest.VerifyMultiFileAsync<MissingAuditDeclarationAnalyzer>(sources);
    }

    /// <summary>
    ///     Verifies that per-file analysis reports no LB1018 while full analysis of the same sources does.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public async Task AuditDeclaration_PerFileReportsNothingWhileFullAnalysisReports()
    {
        IReadOnlyList<string> plain =
        [
            """
            using LiteBus.Commands.Abstractions;

            public sealed record CreateUserCommand(string Name) : ICommand;
            """
        ];
        IReadOnlyList<string> marked =
        [
            """
            using LiteBus.Commands.Abstractions;

            public sealed record {|#0:CreateUserCommand|}(string Name) : ICommand;
            """
        ];

        var compilation = await AnalyzerTest.CreateCompilationAsync(plain).ConfigureAwait(false);
        var perFile = await AnalyzerTest
            .GetPerFileDiagnosticsAsync(compilation, 0, new MissingAuditDeclarationAnalyzer())
            .ConfigureAwait(false);

        Assert.DoesNotContain(perFile, diagnostic => diagnostic.Id == DiagnosticIds.MissingAuditDeclaration);

        await AnalyzerTest
            .VerifyMultiFileAsync<MissingAuditDeclarationAnalyzer>(
                marked,
                (DiagnosticDescriptors.MissingAuditDeclaration, 0, ["CreateUserCommand"]))
            .ConfigureAwait(false);
    }

    /// <summary>
    ///     Verifies that LB1020 still fires when the message and an unrelated definition live in different files.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public async Task RequiredDeclaration_DiagnosticReportedAcrossTwoFiles()
    {
        IReadOnlyList<string> sources =
        [
            """
            using LiteBus.Commands.Abstractions;

            public sealed record CreateUserCommand(string Name) : ICommand;
            """,
            """
            using LiteBus.Commands.Abstractions;
            using LiteBus.Messaging.Abstractions;

            namespace App
            {
                public sealed record RequiredPermission(string Name);
            }

            public sealed record ShipOrderCommand : ICommand;

            public sealed class ShipOrderCommandDefinition : IMessageDefinition<ShipOrderCommand, App.RequiredPermission>
            {
                public App.RequiredPermission Value => new("orders.ship");
            }
            """
        ];

        var diagnostics = await AnalyzeFullWithGlobalConfigAsync(sources).ConfigureAwait(false);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == DiagnosticIds.UnresolvedRequiredDeclaration);

        var missing = diagnostics.Where(diagnostic => diagnostic.Id == DiagnosticIds.MissingDeclaration).ToList();
        var lone = Assert.Single(missing);

        Assert.True(lone.Location.IsInSource, "Expected the diagnostic to carry a source location.");

        var tree = lone.Location.SourceTree!;
        Assert.EndsWith("Test0.cs", tree.FilePath, StringComparison.Ordinal);
        Assert.Equal("CreateUserCommand", tree.GetText().GetSubText(lone.Location.SourceSpan).ToString());
    }

    /// <summary>
    ///     Verifies that a required declaration definition in another file covers the message.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public async Task RequiredDeclaration_DefinitionInAnotherFileCoversTheMessage()
    {
        IReadOnlyList<string> sources =
        [
            """
            using LiteBus.Commands.Abstractions;

            public sealed record CreateUserCommand(string Name) : ICommand;
            """,
            """
            using LiteBus.Commands.Abstractions;
            using LiteBus.Messaging.Abstractions;

            namespace App
            {
                public sealed record RequiredPermission(string Name);
            }

            public sealed class CreateUserCommandDefinition : IMessageDefinition<CreateUserCommand, App.RequiredPermission>
            {
                public App.RequiredPermission Value => new("users.create");
            }
            """
        ];

        var diagnostics = await AnalyzeFullWithGlobalConfigAsync(sources).ConfigureAwait(false);

        Assert.Empty(diagnostics);
    }

    /// <summary>
    ///     Verifies that per-file analysis reports no LB1020 while full analysis of the same sources does.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public async Task RequiredDeclaration_PerFileReportsNothingWhileFullAnalysisReports()
    {
        IReadOnlyList<string> sources =
        [
            """
            using LiteBus.Commands.Abstractions;

            public sealed record CreateUserCommand(string Name) : ICommand;
            """,
            """
            namespace App
            {
                public sealed record RequiredPermission(string Name);
            }
            """
        ];

        var compilation = await AnalyzerTest.CreateCompilationAsync(sources).ConfigureAwait(false);
        var perFile = await AnalyzerTest
            .GetPerFileDiagnosticsAsync(compilation, 0, new MissingDeclarationAnalyzer())
            .ConfigureAwait(false);

        Assert.DoesNotContain(perFile, diagnostic => diagnostic.Id == DiagnosticIds.MissingDeclaration);

        var diagnostics = await AnalyzeFullWithGlobalConfigAsync(sources).ConfigureAwait(false);
        var missing = diagnostics.Where(diagnostic => diagnostic.Id == DiagnosticIds.MissingDeclaration).ToList();
        var lone = Assert.Single(missing);

        Assert.True(lone.Location.IsInSource, "Expected the diagnostic to carry a source location.");
        Assert.EndsWith("Test0.cs", lone.Location.SourceTree!.FilePath, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Verifies that a per-tree configuration still applies and is deduplicated across trees.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    /// <remarks>
    ///     The same name is configured for every tree, the way an <c>.editorconfig</c> entry under
    ///     <c>[*.cs]</c> reaches each syntax tree. Deduplication keeps the message reported once, not once per tree.
    /// </remarks>
    [Fact]
    public async Task RequiredDeclaration_PerTreeConfigurationStillApplies()
    {
        IReadOnlyList<string> sources =
        [
            """
            using LiteBus.Commands.Abstractions;

            public sealed record CreateUserCommand(string Name) : ICommand;
            """,
            """
            namespace App
            {
                public sealed record RequiredPermission(string Name);
            }
            """
        ];

        var diagnostics = await AnalyzeFullWithTreeConfigAsync(sources).ConfigureAwait(false);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == DiagnosticIds.UnresolvedRequiredDeclaration);

        var missing = diagnostics.Where(diagnostic => diagnostic.Id == DiagnosticIds.MissingDeclaration).ToList();
        var lone = Assert.Single(missing);

        Assert.True(lone.Location.IsInSource, "Expected the diagnostic to carry a source location.");
        Assert.EndsWith("Test0.cs", lone.Location.SourceTree!.FilePath, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Runs full analysis, compilation end included, with the requirement configured globally.
    /// </summary>
    /// <param name="sources">The source files, without markup.</param>
    /// <returns>The analyzer diagnostics.</returns>
    private static Task<ImmutableArray<Diagnostic>> AnalyzeFullWithGlobalConfigAsync(IReadOnlyList<string> sources)
    {
        return AnalyzeFullAsync(
            sources,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["litebus_required_declarations"] = RequiredPermissionName
            },
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
    ///     Runs full analysis, compilation end included, with the requirement configured per tree.
    /// </summary>
    /// <param name="sources">The source files, without markup.</param>
    /// <returns>The analyzer diagnostics.</returns>
    private static Task<ImmutableArray<Diagnostic>> AnalyzeFullWithTreeConfigAsync(IReadOnlyList<string> sources)
    {
        return AnalyzeFullAsync(
            sources,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["litebus_required_declarations"] = RequiredPermissionName
            });
    }

    /// <summary>
    ///     Runs full analysis, compilation end included, with the given analyzer configuration.
    /// </summary>
    /// <param name="sources">The source files, without markup.</param>
    /// <param name="globalValues">The global option values.</param>
    /// <param name="treeValues">The option values returned for every syntax tree.</param>
    /// <returns>The analyzer diagnostics.</returns>
    private static async Task<ImmutableArray<Diagnostic>> AnalyzeFullAsync(
        IReadOnlyList<string> sources,
        IReadOnlyDictionary<string, string> globalValues,
        IReadOnlyDictionary<string, string> treeValues)
    {
        var created = await AnalyzerTest.CreateCompilationAsync(sources).ConfigureAwait(false);
        var specific = ImmutableDictionary<string, ReportDiagnostic>.Empty.Add(
            DiagnosticIds.MissingDeclaration,
            ReportDiagnostic.Warn);
        var compilation = created.WithOptions(created.Options.WithSpecificDiagnosticOptions(specific));
        var options = new AnalyzerOptions(
            ImmutableArray<AdditionalText>.Empty,
            new TestAnalyzerConfigOptionsProvider(globalValues, treeValues));
        var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(new MissingDeclarationAnalyzer());
        var withAnalyzers = compilation.WithAnalyzers(analyzers, options);

        return await withAnalyzers.GetAnalyzerDiagnosticsAsync(CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>
    ///     Analyzer options backed by fixed dictionaries.
    /// </summary>
    private sealed class TestAnalyzerConfigOptions : AnalyzerConfigOptions
    {
        /// <summary>
        ///     The option values.
        /// </summary>
        private readonly IReadOnlyDictionary<string, string> _values;

        /// <summary>
        ///     Initializes a new instance of the <see cref="TestAnalyzerConfigOptions" /> class.
        /// </summary>
        /// <param name="values">The option values.</param>
        public TestAnalyzerConfigOptions(IReadOnlyDictionary<string, string> values)
        {
            _values = values;
        }

        /// <inheritdoc />
        public override IEnumerable<string> Keys => _values.Keys;

        /// <inheritdoc />
        public override bool TryGetValue(string key, out string value)
        {
            if (_values.TryGetValue(key, out var found))
            {
                value = found;
                return true;
            }

            value = null!;
            return false;
        }
    }

    /// <summary>
    ///     An analyzer configuration provider backed by fixed dictionaries.
    /// </summary>
    private sealed class TestAnalyzerConfigOptionsProvider : AnalyzerConfigOptionsProvider
    {
        /// <summary>
        ///     The global option values.
        /// </summary>
        private readonly AnalyzerConfigOptions _globalOptions;

        /// <summary>
        ///     The option values returned for every syntax tree.
        /// </summary>
        private readonly AnalyzerConfigOptions _treeOptions;

        /// <summary>
        ///     The empty option values returned for additional files.
        /// </summary>
        private static readonly AnalyzerConfigOptions EmptyOptions =
            new TestAnalyzerConfigOptions(new Dictionary<string, string>());

        /// <summary>
        ///     Initializes a new instance of the <see cref="TestAnalyzerConfigOptionsProvider" /> class.
        /// </summary>
        /// <param name="globalValues">The global option values.</param>
        /// <param name="treeValues">The option values returned for every syntax tree.</param>
        public TestAnalyzerConfigOptionsProvider(
            IReadOnlyDictionary<string, string> globalValues,
            IReadOnlyDictionary<string, string> treeValues)
        {
            _globalOptions = new TestAnalyzerConfigOptions(globalValues);
            _treeOptions = new TestAnalyzerConfigOptions(treeValues);
        }

        /// <inheritdoc />
        public override AnalyzerConfigOptions GlobalOptions => _globalOptions;

        /// <inheritdoc />
        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree)
        {
            return _treeOptions;
        }

        /// <inheritdoc />
        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile)
        {
            return EmptyOptions;
        }
    }
}
