using System.Text;
using LiteBus.Analyzers.Analysis;
using LiteBus.Queries.Abstractions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace LiteBus.Analyzers.UnitTests;

/// <summary>
///     Tests for the <see cref="LiteBusTypeCache" /> per-compilation cache.
/// </summary>
public sealed class LiteBusTypeCacheTests
{
    /// <summary>
    ///     Verifies handler collection over fifty handlers resolves each metadata name once.
    /// </summary>
    /// <returns>A task that completes when verification finishes.</returns>
    [Fact]
    public async Task CollectHandlerRegistrations_OverFiftyHandlers_ResolvesEachMetadataNameOnce()
    {
        var source = new StringBuilder();
        source.AppendLine("using System;");
        source.AppendLine("using System.Threading;");
        source.AppendLine("using System.Threading.Tasks;");
        source.AppendLine("using LiteBus.Queries.Abstractions;");

        for (var index = 0; index < 50; index++)
        {
            source.AppendLine($"public sealed record Query{index} : IQuery<string>;");
            source.AppendLine($"public sealed class Handler{index} : IQueryHandler<Query{index}, string>, IDisposable");
            source.AppendLine("{");
            source.AppendLine($"    public Task<string> HandleAsync(Query{index} query, CancellationToken cancellationToken = default) => Task.FromResult(\"ok\");");
            source.AppendLine("    public void Dispose() { }");
            source.AppendLine("}");
        }

        var tree = CSharpSyntaxTree.ParseText(source.ToString());
        var references = await AnalyzerTest.Net10ReferenceAssemblies
            .ResolveAsync(LanguageNames.CSharp, CancellationToken.None)
            .ConfigureAwait(false);
        var compilation = CSharpCompilation.Create(
            "LiteBusTypeCacheTests",
            [tree],
            references.Concat([MetadataReference.CreateFromFile(typeof(IQuery<>).Assembly.Location)]).ToArray(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var typeCache = new LiteBusTypeCache(compilation);
        var registrations = HandlerAnalysis.CollectHandlerRegistrations(typeCache);

        Assert.Equal(50, registrations.Length);

        // Each handler also implements IDisposable, which matches nothing and walks the
        // whole map, so one full pass resolves every distinct metadata name exactly once
        // instead of once per handler interface.
        Assert.Equal(HandlerAnalysis.DistinctHandlerInterfaceMetadataNameCount, typeCache.LookupCount);
    }
}
