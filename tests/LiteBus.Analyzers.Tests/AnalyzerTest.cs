using LiteBus.Commands.Abstractions;
using LiteBus.Events.Abstractions;
using LiteBus.Inbox.Abstractions;
using LiteBus.Messaging.Abstractions;
using LiteBus.Outbox.Abstractions;
using LiteBus.Queries.Abstractions;
using LiteBus.Transport.Abstractions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;

namespace LiteBus.Analyzers.Tests;

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
            typeof(IMessageTransport).Assembly.Location));

        return test;
    }
}