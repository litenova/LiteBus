using System;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;

namespace LiteBus.Analyzers.Analysis;

/// <summary>
///     Gathers message types and open generic main handlers from symbol actions for one message kind.
/// </summary>
/// <remarks>
///     One instance serves one compilation and one <see cref="MessageKind" />. Symbol actions feed source types
///     through <see cref="Add" />. <see cref="ToMessageTypes" /> returns the analyzed assembly's own messages in
///     discovery order, while <see cref="ToOpenGenericMainHandlers" /> additionally appends referenced assemblies,
///     whose handlers still satisfy coverage.
/// </remarks>
internal sealed class MessageTypeCollector
{
    /// <summary>
    ///     The marker interface metadata names for the collected message kind.
    /// </summary>
    private readonly string[] _markerMetadataNames;

    /// <summary>
    ///     The main handler interface metadata names for the collected message kind.
    /// </summary>
    private readonly string[] _handlerMetadataNames;

    /// <summary>
    ///     The message types already added from source symbols.
    /// </summary>
    private readonly ConcurrentDictionary<INamedTypeSymbol, byte> _processedMessages = new(SymbolEqualityComparer.Default);

    /// <summary>
    ///     The handler types already inspected, shared by the source and referenced assembly passes.
    /// </summary>
    private readonly ConcurrentDictionary<INamedTypeSymbol, byte> _processedHandlers = new(SymbolEqualityComparer.Default);

    /// <summary>
    ///     The message type registrations gathered from source types.
    /// </summary>
    private readonly ConcurrentBag<MessageTypeRegistration> _sourceMessages = new();

    /// <summary>
    ///     The open generic main handlers gathered from source types.
    /// </summary>
    private readonly ConcurrentBag<INamedTypeSymbol> _sourceOpenGenericHandlers = new();

    /// <summary>
    ///     The per-compilation type cache.
    /// </summary>
    private readonly LiteBusTypeCache _typeCache;

    /// <summary>
    ///     The message kind being collected.
    /// </summary>
    private readonly MessageKind _kind;

    /// <summary>
    ///     Initializes a new instance of the <see cref="MessageTypeCollector" /> class.
    /// </summary>
    /// <param name="typeCache">The per-compilation type cache.</param>
    /// <param name="kind">The message kind to collect.</param>
    internal MessageTypeCollector(LiteBusTypeCache typeCache, MessageKind kind)
    {
        _typeCache = typeCache;
        _kind = kind;
        _markerMetadataNames = MessageAnalysis.GetMarkerMetadataNames(kind);
        _handlerMetadataNames = MessageAnalysis.GetOpenGenericHandlerMetadataNames(kind);
    }

    /// <summary>
    ///     Adds the message type and open generic main handler facts declared on one source type symbol.
    /// </summary>
    /// <param name="symbol">The candidate type symbol.</param>
    /// <remarks>
    ///     Symbol actions already deliver nested types, so this method never recurses.
    /// </remarks>
    internal void Add(INamedTypeSymbol symbol)
    {
        TryAddMessage(symbol);
        TryAddOpenGenericHandler(symbol);
    }

    /// <summary>
    ///     Gets the message types declared in the analyzed assembly, in discovery order.
    /// </summary>
    /// <returns>The message type registrations declared in the analyzed assembly.</returns>
    /// <remarks>
    ///     Only types declared in the analyzed assembly are candidates. A message from a referenced assembly cannot be
    ///     annotated from here, so reporting it would produce a diagnostic with no available fix.
    /// </remarks>
    internal ImmutableArray<MessageTypeRegistration> ToMessageTypes()
    {
        var compilation = _typeCache.Compilation;
        var order = CompilationEndOrdering.GetSyntaxTreeOrder(compilation);
        var builder = ImmutableArray.CreateBuilder<MessageTypeRegistration>();

        builder.AddRange(_sourceMessages
            .Select(registration => (Registration: registration, Key: CompilationEndOrdering.GetOrderKey(registration.Location, order)))
            .OrderBy(entry => entry.Key?.TreeIndex ?? int.MaxValue)
            .ThenBy(entry => entry.Key?.SpanStart ?? int.MaxValue)
            .ThenBy(entry => LiteBusSymbols.GetMetadataName(entry.Registration.MessageType), StringComparer.Ordinal)
            .Select(entry => entry.Registration));

        return builder.ToImmutable();
    }

    /// <summary>
    ///     Gets all open generic main handlers: gathered source facts in discovery order, then referenced assemblies.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>All discovered open generic main handler type definitions.</returns>
    internal ImmutableArray<INamedTypeSymbol> ToOpenGenericMainHandlers(CancellationToken cancellationToken = default)
    {
        var compilation = _typeCache.Compilation;
        var order = CompilationEndOrdering.GetSyntaxTreeOrder(compilation);
        var builder = ImmutableArray.CreateBuilder<INamedTypeSymbol>();

        builder.AddRange(_sourceOpenGenericHandlers
            .Select(symbol => (Symbol: symbol, Key: CompilationEndOrdering.GetOrderKey(GetSymbolLocation(symbol), order)))
            .OrderBy(entry => entry.Key?.TreeIndex ?? int.MaxValue)
            .ThenBy(entry => entry.Key?.SpanStart ?? int.MaxValue)
            .ThenBy(entry => LiteBusSymbols.GetMetadataName(entry.Symbol), StringComparer.Ordinal)
            .Select(entry => entry.Symbol));

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
                CollectOpenGenericHandlersFromNamespace(module.GlobalNamespace, builder);
            }
        }

        return builder.ToImmutable();
    }

    /// <summary>
    ///     Adds a message type registration when the symbol matches the collected kind.
    /// </summary>
    /// <param name="symbol">The candidate type symbol.</param>
    private void TryAddMessage(INamedTypeSymbol symbol)
    {
        if (!_processedMessages.TryAdd(symbol, 0))
        {
            return;
        }

        if (!MessageAnalysis.IsAnalyzableMessageType(symbol) ||
            !MessageAnalysis.ImplementsAnyMarkerInterface(symbol, _typeCache, _markerMetadataNames))
        {
            return;
        }

        var compilation = _typeCache.Compilation;
        symbol = LiteBusSymbols.RetargetToCompilation(compilation, symbol);

        if (_kind == MessageKind.Query && MessageAnalysis.IsStreamQueryType(symbol, _typeCache))
        {
            return;
        }

        var location = LiteBusSymbols.GetDiagnosticLocation(
            compilation,
            symbol.Locations.FirstOrDefault(locationCandidate => locationCandidate.IsInSource) ??
            symbol.Locations.FirstOrDefault() ??
            Location.None);
        _sourceMessages.Add(new MessageTypeRegistration(symbol, location));
    }

    /// <summary>
    ///     Adds an open generic main handler when the symbol matches the collected kind.
    /// </summary>
    /// <param name="symbol">The candidate handler type symbol.</param>
    private void TryAddOpenGenericHandler(INamedTypeSymbol symbol)
    {
        if (!_processedHandlers.TryAdd(symbol, 0))
        {
            return;
        }

        if (HandlerAnalysis.IsGenericTypeDefinition(symbol) &&
            HandlerAnalysis.UsesBareMessageTypeParameter(symbol) &&
            MessageAnalysis.ImplementsAnyMainHandlerInterface(symbol, _typeCache, _handlerMetadataNames))
        {
            _sourceOpenGenericHandlers.Add(LiteBusSymbols.RetargetToCompilation(_typeCache.Compilation, symbol));
        }
    }

    /// <summary>
    ///     Walks a referenced assembly namespace and collects open generic main handlers declared beneath it.
    /// </summary>
    /// <param name="namespaceSymbol">The namespace to walk.</param>
    /// <param name="builder">The handler builder.</param>
    private void CollectOpenGenericHandlersFromNamespace(INamespaceSymbol namespaceSymbol, ImmutableArray<INamedTypeSymbol>.Builder builder)
    {
        foreach (var member in namespaceSymbol.GetMembers())
        {
            if (member is INamespaceSymbol nestedNamespace)
            {
                CollectOpenGenericHandlersFromNamespace(nestedNamespace, builder);
            }
            else if (member is INamedTypeSymbol namedType)
            {
                AddReferencedOpenGenericHandler(namedType, builder);
            }
        }
    }

    /// <summary>
    ///     Adds an open generic main handler for one referenced assembly type and its nested types.
    /// </summary>
    /// <param name="symbol">The candidate handler type symbol.</param>
    /// <param name="builder">The handler builder.</param>
    private void AddReferencedOpenGenericHandler(INamedTypeSymbol symbol, ImmutableArray<INamedTypeSymbol>.Builder builder)
    {
        if (!_processedHandlers.TryAdd(symbol, 0))
        {
            return;
        }

        if (HandlerAnalysis.IsGenericTypeDefinition(symbol) &&
            HandlerAnalysis.UsesBareMessageTypeParameter(symbol) &&
            MessageAnalysis.ImplementsAnyMainHandlerInterface(symbol, _typeCache, _handlerMetadataNames))
        {
            builder.Add(LiteBusSymbols.RetargetToCompilation(_typeCache.Compilation, symbol));
        }

        foreach (var nestedType in symbol.GetTypeMembers())
        {
            AddReferencedOpenGenericHandler(nestedType, builder);
        }
    }

    /// <summary>
    ///     Gets the first source location of a type symbol for ordering.
    /// </summary>
    /// <param name="symbol">The type symbol.</param>
    /// <returns>The first in-source location, the first location, or <see cref="Location.None" />.</returns>
    private static Location GetSymbolLocation(INamedTypeSymbol symbol)
    {
        return symbol.Locations.FirstOrDefault(locationCandidate => locationCandidate.IsInSource) ??
            symbol.Locations.FirstOrDefault() ??
            Location.None;
    }
}
