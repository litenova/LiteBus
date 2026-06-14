using System.Reflection;
using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Events;
using LiteBus.Events.Abstractions;
using LiteBus.Queries;
using LiteBus.Queries.Abstractions;

namespace LiteBus.Mediator.UnitTests;

/// <summary>
///     Axis-scoped assembly registration for merged mediator unit tests.
/// </summary>
/// <remarks>
///     <see cref="CommandModuleBuilder.RegisterFromAssembly" />,
///     <see cref="EventModuleBuilder.RegisterFromAssembly" />, and
///     <see cref="QueryModuleBuilder.RegisterFromAssembly" /> scan an entire assembly.
///     After merging command, event, and query tests into one assembly, full-assembly scans register
///     duplicate global handlers from other axes (for example
///     <c>GlobalEventPreHandler</c> and <c>FakeGlobalEventPreHandler</c>).
///     Use these helpers to register only types under each axis test namespace prefix, mirroring the
///     same <see cref="IRegistrableCommandConstruct" /> filter logic as each module builder.
///     See also inline types in messaging open-generic tests that avoid cross-axis contamination.
/// </remarks>
internal static class MediatorTestRegistrationExtensions
{
    /// <summary>
    ///     Namespace prefix for command test use-case types.
    /// </summary>
    private const string CommandsNamespacePrefix = "LiteBus.Mediator.UnitTests.UseCases.Commands";

    /// <summary>
    ///     Namespace prefix for event test use-case types.
    /// </summary>
    private const string EventsNamespacePrefix = "LiteBus.Mediator.UnitTests.UseCases.Events";

    /// <summary>
    ///     Namespace prefix for query test use-case types.
    /// </summary>
    private const string QueriesNamespacePrefix = "LiteBus.Mediator.UnitTests.UseCases.Queries";

    /// <summary>
    ///     Registers command constructs from the mediator test assembly under the commands namespace prefix.
    /// </summary>
    /// <param name="builder">The command module builder.</param>
    /// <returns>The builder for method chaining.</returns>
    internal static CommandModuleBuilder RegisterFromCommandsTestAssembly(this CommandModuleBuilder builder)
    {
        return builder.RegisterFromCommandsTestAssembly(typeof(MediatorTestRegistrationExtensions));
    }

    /// <summary>
    ///     Registers command constructs from the anchor type's assembly under the commands namespace prefix.
    /// </summary>
    /// <param name="builder">The command module builder.</param>
    /// <param name="anchor">A type from the mediator test assembly used to resolve the assembly to scan.</param>
    /// <returns>The builder for method chaining.</returns>
    internal static CommandModuleBuilder RegisterFromCommandsTestAssembly(this CommandModuleBuilder builder, Type anchor)
    {
        ArgumentNullException.ThrowIfNull(anchor);

        RegisterFilteredConstructs(
            builder,
            anchor.Assembly,
            CommandsNamespacePrefix,
            typeof(IRegistrableCommandConstruct),
            builder.Register);

        return builder;
    }

    /// <summary>
    ///     Registers event constructs from the mediator test assembly under the events namespace prefix.
    /// </summary>
    /// <param name="builder">The event module builder.</param>
    /// <returns>The builder for method chaining.</returns>
    internal static EventModuleBuilder RegisterFromEventsTestAssembly(this EventModuleBuilder builder)
    {
        return builder.RegisterFromEventsTestAssembly(typeof(MediatorTestRegistrationExtensions));
    }

    /// <summary>
    ///     Registers event constructs from the anchor type's assembly under the events namespace prefix.
    /// </summary>
    /// <param name="builder">The event module builder.</param>
    /// <param name="anchor">A type from the mediator test assembly used to resolve the assembly to scan.</param>
    /// <returns>The builder for method chaining.</returns>
    internal static EventModuleBuilder RegisterFromEventsTestAssembly(this EventModuleBuilder builder, Type anchor)
    {
        ArgumentNullException.ThrowIfNull(anchor);

        RegisterFilteredConstructs(
            builder,
            anchor.Assembly,
            EventsNamespacePrefix,
            typeof(IRegistrableEventConstruct),
            builder.Register);

        return builder;
    }

    /// <summary>
    ///     Registers query constructs from the mediator test assembly under the queries namespace prefix.
    /// </summary>
    /// <param name="builder">The query module builder.</param>
    /// <returns>The builder for method chaining.</returns>
    internal static QueryModuleBuilder RegisterFromQueriesTestAssembly(this QueryModuleBuilder builder)
    {
        return builder.RegisterFromQueriesTestAssembly(typeof(MediatorTestRegistrationExtensions));
    }

    /// <summary>
    ///     Registers query constructs from the anchor type's assembly under the queries namespace prefix.
    /// </summary>
    /// <param name="builder">The query module builder.</param>
    /// <param name="anchor">A type from the mediator test assembly used to resolve the assembly to scan.</param>
    /// <returns>The builder for method chaining.</returns>
    internal static QueryModuleBuilder RegisterFromQueriesTestAssembly(this QueryModuleBuilder builder, Type anchor)
    {
        ArgumentNullException.ThrowIfNull(anchor);

        RegisterFilteredConstructs(
            builder,
            anchor.Assembly,
            QueriesNamespacePrefix,
            typeof(IRegistrableQueryConstruct),
            builder.Register);

        return builder;
    }

    /// <summary>
    ///     Registers concrete construct types from an assembly when their namespace matches the prefix.
    /// </summary>
    /// <typeparam name="TBuilder">The module builder type passed through for chaining.</typeparam>
    /// <param name="builder">The module builder instance (unused except for generic inference).</param>
    /// <param name="assembly">The assembly to scan.</param>
    /// <param name="namespacePrefix">The required namespace prefix for registrable types.</param>
    /// <param name="constructMarker">The axis registrable construct marker interface.</param>
    /// <param name="register">The builder registration delegate.</param>
    private static void RegisterFilteredConstructs<TBuilder>(
        TBuilder builder,
        Assembly assembly,
        string namespacePrefix,
        Type constructMarker,
        Func<Type, TBuilder> register)
    {
        _ = builder;

        foreach (var registrableConstruct in assembly.GetTypes()
                     .Where(t => t is { IsClass: true, IsAbstract: false }
                                 && t.Namespace?.StartsWith(namespacePrefix, StringComparison.Ordinal) == true
                                 && t.IsAssignableTo(constructMarker)))
        {
            register(registrableConstruct);
        }
    }
}
