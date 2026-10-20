using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit.Abstractions;

namespace LiteBus.Analyzers.UnitTests;

/// <summary>
///     Proves every LiteBus analyzer stays cheap when a host analyzes one file in a large compilation.
/// </summary>
public sealed class AnalyzerPerFileCostTests
{
    /// <summary>
    ///     The small compilation size used as the flat-cost baseline.
    /// </summary>
    private const int SmallFileCount = 200;

    /// <summary>
    ///     The large compilation size used to detect whole-compilation scans.
    /// </summary>
    private const int LargeFileCount = 1000;

    /// <summary>
    ///     The maximum acceptable per-file execution time for one analyzer over the large compilation.
    /// </summary>
    private static readonly TimeSpan MaxPerFileExecutionTime = TimeSpan.FromMilliseconds(250);

    /// <summary>
    ///     The allowance added to the scaled baseline when comparing large and small compilations.
    /// </summary>
    private static readonly TimeSpan GrowthAllowance = TimeSpan.FromMilliseconds(50);

    /// <summary>
    ///     Writes per-analyzer timings to the test output.
    /// </summary>
    private readonly ITestOutputHelper _output;

    /// <summary>
    ///     Initializes a new instance of the <see cref="AnalyzerPerFileCostTests"/> class.
    /// </summary>
    /// <param name="output">The test output helper.</param>
    public AnalyzerPerFileCostTests(ITestOutputHelper output)
    {
        _output = output;
    }

    /// <summary>
    ///     Verifies per-file analysis of one file stays cheap and flat from 200 to 1,000 files.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public async Task PerFileAnalysis_StaysCheapAndFlatAsCompilationGrows()
    {
        var analyzers = DiscoverAnalyzers();

        var smallCosts = await MeasurePerFileCostsAsync(SmallFileCount, analyzers).ConfigureAwait(false);
        var largeCosts = await MeasurePerFileCostsAsync(LargeFileCount, analyzers).ConfigureAwait(false);

        WriteCostTable(analyzers, smallCosts, largeCosts);
        AssertFlatCosts(analyzers, smallCosts, largeCosts);

        Assert.True(
            analyzers.Length >= 19,
            FormattableString.Invariant($"Expected at least 19 LiteBus analyzers but discovered {analyzers.Length}."));
    }

    /// <summary>
    ///     Discovers every LiteBus analyzer by reflection.
    /// </summary>
    /// <returns>The analyzer instances.</returns>
    private static ImmutableArray<DiagnosticAnalyzer> DiscoverAnalyzers()
    {
        return typeof(DiagnosticDescriptors).Assembly
            .GetTypes()
            .Where(type => !type.IsAbstract
                && typeof(DiagnosticAnalyzer).IsAssignableFrom(type)
                && type.GetCustomAttribute<DiagnosticAnalyzerAttribute>() is not null)
            .Select(type => (DiagnosticAnalyzer) Activator.CreateInstance(type)!)
            .OrderBy(analyzer => analyzer.GetType().FullName, StringComparer.Ordinal)
            .ToImmutableArray();
    }

    /// <summary>
    ///     Builds a generated compilation once and measures per-file analysis of its middle file.
    /// </summary>
    /// <param name="fileCount">The number of generated files in the compilation.</param>
    /// <param name="analyzers">The analyzers to measure.</param>
    /// <returns>The minimum per-analyzer execution time across three runs.</returns>
    private static async Task<ImmutableDictionary<DiagnosticAnalyzer, TimeSpan>> MeasurePerFileCostsAsync(
        int fileCount,
        ImmutableArray<DiagnosticAnalyzer> analyzers)
    {
        var sources = LiteBusSourceGenerator.Generate(fileCount);
        var compilation = await AnalyzerTest.CreateCompilationAsync(sources).ConfigureAwait(false);

        var errors = compilation.GetDiagnostics()
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Take(10)
            .ToArray();

        Assert.True(
            errors.Length == 0,
            FormattableString.Invariant($"Generated {fileCount}-file compilation has compile errors: {string.Join("; ", errors.Select(error => error.ToString()))}"));

        var fileIndex = fileCount / 2;
        var semanticModel = compilation.GetSemanticModel(compilation.SyntaxTrees.ElementAt(fileIndex));

        var options = new CompilationWithAnalyzersOptions(
            options: new AnalyzerOptions(ImmutableArray<AdditionalText>.Empty),
            onAnalyzerException: null,
            concurrentAnalysis: false,
            logAnalyzerExecutionTime: true);

        var warmup = new CompilationWithAnalyzers(compilation, analyzers, options);
        await warmup.GetAnalysisResultAsync(semanticModel, null, analyzers, CancellationToken.None).ConfigureAwait(false);

        var minimums = analyzers.ToDictionary(analyzer => analyzer, _ => TimeSpan.MaxValue);

        for (var run = 0; run < 3; run++)
        {
            var withAnalyzers = new CompilationWithAnalyzers(compilation, analyzers, options);
            var result = await withAnalyzers.GetAnalysisResultAsync(semanticModel, null, analyzers, CancellationToken.None).ConfigureAwait(false);

            foreach (var analyzer in analyzers)
            {
                var elapsed = result.AnalyzerTelemetryInfo.TryGetValue(analyzer, out var telemetry)
                    ? telemetry.ExecutionTime
                    : TimeSpan.Zero;

                if (elapsed < minimums[analyzer])
                {
                    minimums[analyzer] = elapsed;
                }
            }
        }

        return minimums.ToImmutableDictionary();
    }

    /// <summary>
    ///     Writes every per-analyzer time to the test output, slowest first.
    /// </summary>
    /// <param name="analyzers">The measured analyzers.</param>
    /// <param name="smallCosts">The per-analyzer times over the small compilation.</param>
    /// <param name="largeCosts">The per-analyzer times over the large compilation.</param>
    private void WriteCostTable(
        ImmutableArray<DiagnosticAnalyzer> analyzers,
        ImmutableDictionary<DiagnosticAnalyzer, TimeSpan> smallCosts,
        ImmutableDictionary<DiagnosticAnalyzer, TimeSpan> largeCosts)
    {
        _output.WriteLine(FormattableString.Invariant($"LiteBus per-file analyzer costs, minimum of three runs, in milliseconds. Compilation-end rules report no per-file actions and read as 0.0."));

        foreach (var analyzer in analyzers.OrderByDescending(analyzer => largeCosts[analyzer]))
        {
            _output.WriteLine(FormattableString.Invariant($"{analyzer.GetType().FullName}: 200 files = {smallCosts[analyzer].TotalMilliseconds:F1} ms, 1000 files = {largeCosts[analyzer].TotalMilliseconds:F1} ms"));
        }
    }

    /// <summary>
    ///     Verifies every analyzer stays below the absolute budget and the scaled baseline.
    /// </summary>
    /// <param name="analyzers">The measured analyzers.</param>
    /// <param name="smallCosts">The per-analyzer times over the small compilation.</param>
    /// <param name="largeCosts">The per-analyzer times over the large compilation.</param>
    private static void AssertFlatCosts(
        ImmutableArray<DiagnosticAnalyzer> analyzers,
        ImmutableDictionary<DiagnosticAnalyzer, TimeSpan> smallCosts,
        ImmutableDictionary<DiagnosticAnalyzer, TimeSpan> largeCosts)
    {
        var message = new StringBuilder();
        message.AppendLine(CultureInfo.InvariantCulture, $"Per-file analyzer budget exceeded: 1000-file time must stay below {MaxPerFileExecutionTime.TotalMilliseconds:F0} ms and below 3 times the 200-file time plus {GrowthAllowance.TotalMilliseconds:F0} ms. Offending analyzers:");

        var failures = 0;

        foreach (var analyzer in analyzers)
        {
            var small = smallCosts[analyzer];
            var large = largeCosts[analyzer];
            var growthLimit = TimeSpan.FromTicks(small.Ticks * 3) + GrowthAllowance;

            if (large < MaxPerFileExecutionTime && large < growthLimit)
            {
                continue;
            }

            failures++;
            message.AppendLine(CultureInfo.InvariantCulture, $"{analyzer.GetType().FullName}: 200 files = {small.TotalMilliseconds:F1} ms, 1000 files = {large.TotalMilliseconds:F1} ms (limits {MaxPerFileExecutionTime.TotalMilliseconds:F0} ms absolute, {growthLimit.TotalMilliseconds:F1} ms growth)");
        }

        Assert.True(failures == 0, message.ToString());
    }
}
