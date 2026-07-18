using LiteBus.Commands;
using LiteBus.Events;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Queries;
using LiteBus.Sample.Commands;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Sample.Documentation;

/// <summary>
///     Provides compiled source for the module-registration snippet used by the getting-started guide.
/// </summary>
internal static class ModuleRegistrationSnippet
{
    /// <summary>
    ///     Registers the semantic mediator modules used by the sample application.
    /// </summary>
    /// <param name="services">The application service collection.</param>
    internal static void Register(IServiceCollection services)
    {
        // <docs-snippet module-registration>
        services.AddLiteBus(liteBus =>
        {
            var applicationAssembly = typeof(ProcessPaymentCommand).Assembly;

            liteBus.AddMessaging(_ =>
            {
            });

            liteBus.AddCommands(commands => commands.RegisterFromAssembly(applicationAssembly));
            liteBus.AddQueries(queries => queries.RegisterFromAssembly(applicationAssembly));
            liteBus.AddEvents(events => events.RegisterFromAssembly(applicationAssembly));
        });
        // </docs-snippet>
    }
}
