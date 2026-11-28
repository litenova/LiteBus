using System;
using LiteBus.Runtime.Abstractions.Exceptions;

namespace LiteBus.Messaging.Abstractions.Tenancy;

/// <summary>
///     Configures how tenancy is enforced for mediated messages.
/// </summary>
/// <remarks>
///     <para>
///         Reached through <c>MessageModuleBuilder.UseTenancy</c>. The tenancy guard, tenant policies, and tenant
///         sources read the validator configured here: a mediation running in a malformed tenant is denied with
///         <see cref="TenancyRefusalCodes.Invalid" />, distinct from
///         <see cref="TenancyRefusalCodes.TenantRequired" />.
///     </para>
///     <para>
///         With no validator configured, a tenant is accepted when it is not blank. Register a validator when tenant
///         identifiers follow a stricter shape, such as workspace GUIDs.
///     </para>
///     <para>
///         One validator serves a composition: repeating the same registration is a no-op, and configuring a different
///         one fails composition naming both.
///     </para>
/// </remarks>
/// <example>
///     <code><![CDATA[
/// registry.AddMessaging(messaging => messaging.UseTenancy(tenancy => tenancy
///     .ValidateTenant(tenantId => Guid.TryParse(tenantId, out _))));
/// ]]></code>
/// </example>
public sealed class TenancyOptions
{
    /// <summary>
    ///     The validation predicate, or <see langword="null" /> when validation was configured another way or not at all.
    /// </summary>
    private Func<string, bool>? _predicate;

    /// <summary>
    ///     The validator implementation type, or <see langword="null" /> when validation was configured another way or
    ///     not at all.
    /// </summary>
    private Type? _validatorType;

    /// <summary>
    ///     The pre-created validator, or <see langword="null" /> when validation was configured another way or not at
    ///     all.
    /// </summary>
    private ITenantIdentifierValidator? _validatorInstance;

    /// <summary>
    ///     Gets the validation predicate, or <see langword="null" /> when validation was configured another way or not
    ///     at all.
    /// </summary>
    internal Func<string, bool>? ValidatorPredicate => _predicate;

    /// <summary>
    ///     Gets the validator implementation type, or <see langword="null" /> when validation was configured another way
    ///     or not at all.
    /// </summary>
    internal Type? ValidatorType => _validatorType;

    /// <summary>
    ///     Gets the pre-created validator, or <see langword="null" /> when validation was configured another way or not
    ///     at all.
    /// </summary>
    internal ITenantIdentifierValidator? ValidatorInstance => _validatorInstance;

    /// <summary>
    ///     Validates tenant identifiers with a predicate.
    /// </summary>
    /// <param name="isValid">Decides whether a tenant identifier is well formed.</param>
    /// <returns>The current options.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="isValid" /> is <see langword="null" />.</exception>
    /// <exception cref="LiteBusConfigurationException">A different validator is already configured.</exception>
    /// <remarks>
    ///     <para>
    ///         The predicate runs for every mediation carrying a tenant, so it must be thread-safe and free of side
    ///         effects. It is shared for the life of the process.
    ///     </para>
    ///     <para>
    ///         Repeating the same predicate is a no-op, which is what repeated <c>UseTenancy</c> calls from several
    ///         composition helpers produce. A different validator fails configuration naming both.
    ///     </para>
    /// </remarks>
    /// <example>
    ///     <code><![CDATA[
    /// tenancy.ValidateTenant(tenantId => Guid.TryParse(tenantId, out _));
    /// ]]></code>
    /// </example>
    public TenancyOptions ValidateTenant(Func<string, bool> isValid)
    {
        ArgumentNullException.ThrowIfNull(isValid);

        if (Equals(_predicate, isValid))
        {
            return this;
        }

        ThrowIfValidatorConfigured("a validation predicate");

        _predicate = isValid;
        return this;
    }

    /// <summary>
    ///     Validates tenant identifiers with a validator constructed from the container.
    /// </summary>
    /// <typeparam name="TValidator">The validator implementation, constructed from the dispatch scope.</typeparam>
    /// <returns>The current options.</returns>
    /// <exception cref="LiteBusConfigurationException">A different validator is already configured.</exception>
    /// <remarks>
    ///     <para>
    ///         The overload to reach for when validation needs services, such as a tenant record lookup. The validator
    ///         receives the dispatch scope on every call, so it observes the same scoped services the handlers do.
    ///     </para>
    ///     <para>
    ///         Repeating the same validator type is a no-op. A different validator fails configuration naming both.
    ///     </para>
    /// </remarks>
    /// <example>
    ///     <code><![CDATA[
    /// tenancy.ValidateTenant<WorkspaceTenantValidator>();
    /// ]]></code>
    /// </example>
    public TenancyOptions ValidateTenant<TValidator>()
        where TValidator : class, ITenantIdentifierValidator
    {
        if (_validatorType == typeof(TValidator))
        {
            return this;
        }

        ThrowIfValidatorConfigured($"the '{typeof(TValidator).Name}' validator type");

        _validatorType = typeof(TValidator);
        return this;
    }

    /// <summary>
    ///     Validates tenant identifiers with a pre-created validator.
    /// </summary>
    /// <param name="validator">The validator, shared by every mediation for the life of the process.</param>
    /// <returns>The current options.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="validator" /> is <see langword="null" />.</exception>
    /// <exception cref="LiteBusConfigurationException">A different validator is already configured.</exception>
    /// <remarks>
    ///     The name says the lifetime, because a pre-created instance can only be shared. A validator that reads
    ///     scoped dependencies cannot be built here; use <see cref="ValidateTenant{TValidator}" /> for that.
    ///     Repeating the same instance is a no-op, and a different validator fails configuration naming both.
    /// </remarks>
    public TenancyOptions ValidateTenantInstance(ITenantIdentifierValidator validator)
    {
        ArgumentNullException.ThrowIfNull(validator);

        if (ReferenceEquals(_validatorInstance, validator))
        {
            return this;
        }

        ThrowIfValidatorConfigured($"an instance of '{validator.GetType().Name}'");

        _validatorInstance = validator;
        return this;
    }

    /// <summary>
    ///     Fails configuration when a validator is already configured from another source.
    /// </summary>
    /// <param name="incoming">Describes the validator being configured, in the words the error uses.</param>
    /// <exception cref="LiteBusConfigurationException">A validator is already configured.</exception>
    private void ThrowIfValidatorConfigured(string incoming)
    {
        var existing = DescribeConfiguredValidator();

        if (existing is not null)
        {
            throw new LiteBusConfigurationException(
                $"A tenant identifier validator is already configured from {existing}. Register one validator per "
                + $"composition; {incoming} conflicts with it.");
        }
    }

    /// <summary>
    ///     Describes the configured validator for a conflict message.
    /// </summary>
    /// <returns>The description, or <see langword="null" /> when no validator is configured.</returns>
    private string? DescribeConfiguredValidator()
    {
        if (_predicate is not null)
        {
            return "a validation predicate";
        }

        if (_validatorType is not null)
        {
            return $"the '{_validatorType.Name}' validator type";
        }

        if (_validatorInstance is not null)
        {
            return $"an instance of '{_validatorInstance.GetType().Name}'";
        }

        return null;
    }
}
