using LiteBus.Messaging.Abstractions;

namespace LiteBus.Events.Abstractions;

/// <summary>
///     Represents a predicate function used to filter event handlers based on their descriptor metadata.
/// </summary>
/// <param name="descriptor">The handler descriptor containing metadata about the handler.</param>
/// <returns><c>true</c> if the handler should execute; otherwise, <c>false</c>.</returns>
/// <remarks>
///     <para>
///         This predicate provides advanced filtering capabilities beyond tag-based filtering, allowing you to filter
///         handlers based on any combination of their metadata including type, priority, tags, and message type.
///         The predicate is evaluated for each potential handler descriptor before execution and is applied
///         in addition to tag-based filtering.
///     </para>
///     <para>
///         The <see cref="IHandlerDescriptor" /> provides access to:
///     </para>
///     <list type="bullet">
///         <item>
///             <description><see cref="IHandlerDescriptor.HandlerType" /> - The actual handler class type</description>
///         </item>
///         <item>
///             <description><see cref="IHandlerDescriptor.Priority" /> - The execution priority/order of the handler</description>
///         </item>
///         <item>
///             <description><see cref="IHandlerDescriptor.Tags" /> - Collection of tags associated with the handler</description>
///         </item>
///         <item>
///             <description><see cref="IHandlerDescriptor.MessageType" /> - The message type the handler processes</description>
///         </item>
///     </list>
///     <para>
///         <strong>Common Use Cases:</strong>
///     </para>
///     <list type="bullet">
///         <item>
///             <description>
///                 <strong>Environment-based filtering:</strong> Execute different handlers in development vs
///                 production
///             </description>
///         </item>
///         <item>
///             <description>
///                 <strong>Feature flag integration:</strong> Conditionally enable/disable handlers based on
///                 feature flags
///             </description>
///         </item>
///         <item>
///             <description><strong>Performance optimization:</strong> Skip expensive handlers during high-load scenarios</description>
///         </item>
///         <item>
///             <description>
///                 <strong>Namespace-based filtering:</strong> Execute only handlers from specific modules or
///                 assemblies
///             </description>
///         </item>
///         <item>
///             <description>
///                 <strong>Priority-based filtering:</strong> Execute only high-priority handlers during
///                 emergency modes
///             </description>
///         </item>
///         <item>
///             <description><strong>Type-based filtering:</strong> Exclude specific handler types for testing or debugging</description>
///         </item>
///     </list>
/// </remarks>
/// <example>
///     <para>
///         <strong>Basic type-based filtering:</strong>
///     </para>
///     <code><![CDATA[
/// EventHandlerFilterPredicate validationOnly = descriptor => 
///     descriptor.HandlerType.Name.Contains("Validation");
/// 
/// var settings = new EventMediationSettings
/// {
///     Routing = new EventMediationRoutingSettings
///     {
///         HandlerPredicate = validationOnly
///     }
/// };
/// ]]></code>
///     <para>
///         <strong>Environment-based filtering:</strong>
///     </para>
///     <code><![CDATA[
/// EventHandlerFilterPredicate skipAuditInDev = descriptor => 
///     Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Development" 
///         ? !descriptor.HandlerType.Name.Contains("Audit")
///         : true;
/// ]]></code>
///     <para>
///         <strong>Priority-based emergency mode:</strong>
///     </para>
///     <code><![CDATA[
/// EventHandlerFilterPredicate emergencyMode = descriptor => 
///     isEmergencyMode 
///         ? descriptor.Priority == 1 || descriptor.Tags.Contains("Emergency")
///         : true;
/// ]]></code>
///     <para>
///         <strong>Feature flag integration:</strong>
///     </para>
///     <code><![CDATA[
/// EventHandlerFilterPredicate featureFlagFilter = descriptor => 
///     descriptor.HandlerType.Namespace?.Contains("NewFeatures") == true
///         ? featureFlags.IsEnabled("NewEventHandlers")
///         : true;
/// ]]></code>
///     <para>
///         <strong>Namespace and assembly filtering:</strong>
///     </para>
///     <code><![CDATA[
/// EventHandlerFilterPredicate coreModulesOnly = descriptor => 
///     descriptor.HandlerType.Namespace?.StartsWith("MyApp.Core") == true ||
///     descriptor.HandlerType.Namespace?.StartsWith("MyApp.Business") == true;
/// ]]></code>
///     <para>
///         <strong>Complex conditional filtering:</strong>
///     </para>
///     <code><![CDATA[
/// EventHandlerFilterPredicate complexBusinessLogic = descriptor => 
/// {
///     var isPeakHours = DateTime.Now.Hour >= 9 && DateTime.Now.Hour <= 17;
///     var isExpensiveAnalytics = descriptor.HandlerType.Name.Contains("Analytics") && 
///                               descriptor.Order > 5;
///     var isCritical = descriptor.Tags.Contains("Critical") || 
///                    descriptor.Tags.Contains("Emergency");
///     
///     return isCritical || !(isPeakHours && isExpensiveAnalytics);
/// };
/// ]]></code>
///     <para>
///         <strong>Testing and debugging scenarios:</strong>
///     </para>
///     <code><![CDATA[
/// EventHandlerFilterPredicate integrationTestFilter = descriptor => 
///     isIntegrationTest 
///         ? !descriptor.HandlerType.Name.Contains("ExternalService") &&
///           !descriptor.HandlerType.Name.Contains("EmailNotification")
///         : true;
/// ]]></code>
///     <para>
///         <strong>Message type-based filtering:</strong>
///     </para>
///     <code><![CDATA[
/// EventHandlerFilterPredicate orderSpecificHandlers = descriptor => 
///     descriptor.MessageType.Name.Contains("Order")
///         ? descriptor.HandlerType.Name.Contains("Order") || descriptor.Tags.Contains("OrderProcessing")
///         : true;
/// ]]></code>
///     <para>
///         <strong>Combining multiple predicates:</strong>
///     </para>
///     <code><![CDATA[
/// // You can combine predicates using logical operators
/// EventHandlerFilterPredicate combined = descriptor => 
///     validationOnly(descriptor) && emergencyMode(descriptor);
/// 
/// // Or create helper methods for reusable logic
/// public static class EventHandlerFilters
/// {
///     public static EventHandlerFilterPredicate ByNamespace(string namespacePrefix) =>
///         descriptor => descriptor.HandlerType.Namespace?.StartsWith(namespacePrefix) == true;
///     
///     public static EventHandlerFilterPredicate ByTag(string tag) =>
///         descriptor => descriptor.Tags.Contains(tag);
///     
///     public static EventHandlerFilterPredicate ByPriority(int maxPriority) =>
///         descriptor => descriptor.Order <= maxPriority;
///     
///     public static EventHandlerFilterPredicate ExcludeNamespaces(params string[] namespaces) =>
///         descriptor => !namespaces.Any(ns => descriptor.HandlerType.Namespace?.StartsWith(ns) == true);
/// }
/// ]]></code>
///     <para>
///         <strong>Real-world production scenarios:</strong>
///     </para>
///     <code><![CDATA[
/// // Circuit breaker pattern - disable expensive handlers when system is under stress
/// EventHandlerFilterPredicate circuitBreakerFilter = descriptor =>
/// {
///     if (systemMetrics.CpuUsage > 80 || systemMetrics.MemoryUsage > 90)
///     {
///         // Only execute critical handlers during high load
///         return descriptor.Tags.Contains("Critical") || descriptor.Order <= 2;
///     }
///     return true;
/// };
/// 
/// // A/B testing - route events to different handler implementations
/// EventHandlerFilterPredicate abTestFilter = descriptor =>
/// {
///     if (descriptor.HandlerType.Name.Contains("ExperimentalHandler"))
///     {
///         return userSegmentService.IsUserInExperiment(currentUserId, "NewHandlerExperiment");
///     }
///     return true;
/// };
/// 
/// // Multi-tenant filtering - execute only handlers for current tenant
/// EventHandlerFilterPredicate tenantFilter = descriptor =>
/// {
///     var tenantAttribute = descriptor.HandlerType.GetCustomAttribute<TenantSpecificAttribute>();
///     return tenantAttribute == null || tenantAttribute.TenantId == currentTenantId;
/// };
/// ]]></code>
/// </example>
public delegate bool EventHandlerFilterPredicate(IHandlerDescriptor descriptor);