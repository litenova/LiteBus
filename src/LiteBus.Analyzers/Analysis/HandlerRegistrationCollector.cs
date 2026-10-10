using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;

namespace LiteBus.Analyzers.Analysis;

/// <summary>
///     Gathers handler registrations from symbol actions and reports them in discovery order at compilation end.
/// </summary>
/// <remarks>
///     One instance serves one compilation. Symbol actions feed source types through <see cref="Add" /> while
///     <see cref="ToRegistrations" /> runs at compilation end, where it sorts the gathered source facts and
///     appends registrations from referenced assemblies.
/// </remarks>
internal sealed class HandlerRegistrationCollector
{
    /// <summary>
    ///     The handler types already added, shared by the source and referenced assembly passes.
    /// </summary>
    private readonly ConcurrentDictionary<INamedTypeSymbol, byte> _processed = new(SymbolEqualityComparer.Default);

    /// <summary>
    ///     The registration groups gathered from source types, one group per type in interface order.
    /// </summary>
    private readonly ConcurrentBag<List<HandlerRegistration>> _sourceGroups = new();

    /// <summary>
    ///     The per-compilation type cache.
    /// </summary>
    private readonly LiteBusTypeCache _typeCache;

    /// <summary>
    ///     Initializes a new instance of the <see cref="HandlerRegistrationCollector" /> class.
    /// </summary>
    /// <param name="typeCache">The per-compilation type cache.</param>
    internal HandlerRegistrationCollector(LiteBusTypeCache typeCache)
    {
        _typeCache = typeCache;
    }

    /// <summary>
    ///     Adds the handler registrations declared on one source type symbol.
    /// </summary>
    /// <param name="symbol">The candidate handler type symbol.</param>
    /// <remarks>
    ///     Symbol actions already deliver nested types, so this method never recurses. Matches keep
    ///     <see cref="INamedTypeSymbol.AllInterfaces" /> order inside the group.
    /// </remarks>
    internal void Add(INamedTypeSymbol symbol)
    {
        if (symbol.TypeKind == TypeKind.Interface || !_processed.TryAdd(symbol, 0))
        {
            return;
        }

        var group = CreateRegistrations(symbol);

        if (group.Count > 0)
        {
            _sourceGroups.Add(group);
        }
    }

    /// <summary>
    ///     Gets all handler registrations: gathered source facts in discovery order, then referenced assemblies.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>All discovered handler registrations.</returns>
    internal ImmutableArray<HandlerRegistration> ToRegistrations(CancellationToken cancellationToken = default)
    {
        var compilation = _typeCache.Compilation;
        var order = CompilationEndOrdering.GetSyntaxTreeOrder(compilation);
        var builder = ImmutableArray.CreateBuilder<HandlerRegistration>();

        builder.AddRange(_sourceGroups
            .SelectMany(group => group)
            .Select(registration => (Registration: registration, Key: CompilationEndOrdering.GetOrderKey(registration.Location, order)))
            .OrderBy(entry => entry.Key?.TreeIndex ?? int.MaxValue)
            .ThenBy(entry => entry.Key?.SpanStart ?? int.MaxValue)
            .ThenBy(entry => LiteBusSymbols.GetMetadataName(entry.Registration.HandlerType), StringComparer.Ordinal)
            .Select(entry => entry.Registration));

        foreach (var reference in compilation.References)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (compilation.GetAssemblyOrModuleSymbol(reference) is not IAssemblySymbol assemblySymbol ||
                !HandlerAnalysis.ShouldScanReferencedAssembly(assemblySymbol, compilation.Assembly))
            {
                continue;
            }

            foreach (var module in assemblySymbol.Modules)
            {
                CollectFromNamespace(module.GlobalNamespace, builder);
            }
        }

        return builder.ToImmutable();
    }

    /// <summary>
    ///     Creates the handler registrations declared on one type symbol in interface order.
    /// </summary>
    /// <param name="symbol">The candidate handler type symbol.</param>
    /// <returns>The registrations declared on the symbol, empty when it implements no handler interface.</returns>
    private List<HandlerRegistration> CreateRegistrations(INamedTypeSymbol symbol)
    {
        var location = LiteBusSymbols.GetDiagnosticLocation(
            _typeCache.Compilation,
            symbol.Locations.FirstOrDefault(locationCandidate => locationCandidate.IsInSource) ?? symbol.Locations.FirstOrDefault() ?? Location.None);

        var group = new List<HandlerRegistration>();

        foreach (var handlerInterface in symbol.AllInterfaces)
        {
            var pipeline = HandlerAnalysis.GetPipeline(_typeCache, handlerInterface);

            if (pipeline is null)
            {
                continue;
            }

            var messageType = handlerInterface.TypeArguments[0];
            group.Add(new HandlerRegistration(symbol, messageType, pipeline, location));
        }

        return group;
    }

    /// <summary>
    ///     Walks a referenced assembly namespace and registers handler types declared beneath it.
    /// </summary>
    /// <param name="namespaceSymbol">The namespace to walk.</param>
    /// <param name="builder">The registration builder.</param>
    private void CollectFromNamespace(INamespaceSymbol namespaceSymbol, ImmutableArray<HandlerRegistration>.Builder builder)
    {
        foreach (var member in namespaceSymbol.GetMembers())
        {
            if (member is INamespaceSymbol nestedNamespace)
            {
                CollectFromNamespace(nestedNamespace, builder);
            }
            else if (member is INamedTypeSymbol namedType)
            {
                AddReferencedType(namedType, builder);
            }
        }
    }

    /// <summary>
    ///     Adds handler registrations for one referenced assembly type and its nested types.
    /// </summary>
    /// <param name="symbol">The candidate handler type symbol.</param>
    /// <param name="builder">The registration builder.</param>
    private void AddReferencedType(INamedTypeSymbol symbol, ImmutableArray<HandlerRegistration>.Builder builder)
    {
        if (symbol.TypeKind == TypeKind.Interface || !_processed.TryAdd(symbol, 0))
        {
            return;
        }

        builder.AddRange(CreateRegistrations(symbol));

        foreach (var nestedType in symbol.GetTypeMembers())
        {
            AddReferencedType(nestedType, builder);
        }
    }
}
