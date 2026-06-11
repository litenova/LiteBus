using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace LiteBus.Analyzers;

/// <summary>
///     Reports constructors that inject transactional inbox storage without a database context.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class TransactionalInboxWithoutDbContextAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(DiagnosticDescriptors.TransactionalInboxWithoutDbContext);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(AnalyzeNamedType, SymbolKind.NamedType);
    }

    /// <summary>
    ///     Reports constructors that inject transactional inbox storage without a database context.
    /// </summary>
    /// <param name="context">The symbol analysis context.</param>
    private static void AnalyzeNamedType(SymbolAnalysisContext context)
    {
        if (context.Symbol is not INamedTypeSymbol typeSymbol)
        {
            return;
        }

        var transactionalInbox = context.Compilation.GetTypeByMetadataName("LiteBus.Inbox.Abstractions.ITransactionalInboxStore");

        if (transactionalInbox is null)
        {
            return;
        }

        foreach (var constructor in typeSymbol.InstanceConstructors.Where(ctor => ctor.DeclaredAccessibility == Accessibility.Public))
        {
            var hasTransactionalInbox = constructor.Parameters.Any(parameter => Implements(parameter.Type, transactionalInbox));

            if (!hasTransactionalInbox)
            {
                continue;
            }

            var hasDbContext = constructor.Parameters.Any(parameter => InheritsFromDbContext(parameter.Type));

            if (hasDbContext)
            {
                continue;
            }

            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.TransactionalInboxWithoutDbContext,
                constructor.Locations.FirstOrDefault() ?? typeSymbol.Locations.First(),
                typeSymbol.Name));
        }
    }

    /// <summary>
    ///     Determines whether the type implements the expected interface.
    /// </summary>
    /// <param name="type">The candidate type symbol.</param>
    /// <param name="expected">The expected interface symbol.</param>
    /// <returns><see langword="true" /> when the type implements the interface.</returns>
    private static bool Implements(ITypeSymbol type, INamedTypeSymbol expected)
    {
        return type.AllInterfaces.Any(candidate => SymbolEqualityComparer.Default.Equals(candidate, expected)) || SymbolEqualityComparer.Default.Equals(type, expected);
    }

    /// <summary>
    ///     Determines whether the type inherits from Entity Framework Core <c>DbContext</c>.
    /// </summary>
    /// <param name="type">The candidate type symbol.</param>
    /// <returns><see langword="true" /> when the type is or inherits from <c>DbContext</c>.</returns>
    private static bool InheritsFromDbContext(ITypeSymbol type)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (current.Name == "DbContext")
            {
                return true;
            }
        }

        return false;
    }
}