namespace LiteBus.Runtime.Abstractions;

/// <summary>
///     Marker interface that indicates a module has a dependency on another module.
///     The dependent module will be initialized after all its required modules have been initialized.
/// </summary>
/// <typeparam name="TModule">The type of module that this module depends on.</typeparam>
/// <remarks>
///     Multiple dependencies can be declared by implementing multiple IRequires interfaces.
///     Circular dependencies will be detected and will throw an exception during initialization.
/// </remarks>
/// <example>
///     <code>
/// internal class CommandModule : IModule, IRequires&lt;MessageModule&gt;
/// {
///     public void Build(IModuleConfiguration configuration)
///     {
///         // MessageModule is guaranteed to have been initialized first
///         var messageRegistry = configuration.GetContext&lt;IMessageRegistry&gt;();
///     }
/// }
/// </code>
/// </example>
public interface IRequires<TModule> where TModule : IModule
{
    // Marker interface - no implementation required
}