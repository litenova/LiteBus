using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using LiteBus.Analyzers.Analysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace LiteBus.Analyzers;

/// <summary>
///     Reports subscriber names shared by handlers of different message types.
/// </summary>
/// <remarks>
///     <para>
///         Per-handler outbox rows record the subscriber name, so a name shared by reactions to different events makes
///         rows hard to read in operations tooling and makes renaming one handler change the other's recorded identity.
///         Composition already refuses two main handlers of one message that share a name; this rule reports the reuse
///         composition allows, where handlers of different messages share one name.
///     </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class SubscriberNameReusedAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    ///     The metadata name of the attribute that gives a handler its subscriber name.
    /// </summary>
    private const string SubscriberNameAttributeMetadataName = "LiteBus.Messaging.Abstractions.SubscriberNameAttribute";

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        [DiagnosticDescriptors.SubscriberNameReused];

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.Analyze);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(AnalyzeCompilationStart);
    }

    /// <summary>
    ///     Gathers handler registrations for one compilation.
    /// </summary>
    /// <param name="context">The compilation start analysis context.</param>
    private static void AnalyzeCompilationStart(CompilationStartAnalysisContext context)
    {
        var typeCache = new LiteBusTypeCache(context.Compilation);

        if (!HandlerAnalysis.AnyHandlerInterfaceResolves(typeCache))
        {
            return;
        }

        var attribute = typeCache.GetType(SubscriberNameAttributeMetadataName);

        if (attribute is null)
        {
            return;
        }

        var collector = new HandlerRegistrationCollector(typeCache);

        context.RegisterSymbolAction(
            symbolContext => AnalyzeSymbol(symbolContext, collector),
            SymbolKind.NamedType);

        context.RegisterCompilationEndAction(
            endContext => AnalyzeCompilationEnd(endContext, collector, attribute));
    }

    /// <summary>
    ///     Adds the handler registrations declared on one type symbol.
    /// </summary>
    /// <param name="context">The symbol analysis context.</param>
    /// <param name="collector">The handler registration collector.</param>
    /// <remarks>
    ///     Only types declared in the analyzed assembly are collected. A handler in a referenced assembly cannot be
    ///     renamed from here, so reporting it would produce a warning with no available fix.
    /// </remarks>
    private static void AnalyzeSymbol(SymbolAnalysisContext context, HandlerRegistrationCollector collector)
    {
        if (context.Symbol is INamedTypeSymbol symbol &&
            SymbolEqualityComparer.Default.Equals(symbol.ContainingAssembly, context.Compilation.Assembly))
        {
            collector.Add(symbol);
        }
    }

    /// <summary>
    ///     Reports subscriber names used by handlers of different message types.
    /// </summary>
    /// <param name="context">The compilation analysis context.</param>
    /// <param name="collector">The handler registration collector.</param>
    /// <param name="attribute">The resolved subscriber name attribute symbol.</param>
    private static void AnalyzeCompilationEnd(
        CompilationAnalysisContext context,
        HandlerRegistrationCollector collector,
        INamedTypeSymbol attribute)
    {
        var groups = CollectOccurrencesByName(collector, attribute)
            .OrderBy(group => group.Key, StringComparer.Ordinal);

        foreach (var group in groups)
        {
            context.CancellationToken.ThrowIfCancellationRequested();

            if (group.Value.Count < 2 || !HandlesDifferentMessages(group.Value))
            {
                continue;
            }

            var first = group.Value[0];
            var second = FindSecondHandler(group.Value, first);

            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.SubscriberNameReused,
                second.Location,
                group.Key,
                first.HandlerName,
                second.HandlerName));
        }
    }

    /// <summary>
    ///     Groups the named handler occurrences of the analyzed assembly by subscriber name.
    /// </summary>
    /// <param name="collector">The handler registration collector.</param>
    /// <param name="attribute">The resolved subscriber name attribute symbol.</param>
    /// <returns>The occurrences keyed by subscriber name, in discovery order within each group.</returns>
    /// <remarks>
    ///     Names compare with <c>StringComparison.Ordinal</c>, matching the dispatch-time match in
    ///     <c>SubscriberNames.Matches</c>.
    /// </remarks>
    private static Dictionary<string, List<SubscriberNameOccurrence>> CollectOccurrencesByName(
        HandlerRegistrationCollector collector,
        INamedTypeSymbol attribute)
    {
        Dictionary<string, List<SubscriberNameOccurrence>> groups = new(StringComparer.Ordinal);

        var handlers = collector.ToSourceRegistrations()
            .GroupBy<HandlerRegistration, INamedTypeSymbol>(registration => registration.HandlerType, SymbolEqualityComparer.Default);

        foreach (var handler in handlers)
        {
            var names = GetSubscriberNames(handler.Key, attribute);

            if (names.Count == 0)
            {
                continue;
            }

            List<ITypeSymbol> messages =
            [
                .. handler.Select(registration => registration.MessageType).Distinct<ITypeSymbol>(SymbolEqualityComparer.Default)
            ];
            var location = handler.First().Location;

            foreach (var name in names)
            {
                if (!groups.TryGetValue(name, out var occurrences))
                {
                    occurrences = [];
                    groups[name] = occurrences;
                }

                occurrences.Add(new SubscriberNameOccurrence(handler.Key.Name, location, messages));
            }
        }

        return groups;
    }

    /// <summary>
    ///     Gets the subscriber names declared on one handler type.
    /// </summary>
    /// <param name="handlerType">The handler type symbol.</param>
    /// <param name="attribute">The resolved subscriber name attribute symbol.</param>
    /// <returns>The declared names, in attribute order.</returns>
    /// <remarks>
    ///     The attribute value is a compile-time constant, so a <c>const</c> field resolves to its value the same way
    ///     a literal does. Empty names are skipped: composition validation reports them.
    /// </remarks>
    private static List<string> GetSubscriberNames(INamedTypeSymbol handlerType, INamedTypeSymbol attribute)
    {
        List<string> names = [];

        foreach (var applied in handlerType.GetAttributes())
        {
            if (!SymbolEqualityComparer.Default.Equals(applied.AttributeClass, attribute) ||
                applied.ConstructorArguments.Length == 0)
            {
                continue;
            }

            if (applied.ConstructorArguments[0].Value is string name && name.Length > 0)
            {
                names.Add(name);
            }
        }

        return names;
    }

    /// <summary>
    ///     Determines whether the handlers sharing one subscriber name handle more than one message type.
    /// </summary>
    /// <param name="occurrences">The occurrences sharing one subscriber name.</param>
    /// <returns><see langword="true" /> when the name is used for different message types.</returns>
    private static bool HandlesDifferentMessages(List<SubscriberNameOccurrence> occurrences)
    {
        HashSet<ITypeSymbol> messages = new(SymbolEqualityComparer.Default);

        foreach (var occurrence in occurrences)
        {
            foreach (var message in occurrence.Messages)
            {
                messages.Add(message);

                if (messages.Count > 1)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    ///     Finds the handler the diagnostic names alongside the first handler sharing the name.
    /// </summary>
    /// <param name="occurrences">The occurrences sharing one subscriber name, in discovery order.</param>
    /// <param name="first">The first handler sharing the name.</param>
    /// <returns>The earliest handler whose handled messages differ from the first handler's.</returns>
    /// <remarks>
    ///     A handler repeating the first handler's messages is skipped, so the diagnostic names the pair that
    ///     actually introduces the second message type.
    /// </remarks>
    private static SubscriberNameOccurrence FindSecondHandler(
        List<SubscriberNameOccurrence> occurrences,
        SubscriberNameOccurrence first)
    {
        foreach (var occurrence in occurrences.Skip(1))
        {
            if (!HaveSameMessages(first.Messages, occurrence.Messages))
            {
                return occurrence;
            }
        }

        return occurrences[1];
    }

    /// <summary>
    ///     Determines whether two handlers handle the same message types.
    /// </summary>
    /// <param name="first">The first handler's message types.</param>
    /// <param name="second">The second handler's message types.</param>
    /// <returns><see langword="true" /> when both handlers handle the same message types.</returns>
    private static bool HaveSameMessages(List<ITypeSymbol> first, List<ITypeSymbol> second)
    {
        HashSet<ITypeSymbol> firstMessages = new(first, SymbolEqualityComparer.Default);

        return firstMessages.SetEquals(second);
    }

    /// <summary>
    ///     Describes one handler carrying a subscriber name.
    /// </summary>
    /// <param name="HandlerName">The handler's simple type name.</param>
    /// <param name="Location">The handler's diagnostic location.</param>
    /// <param name="Messages">The message types the handler handles.</param>
    private sealed record SubscriberNameOccurrence(string HandlerName, Location Location, List<ITypeSymbol> Messages);
}
