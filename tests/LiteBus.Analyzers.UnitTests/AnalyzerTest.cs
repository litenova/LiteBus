using System.Collections.Immutable;
using LiteBus.Commands.Abstractions;
using LiteBus.Events.Abstractions;
using LiteBus.Inbox.Abstractions;
using LiteBus.Messaging.Abstractions;
using LiteBus.Outbox.Abstractions;
using LiteBus.Queries.Abstractions;
using LiteBus.Transport.Abstractions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;

namespace LiteBus.Analyzers.UnitTests;

/// <summary>
///     Shared analyzer test harness for LiteBus analyzer rules.
/// </summary>
internal static class AnalyzerTest
{
    /// <summary>
    ///     Reference assemblies aligned with the net10.0 test target framework.
    /// </summary>
    internal static readonly ReferenceAssemblies Net10ReferenceAssemblies = new(
        "net10.0",
        new PackageIdentity("Microsoft.NETCore.App.Ref", "10.0.0"),
        Path.Combine("ref", "net10.0"));

    /// <summary>
    ///     Verifies that valid source produces no diagnostics for the supplied analyzer.
    /// </summary>
    /// <typeparam name="TAnalyzer">The analyzer type under test.</typeparam>
    /// <param name="source">The source under test.</param>
    /// <returns>A task that completes when verification finishes.</returns>
    internal static Task VerifyNoDiagnosticsAsync<TAnalyzer>(string source)
        where TAnalyzer : DiagnosticAnalyzer, new()
    {
        var test = CreateTest<TAnalyzer>();
        test.TestCode = source;
        return test.RunAsync(CancellationToken.None);
    }

    /// <summary>
    ///     Verifies that invalid source produces the expected diagnostic for the supplied analyzer.
    /// </summary>
    /// <typeparam name="TAnalyzer">The analyzer type under test.</typeparam>
    /// <param name="source">The source under test.</param>
    /// <param name="expectedDiagnostic">The expected diagnostic descriptor.</param>
    /// <param name="markupLocation">The markup location index for the expected diagnostic.</param>
    /// <param name="arguments">The expected diagnostic message arguments.</param>
    /// <returns>A task that completes when verification finishes.</returns>
    internal static Task VerifyDiagnosticAsync<TAnalyzer>(
        string source,
        DiagnosticDescriptor expectedDiagnostic,
        int markupLocation,
        params object[] arguments)
        where TAnalyzer : DiagnosticAnalyzer, new()
    {
        var test = CreateTest<TAnalyzer>();
        test.TestCode = source;

        test.ExpectedDiagnostics.Add(
            new DiagnosticResult(expectedDiagnostic)
                .WithLocation(markupLocation)
                .WithArguments(arguments));

        return test.RunAsync(CancellationToken.None);
    }

    /// <summary>
    ///     Verifies that invalid source produces the expected diagnostics for the supplied analyzer.
    /// </summary>
    /// <typeparam name="TAnalyzer">The analyzer type under test.</typeparam>
    /// <param name="source">The source under test.</param>
    /// <param name="expectedDiagnostics">The expected diagnostics.</param>
    /// <returns>A task that completes when verification finishes.</returns>
    internal static Task VerifyDiagnosticsAsync<TAnalyzer>(
        string source,
        params (DiagnosticDescriptor Descriptor, int MarkupLocation, object[] Arguments)[] expectedDiagnostics)
        where TAnalyzer : DiagnosticAnalyzer, new()
    {
        var test = CreateTest<TAnalyzer>();
        test.TestCode = source;

        foreach (var (descriptor, markupLocation, arguments) in expectedDiagnostics)
        {
            test.ExpectedDiagnostics.Add(
                new DiagnosticResult(descriptor)
                    .WithLocation(markupLocation)
                    .WithArguments(arguments));
        }

        return test.RunAsync(CancellationToken.None);
    }

    /// <summary>
    ///     Verifies analyzer behavior under an <c>.editorconfig</c>, for rules whose required inputs are configured.
    /// </summary>
    /// <typeparam name="TAnalyzer">The analyzer type under test.</typeparam>
    /// <param name="source">The source under test.</param>
    /// <param name="editorConfig">The <c>.editorconfig</c> content applied to the compilation.</param>
    /// <param name="expectedDiagnostics">The expected diagnostics, empty when none are expected.</param>
    /// <returns>A task that completes when verification finishes.</returns>
    internal static Task VerifyWithEditorConfigAsync<TAnalyzer>(
        string source,
        string editorConfig,
        params (DiagnosticDescriptor Descriptor, int MarkupLocation, object[] Arguments)[] expectedDiagnostics)
        where TAnalyzer : DiagnosticAnalyzer, new()
    {
        var test = CreateTest<TAnalyzer>();
        test.TestCode = source;
        test.TestState.AnalyzerConfigFiles.Add(("/.editorconfig", editorConfig));

        foreach (var (descriptor, markupLocation, arguments) in expectedDiagnostics)
        {
            test.ExpectedDiagnostics.Add(
                new DiagnosticResult(descriptor)
                    .WithLocation(markupLocation)
                    .WithArguments(arguments));
        }

        return test.RunAsync(CancellationToken.None);
    }

    /// <summary>
    ///     Verifies a configured rule reports a diagnostic that carries no source location.
    /// </summary>
    /// <typeparam name="TAnalyzer">The analyzer type under test.</typeparam>
    /// <param name="source">The source under test.</param>
    /// <param name="editorConfig">The <c>.editorconfig</c> content applied to the compilation.</param>
    /// <param name="expectedDiagnostic">The expected diagnostic descriptor.</param>
    /// <param name="arguments">The expected diagnostic message arguments.</param>
    /// <returns>A task that completes when verification finishes.</returns>
    internal static Task VerifyUnlocatedWithEditorConfigAsync<TAnalyzer>(
        string source,
        string editorConfig,
        DiagnosticDescriptor expectedDiagnostic,
        params object[] arguments)
        where TAnalyzer : DiagnosticAnalyzer, new()
    {
        var test = CreateTest<TAnalyzer>();
        test.TestCode = source;
        test.TestState.AnalyzerConfigFiles.Add(("/.editorconfig", editorConfig));

        test.ExpectedDiagnostics.Add(
            new DiagnosticResult(expectedDiagnostic).WithArguments(arguments));

        return test.RunAsync(CancellationToken.None);
    }

    /// <summary>
    ///     Creates a configured analyzer test instance with LiteBus references.
    /// </summary>
    /// <typeparam name="TAnalyzer">The analyzer type under test.</typeparam>
    /// <returns>The configured analyzer test instance.</returns>
    private static CSharpAnalyzerTest<TAnalyzer, DefaultVerifier> CreateTest<TAnalyzer>()
        where TAnalyzer : DiagnosticAnalyzer, new()
    {
        var test = new CSharpAnalyzerTest<TAnalyzer, DefaultVerifier>
        {
            ReferenceAssemblies = Net10ReferenceAssemblies
        };

        test.TestState.AdditionalReferences.Add(MetadataReference.CreateFromFile(
            typeof(ICommand).Assembly.Location));

        test.TestState.AdditionalReferences.Add(MetadataReference.CreateFromFile(
            typeof(IEvent).Assembly.Location));

        test.TestState.AdditionalReferences.Add(MetadataReference.CreateFromFile(
            typeof(IQuery<>).Assembly.Location));

        test.TestState.AdditionalReferences.Add(MetadataReference.CreateFromFile(
            typeof(IInbox).Assembly.Location));

        test.TestState.AdditionalReferences.Add(MetadataReference.CreateFromFile(
            typeof(IOutbox).Assembly.Location));

        test.TestState.AdditionalReferences.Add(MetadataReference.CreateFromFile(
            typeof(HandlerPriorityAttribute).Assembly.Location));

        test.TestState.AdditionalReferences.Add(MetadataReference.CreateFromFile(
            typeof(MessageContractAttribute).Assembly.Location));

        test.TestState.AdditionalReferences.Add(MetadataReference.CreateFromFile(
            typeof(ITransportPublisher).Assembly.Location));

        return test;
    }

    /// <summary>
    ///     Verifies that valid source with extra metadata references produces no diagnostics.
    /// </summary>
    /// <typeparam name="TAnalyzer">The analyzer type under test.</typeparam>
    /// <param name="source">The source under test.</param>
    /// <param name="additionalReferences">Additional metadata references.</param>
    /// <returns>A task that completes when verification finishes.</returns>
    internal static Task VerifyNoDiagnosticsWithReferencesAsync<TAnalyzer>(
        string source,
        params MetadataReference[] additionalReferences)
        where TAnalyzer : DiagnosticAnalyzer, new()
    {
        var test = CreateTest<TAnalyzer>();
        test.TestCode = source;

        foreach (var reference in additionalReferences)
        {
            test.TestState.AdditionalReferences.Add(reference);
        }

        return test.RunAsync(CancellationToken.None);
    }

    /// <summary>
    ///     Verifies that source with extra metadata references produces one expected diagnostic without source location.
    /// </summary>
    /// <typeparam name="TAnalyzer">The analyzer type under test.</typeparam>
    /// <param name="source">The source under test.</param>
    /// <param name="expectedDiagnostic">The expected diagnostic descriptor.</param>
    /// <param name="arguments">The expected diagnostic message arguments.</param>
    /// <param name="additionalReferences">Additional metadata references.</param>
    /// <returns>A task that completes when verification finishes.</returns>
    internal static Task VerifyDiagnosticWithReferencesAsync<TAnalyzer>(
        string source,
        DiagnosticDescriptor expectedDiagnostic,
        object[] arguments,
        params MetadataReference[] additionalReferences)
        where TAnalyzer : DiagnosticAnalyzer, new()
    {
        var test = CreateTest<TAnalyzer>();
        test.TestCode = source;

        foreach (var reference in additionalReferences)
        {
            test.TestState.AdditionalReferences.Add(reference);
        }

        test.ExpectedDiagnostics.Add(
            new DiagnosticResult(expectedDiagnostic).WithArguments(arguments));

        return test.RunAsync(CancellationToken.None);
    }

    /// <summary>
    ///     Compiles source into a metadata reference for single-compilation referenced-assembly analyzer tests.
    /// </summary>
    /// <param name="assemblyName">The emitted assembly name.</param>
    /// <param name="source">The source to compile.</param>
    /// <param name="references">Additional metadata references required by the source.</param>
    /// <returns>The metadata reference for the emitted assembly.</returns>
    internal static MetadataReference CompileToMetadataReference(
        string assemblyName,
        string source,
        params MetadataReference[] references)
    {
        var compilationReferences = Net10ReferenceAssemblies
            .ResolveAsync(LanguageNames.CSharp, CancellationToken.None)
            .GetAwaiter()
            .GetResult()
            .Concat(references)
            .ToArray();

        var compilation = CSharpCompilation.Create(
            assemblyName,
            [CSharpSyntaxTree.ParseText(source)],
            compilationReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var stream = new MemoryStream();
        var emitResult = compilation.Emit(stream);

        if (!emitResult.Success)
        {
            throw new InvalidOperationException(
                string.Join(Environment.NewLine, emitResult.Diagnostics.Select(diagnostic => diagnostic.ToString())));
        }

        return MetadataReference.CreateFromImage(stream.ToArray());
    }

    /// <summary>
    ///     Verifies the diagnostics an analyzer reports over a compilation of several source files.
    /// </summary>
    /// <typeparam name="TAnalyzer">The analyzer type under test.</typeparam>
    /// <param name="sources">The source files, each of which may carry <c>{|#n:...|}</c> markup locations.</param>
    /// <param name="expectedDiagnostics">The expected diagnostics, empty when the compilation must be clean.</param>
    /// <returns>A task that completes when verification finishes.</returns>
    internal static Task VerifyMultiFileAsync<TAnalyzer>(
        IReadOnlyList<string> sources,
        params (DiagnosticDescriptor Descriptor, int MarkupLocation, object[] Arguments)[] expectedDiagnostics)
        where TAnalyzer : DiagnosticAnalyzer, new()
    {
        var test = CreateTest<TAnalyzer>();

        for (var index = 0; index < sources.Count; index++)
        {
            test.TestState.Sources.Add(($"/0/Test{index}.cs", sources[index]));
        }

        foreach (var (descriptor, markupLocation, arguments) in expectedDiagnostics)
        {
            test.ExpectedDiagnostics.Add(
                new DiagnosticResult(descriptor)
                    .WithLocation(markupLocation)
                    .WithArguments(arguments));
        }

        return test.RunAsync(CancellationToken.None);
    }

    /// <summary>
    ///     Creates a compilation of plain source files with the LiteBus contract assemblies referenced.
    /// </summary>
    /// <param name="sources">The source files, without markup.</param>
    /// <returns>The compilation.</returns>
    internal static async Task<CSharpCompilation> CreateCompilationAsync(IReadOnlyList<string> sources)
    {
        var references = await Net10ReferenceAssemblies
            .ResolveAsync(LanguageNames.CSharp, CancellationToken.None)
            .ConfigureAwait(false);

        var liteBusReferences = new[]
            {
                typeof(ICommand), typeof(IEvent), typeof(IQuery<>), typeof(IInbox), typeof(IOutbox),
                typeof(HandlerPriorityAttribute), typeof(MessageContractAttribute), typeof(ITransportPublisher)
            }
            .Select(type => type.Assembly.Location)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(location => (MetadataReference) MetadataReference.CreateFromFile(location));

        return CSharpCompilation.Create(
            "LiteBusAnalyzerCompilation",
            sources.Select((source, index) => CSharpSyntaxTree.ParseText(source, path: $"/0/Test{index}.cs")),
            references.Concat(liteBusReferences),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    /// <summary>
    ///     Runs analyzers the way a host analyzes one open file, and returns what they report for that file.
    /// </summary>
    /// <param name="compilation">The compilation the file belongs to.</param>
    /// <param name="fileIndex">The index of the file in the compilation's syntax trees.</param>
    /// <param name="analyzers">The analyzers to run.</param>
    /// <returns>The diagnostics reported by semantic analysis of that one file.</returns>
    /// <remarks>
    ///     This is the per-file path Visual Studio and language servers take. Compilation-end actions do not run on it,
    ///     so a rule that reports at compilation end must produce nothing here.
    /// </remarks>
    internal static async Task<ImmutableArray<Diagnostic>> GetPerFileDiagnosticsAsync(
        Compilation compilation,
        int fileIndex,
        params DiagnosticAnalyzer[] analyzers)
    {
        var analyzerArray = analyzers.ToImmutableArray();
        var withAnalyzers = compilation.WithAnalyzers(analyzerArray);
        var semanticModel = compilation.GetSemanticModel(compilation.SyntaxTrees.ElementAt(fileIndex));
        var result = await withAnalyzers
            .GetAnalysisResultAsync(semanticModel, null, analyzerArray, CancellationToken.None)
            .ConfigureAwait(false);

        return result.GetAllDiagnostics();
    }
}
