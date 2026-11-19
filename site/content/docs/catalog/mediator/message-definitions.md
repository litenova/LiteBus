# Message Definitions

- **ID**: `mediator.message-definitions`
- **Name**: Message definitions and metadata
- **Maturity**: GA
- **Summary**: Resolves declarative per-message metadata from definitions, attributes, defaults, and conventions, once at registration, for pipeline stages to read.
- **Guide**: [Message definitions](../../concepts/message-definitions.md)

## What It Does

A message definition declares facts about a message that stages outside the handler need: whether it is audited, what permission it requires, and anything an application chooses to add. LiteBus resolves them when the message registry is built and exposes the result on `IMessageDescriptor.Metadata`, so a pipeline stage reads a dictionary rather than reflecting on every dispatch.

Four sources populate metadata, in precedence order, and all contribute values keyed by the same value type:

1. Definitions, which win over every other source, so a definition wins over an attribute declaring the same value type.
2. Attributes on the message type that implement `IMessageDeclarationSource`, each converted to the value type it names. Attributes that do not implement it are not metadata and are never collected, which keeps the collection bounded.
3. Defaults declared with `DeclareDefault`, which cover a family of messages through a shared base type or marker interface unless the message states its own value.
4. Conventions declared with `DeclareConvention`, which derive a value from the message type itself for every message that states nothing through the first three sources.

Declarations are keyed by value type, in both definition shapes. `IMessageDefinition<TMessage>` declares any number of them from one `Describe(IMessageDeclarations)` method and is the usual choice. `IMessageDefinition<TMessage, TValue>` pins one declaration to its value type at compile time, which makes it the better choice for a message declaring exactly one thing, and is why `IAuditDefinition<TMessage>` is built on it. Past one value, every later value has to be written as an explicit interface implementation naming both types again. Applications may declare their own value types either way, and LiteBus applies them without understanding them.

A declaration covers the message type it names and every message assignable to it, so a definition over a base type or marker interface describes a family of messages. The most derived declaration wins.

## Public Surface

```csharp
public sealed record RequiredPermission(string Name);

public interface IPermissionDefinition<TMessage> : IMessageDefinition<TMessage, RequiredPermission>
    where TMessage : notnull
{
    RequiredPermission Required { get; }

    RequiredPermission IMessageDefinition<TMessage, RequiredPermission>.Value => Required;
}

public sealed class PlaceOrderCommandDefinition :
    IAuditDefinition<PlaceOrderCommand>,
    IPermissionDefinition<PlaceOrderCommand>
{
    public AuditDeclaration Audit => AuditDeclaration.Audited("orders.place-order");
    public RequiredPermission Required => new("orders.place");
}
```

| API | Role |
| --- | --- |
| `IMessageDefinition` | Non-generic marker used for discovery |
| `IMessageDefinition<TMessage, TValue>` | One declaration, keyed by `TValue` and checked by the compiler |
| `IMessageDefinition<TMessage>` | Any number of declarations from one `Describe(IMessageDeclarations)` method |
| `IMessageDeclarations` | `Declare<TValue>`, `Audited`, `NotAudited`, `Authorized`, `AuthorizationExempt`, `Exempt<TValue>` |
| `IMessageDeclarationExemptionSource` | Implemented by an attribute that records an exemption. Exemptions are aggregated into one `DeclarationExemptions` value |
| `[DeclarationExempt(typeof(TValue), rationale)]` | Records that a message deliberately declares nothing for one metadata type. Repeatable, and exemptions are aggregated into `DeclarationExemptions` |
| `[MessageDeclaration(typeof(TValue))]` | States on an attribute class which metadata value it declares, so analyzers can read it. Registration fails when it names a different type than `IMessageDeclarationSource.DeclarationType` |
| `MessageModuleBuilder.DeclareDefault<TScope, TValue>(value)` | Declares a value for every message assignable to a marker, including an interface scope. A message that states its own value overrides it. A default applies only to messages assignable to its scope and never satisfies another scope's requirement |
| `MessageModuleBuilder.DeclareConvention<TValue>(convention)` | Derives a value from the message type for every registered message with no definition, attribute, or default value for `TValue`. Returns null when the convention does not apply. One convention per value type |
| `MessageDeclarationItem` / `IMessageWriter.AddDeclaration` | A declaration made from composition code rather than a definition class |
| `MessageModuleBuilder.RequireDeclaration<TValue>()` / `RequireDeclaration<TValue, TScope>()` / `RequireDeclaration<TValue>(predicate, description)` | With `DeclarationEnforcement.Fail`, fails composition for every registered message that neither declares the value nor records an exemption. With `Report`, lists them through the `litebus.declarations` probe instead. Unscoped, marker-scoped, or predicate-scoped |
| `DeclarationEnforcement` | `Fail` fails composition naming every offender. `Report` lets composition succeed and reports `Degraded` through the `litebus.declarations` probe |
| `MessageDeclarationException` | Names every offending message, grouped by the omitted declaration and the violated scope |
| `MessageModuleBuilder.ValidateComposition(Action<IMessageCatalog>)` | Runs an application convention over every registered message at composition |
| `IMessageCatalog` / `MessageCatalogEntry` | The read-only view a composition check asserts over, with `Count`, an `Audited()` filter, and `MissingDeclaration` for the messages missing a reported declaration |
| `DeclarationReportDiagnosticCheck` | The `litebus.declarations` probe. Reports `Degraded` listing every undeclared message, `Healthy` when every message states a position |
| `IMessageDeclarationSource` | Implemented by an attribute that declares metadata |
| `IMessageMetadata` | Read side exposed on the message descriptor |
| `IMessageDescriptor.Metadata` | Resolved metadata for one message type |
| `IMessageMetadataAccessor` | Reads resolved metadata from application code through `ForMessage` and `TryGet`. An unregistered runtime type reads the metadata of the descriptor it mediates through, and only a type nothing covers raises `MessageMetadataNotFoundException` |
| `IAuditDefinition<TMessage>` | The audit declaration shipped by LiteBus |
| `IAuthorizationDefinition<TMessage>` | The authorization declaration shipped by LiteBus |

## Packages

- `LiteBus.Messaging`
- `LiteBus.Messaging.Abstractions`

## Requires

- `mediator.module-registration`

## Invariants

- A definition type must expose a parameterless constructor, public or non-public.
- A definition type must declare at least one value, or registration throws `LiteBusConfigurationException`.
- A declared value must not be null, and must be assignable to the value type it is keyed under.
- Only attributes implementing `IMessageDeclarationSource` become metadata.
- Precedence is definition, then attribute, then default, then convention. A definition overrides an attribute of the same value type, a default applies only when neither states one, and a convention applies only when none of the first three states one.
- A declaration covers every message assignable to the type it names. The most derived declaration wins.
- A default applies only to messages assignable to its scope and never satisfies another scope's requirement.
- Two definitions declaring the same value type for one message throw `LiteBusConfigurationException` at registration.
- Two declarations covering one message where neither type is more derived than the other throw `LiteBusConfigurationException`.
- Two defaults for one scope and value type are a configuration error, as are a default and a definition both declared against the same message.
- One value type has one convention. A second convention for the same value type fails composition.
- A convention returning null leaves the message undeclared for that value type. A convention never overrides an exemption.
- A convention runs once per registered concrete message at composition, after every module has registered its messages and before declaration requirements are checked, so a convention value satisfies a requirement.
- Metadata is resolved once per message type at registration, not per dispatch.
- Metadata follows the descriptor of the message type. A message type mediated without being registered is registered on its first dispatch, and its descriptor carries the declarations made for its base types and interfaces, exactly as if it had been registered. Reading metadata for a type that was never registered or dispatched falls back to the most-derived registered supertype. Only a type nothing registered covers raises `MessageMetadataNotFoundException`, and an ambiguous fallback raises `AmbiguousMessageResolveException`.
- Open generic message shapes are matched exactly, because assignability between generic type definitions is not meaningful.
- A `RequireDeclaration` requirement is satisfied by a declaration of the value type or by a recorded exemption for it. With `Fail` enforcement, every other message fails composition, named in full rather than one at a time. With `Report` enforcement, composition succeeds and the `litebus.declarations` probe lists them instead.
- A marker-scoped requirement covers every message assignable to the marker and leaves the messages outside it alone. A predicate requirement names its own scope in the error.
- The `litebus.declarations` probe reports `Degraded` listing every undeclared message and `Healthy` when every message in scope states a position. One probe lists every report-mode requirement's offenders.
- `MissingDeclaration` returns one entry per concrete message in scope that neither declares the value nor records an exemption from it, in registration order. Abstract types and interfaces are skipped.
- A `Describe` body that declares nothing, or that declares one value type twice, fails registration.

## Non-Goals

- Interpreting application-owned declared values. LiteBus carries the declaration. Enforcement stays in the application.
- Replacing handler-level configuration such as `[HandlerPriority]` and `[HandlerTag]`, which describe an implementation rather than a use case.
- Runtime mutation of metadata after the registry is built.

## Observability

No definition-specific meter or activity source is emitted. Binding failures surface as `LiteBusConfigurationException` during module build, so a malformed definition fails at startup rather than at dispatch. A composition with a report-mode requirement registers the `litebus.declarations` diagnostic probe, which reports `Degraded` listing every undeclared message and `Healthy` when every message states a position.

## Test Coverage

### Covered

| Test method | Project |
| --- | --- |
| `A_definition_declared_command_produces_a_record` | `LiteBus.Mediator.UnitTests` |
| `A_definition_takes_precedence_over_an_attribute` | `LiteBus.Mediator.UnitTests` |
| `An_application_owned_declaration_is_applied_without_LiteBus_knowing_it` | `LiteBus.Mediator.UnitTests` |
| `An_attribute_is_normalized_to_the_declaration_a_definition_would_contribute` | `LiteBus.Mediator.UnitTests` |
| `Attributes_that_do_not_declare_metadata_are_not_collected` | `LiteBus.Mediator.UnitTests` |
| `A_declaration_over_a_marker_interface_covers_the_messages_beneath_it` | `LiteBus.Mediator.UnitTests` |
| `Two_definitions_declaring_the_same_value_for_one_message_are_reported_at_registration` | `LiteBus.Mediator.UnitTests` |
| `One_describe_method_declares_several_values_for_one_message` | `LiteBus.Mediator.UnitTests` |
| `Describe_records_an_audit_exemption_and_an_application_exemption_in_one_set` | `LiteBus.Mediator.UnitTests` |
| `A_requirement_scoped_to_a_marker_leaves_the_messages_outside_it_alone` | `LiteBus.Mediator.UnitTests` |
| `A_requirement_scoped_to_a_marker_still_reports_a_message_inside_it` | `LiteBus.Mediator.UnitTests` |
| `A_requirement_scoped_to_the_command_axis_does_not_reach_queries` | `LiteBus.Mediator.UnitTests` |
| `An_unscoped_requirement_still_covers_every_message` | `LiteBus.Mediator.UnitTests` |
| `A_predicate_requirement_names_its_own_scope_in_the_error` | `LiteBus.Mediator.UnitTests` |
| `Declaring_one_value_type_twice_in_one_describe_is_reported` | `LiteBus.Mediator.UnitTests` |
| `A_describe_that_declares_nothing_is_reported` | `LiteBus.Mediator.UnitTests` |
| `A_composition_check_asserts_over_every_registered_message` | `LiteBus.Mediator.UnitTests` |
| `The_catalog_holds_every_registered_message_and_filters_the_audited_ones` | `LiteBus.Mediator.UnitTests` |
| `Duplicate_audit_actions_are_reported_at_composition` | `LiteBus.Mediator.UnitTests` |
| `An_audit_action_breaking_the_format_is_reported_at_composition` | `LiteBus.Mediator.UnitTests` |
| `The_default_audit_action_format_accepts_the_documented_convention` | `LiteBus.Mediator.UnitTests` |
| `An_undeclared_message_fails_composition_naming_the_message_and_the_value` | `LiteBus.Mediator.UnitTests` |
| `Every_offender_is_named_rather_than_the_first` | `LiteBus.Mediator.UnitTests` |
| `A_recorded_exemption_satisfies_the_requirement` | `LiteBus.Mediator.UnitTests` |
| `An_exemption_for_another_value_does_not_satisfy_this_requirement` | `LiteBus.Mediator.UnitTests` |
| `An_exemption_is_readable_with_its_rationale` | `LiteBus.Mediator.UnitTests` |
| `Several_exemptions_on_one_message_are_aggregated` | `LiteBus.Mediator.UnitTests` |
| `A_default_covers_every_message_in_the_family` | `LiteBus.Mediator.UnitTests` |
| `A_message_that_states_its_own_position_keeps_it` | `LiteBus.Mediator.UnitTests` |
| `A_default_does_not_reach_a_message_outside_the_family` | `LiteBus.Mediator.UnitTests` |
| `A_default_for_an_interface_scope_keys_a_concrete_command` | `LiteBus.Mediator.UnitTests` |
| `A_default_declared_before_the_messages_are_registered_still_reaches_them` | `LiteBus.Mediator.UnitTests` |
| `A_default_satisfies_a_requirement_scoped_to_the_same_family` | `LiteBus.Mediator.UnitTests` |
| `Two_defaults_for_one_scope_and_value_type_are_reported` | `LiteBus.Mediator.UnitTests` |
| `A_default_and_a_definition_for_the_same_message_are_reported` | `LiteBus.Mediator.UnitTests` |
| `A_convention_supplies_a_value_when_nothing_else_states_one` | `LiteBus.Mediator.UnitTests` |
| `A_definition_beats_a_convention` | `LiteBus.Mediator.UnitTests` |
| `An_attribute_beats_a_convention` | `LiteBus.Mediator.UnitTests` |
| `A_default_beats_a_convention` | `LiteBus.Mediator.UnitTests` |
| `A_convention_returning_null_leaves_the_message_undeclared` | `LiteBus.Mediator.UnitTests` |
| `A_convention_can_key_only_the_messages_carrying_a_key` | `LiteBus.Mediator.UnitTests` |
| `A_convention_does_not_override_an_exemption` | `LiteBus.Mediator.UnitTests` |
| `A_convention_satisfies_a_requirement` | `LiteBus.Mediator.UnitTests` |
| `A_second_convention_for_one_value_type_is_reported` | `LiteBus.Mediator.UnitTests` |
| `Report_mode_does_not_fail_composition` | `LiteBus.Mediator.UnitTests` |
| `Report_mode_registers_the_declarations_probe` | `LiteBus.Mediator.UnitTests` |
| `The_probe_reports_degraded_listing_every_undeclared_message` | `LiteBus.Mediator.UnitTests` |
| `The_probe_reports_healthy_when_every_message_states_a_position` | `LiteBus.Mediator.UnitTests` |
| `The_catalog_lists_the_messages_missing_a_reported_declaration` | `LiteBus.Mediator.UnitTests` |
| `The_catalog_limits_the_report_to_the_requirement_scope` | `LiteBus.Mediator.UnitTests` |
| `A_satisfied_fail_requirement_and_an_unsatisfied_report_requirement_compose_with_a_degraded_probe` | `LiteBus.Mediator.UnitTests` |

### Untested

- Definitions registered for open generic message types.
- Two declarations covering one message through unrelated marker interfaces.
- Very large declaration counts on a single definition class.

### Out-of-Scope

- Durable message contract registration, covered by `IContractWriter`.

## References

- [Message definitions](../../concepts/message-definitions.md)
- [Auditing](../../concepts/auditing.md)
