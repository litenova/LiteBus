using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Messaging;

/// <summary>
///     Builds sample message instances with non-default values for host validation round trips.
/// </summary>
/// <remarks>
///     <para>
///         Registered as the <see cref="IMessageSampleFactory" /> LiteBus default when the messaging module builds
///         without an explicit sample factory. A sample carries values a serializer must preserve: a non-empty
///         string, non-zero numbers, a non-empty GUID, a fixed instant, and the first enum value. Defaults would
///         hide the defect class the round-trip check hunts, because a value the serializer drops reads back as
///         the default it already held.
///     </para>
///     <para>
///         Construction tries, in order: a registered sample for the exact type; static factories named
///         <c>TryParse</c>, <c>TryCreate</c>, <c>Create</c>, <c>From</c>, or <c>Parse</c>, each filled with
///         sampled arguments; then the public constructors in ascending parameter count. A factory or
///         constructor that rejects the sampled arguments is skipped and the next one is tried. Reference
///         types keep parameterless-first order, while value types try parameterized constructors first and
///         use the default value only when the type declares no constructor at all, because the default of a
///         validating struct is the invalid value the recipe exists to avoid.
///     </para>
///     <para>
///         A rejected argument is never retried with another value: when no factory or constructor accepts
///         the sampled arguments, the run reports the member path with its type and reason, and the host
///         validation check pairs that diagnosis with the <c>RegisterSample</c> remedy. A guarded string
///         needs a registered sample.
///     </para>
///     <para>
///         An abstract class samples through the same factories and constructors, then through parameterless
///         static members returning the type or a non-empty enumerable of it, and finally through a concrete
///         derived type from the same assembly, ordered by full name. Interfaces stay unsampleable. After
///         construction the factory sets writable public properties, skipping one whose setter throws so a
///         guarded setter does not fail the whole sample. Nested reference values recurse one level with cycle
///         protection, and anything deeper stays at its default.
///     </para>
/// </remarks>
public sealed class DefaultMessageSampleFactory : IMessageSampleFactory
{
    /// <summary>
    ///     The string every sampled string value carries.
    /// </summary>
    private const string SampleString = "sample";

    /// <summary>
    ///     The character every sampled character value carries.
    /// </summary>
    private const char SampleCharacter = 's';

    /// <summary>
    ///     The section of a registered-sample exception message kept in a failure reason.
    /// </summary>
    private const int MaxSampleExceptionLength = 200;

    /// <summary>
    ///     The GUID every sampled GUID value carries.
    /// </summary>
    private static readonly Guid SampleGuid = new("11111111-1111-1111-1111-111111111111");

    /// <summary>
    ///     The instant every sampled date and time value carries.
    /// </summary>
    private static readonly DateTime SampleInstant = new(2024, 5, 6, 7, 8, 9, DateTimeKind.Utc);

    /// <summary>
    ///     The URI every sampled URI value carries.
    /// </summary>
    private static readonly Uri SampleUri = new("https://example.com/sample");

    /// <summary>
    ///     The version every sampled version value carries.
    /// </summary>
    private static readonly Version SampleVersion = new(1, 2);

    /// <summary>
    ///     The static factory method name groups, in trial order. A method matches when its name equals the
    ///     group or starts with it, so <c>FromCode</c> matches <c>From</c>.
    /// </summary>
    private static readonly string[] FactoryMethodGroups = ["TryParse", "TryCreate", "Create", "From", "Parse"];

    /// <summary>
    ///     The per-type samples registered through the message module builder, or <see langword="null" /> when
    ///     the composition registered none.
    /// </summary>
    private readonly IReadOnlyDictionary<Type, Func<object?>>? _samples;

    /// <summary>
    ///     Initializes a new instance of the <see cref="DefaultMessageSampleFactory" /> class.
    /// </summary>
    public DefaultMessageSampleFactory()
    {
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="DefaultMessageSampleFactory" /> class with registered samples.
    /// </summary>
    /// <param name="samples">The sample factories by sampled type.</param>
    internal DefaultMessageSampleFactory(IReadOnlyDictionary<Type, Func<object?>> samples)
    {
        ArgumentNullException.ThrowIfNull(samples);

        _samples = samples;
    }

    /// <inheritdoc />
    public ValueTask<object?> CreateSampleAsync(Type messageType, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(messageType);

        return new ValueTask<object?>(TryCreateSample(messageType, true, new SamplingContext()));
    }

    /// <inheritdoc />
    public ValueTask<MessageSampleFailure?> DescribeFailureAsync(Type messageType, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(messageType);

        var context = new SamplingContext();

        if (TryCreateSample(messageType, true, context) is not null)
        {
            return new ValueTask<MessageSampleFailure?>((MessageSampleFailure?) null);
        }

        return new ValueTask<MessageSampleFailure?>(
            context.Failure ?? new MessageSampleFailure(
                messageType,
                string.Empty,
                "no static factory or constructor accepts sample values"));
    }

    /// <summary>
    ///     Builds a sample value for one type, or answers <see langword="null" /> when the type cannot be sampled.
    /// </summary>
    /// <param name="type">The type to sample.</param>
    /// <param name="fillNested">
    ///     <see langword="true" /> to fill nested reference values; <see langword="false" /> to fill leaf values
    ///     only, which bounds sampling to one level of nested objects.
    /// </param>
    /// <param name="context">The sampling context carrying cycle protection and the failure diagnosis.</param>
    /// <returns>The sample value, or <see langword="null" /> when the type cannot be sampled.</returns>
    private object? TryCreateSample(Type type, bool fillNested, SamplingContext context)
    {
        if (_samples is not null && _samples.TryGetValue(type, out var registered))
        {
            return TryCreateRegistered(type, registered, context);
        }

        if (type.IsInterface)
        {
            context.ReportFailure(type, "interfaces cannot be sampled");

            return null;
        }

        if (Nullable.GetUnderlyingType(type) is { } underlying)
        {
            var value = TryCreateSample(underlying, fillNested, context);

            if (value is null)
            {
                return null;
            }

            try
            {
                return Activator.CreateInstance(type, value);
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException or TargetInvocationException or MemberAccessException)
            {
                context.ReportFailure(type, "the nullable value cannot be constructed");

                return null;
            }
        }

        if (TryCreateLeaf(type, out var leaf))
        {
            return leaf;
        }

        if (type.IsArray)
        {
            return TryCreateArray(type, context);
        }

        if (TryGetDictionaryArguments(type, out var keyType, out var valueType))
        {
            return TryCreateDictionary(keyType!, valueType!, context);
        }

        if (TryGetEnumerableElementType(type, out var elementType))
        {
            return TryCreateList(type, elementType!, context);
        }

        return TryCreateObject(type, fillNested, context);
    }

    /// <summary>
    ///     Builds a sample through a registered sample factory.
    /// </summary>
    /// <param name="type">The sampled type.</param>
    /// <param name="sample">The factory answering the sample.</param>
    /// <param name="context">The sampling context carrying cycle protection and the failure diagnosis.</param>
    /// <returns>The registered sample, or <see langword="null" /> when it answers nothing usable.</returns>
    /// <remarks>
    ///     The catch stays broad because a registered sample is application code: it throws exception types the
    ///     factory cannot enumerate, and a throwing sample must diagnose like any other sampling failure rather
    ///     than crash validation with no member path.
    /// </remarks>
#pragma warning disable CA1031 // A registered sample is application code; any failure becomes a diagnosis, as the remarks explain.
    private static object? TryCreateRegistered(Type type, Func<object?> sample, SamplingContext context)
    {
        object? value;

        try
        {
            value = sample();
        }
        catch (Exception exception)
        {
            context.ReportFailure(
                type,
                $"the registered sample threw '{TypeName(exception.GetType())}': {Truncate(exception.Message)}");

            return null;
        }

        if (value is null)
        {
            context.ReportFailure(type, "the registered sample returned no value");

            return null;
        }

        if (!type.IsInstanceOfType(value))
        {
            context.ReportFailure(
                type,
                $"the registered sample returned '{TypeName(value.GetType())}', which is not assignable to '{TypeName(type)}'");

            return null;
        }

        return value;
    }
#pragma warning restore CA1031

    /// <summary>
    ///     Builds a leaf sample value, which needs no construction and carries no nested values.
    /// </summary>
    /// <param name="type">The type to sample.</param>
    /// <param name="leaf">When this method returns <see langword="true" />, the sample value.</param>
    /// <returns><see langword="true" /> when the type is a sampled leaf; otherwise, <see langword="false" />.</returns>
    private static bool TryCreateLeaf(Type type, out object? leaf)
    {
        if (type.IsEnum)
        {
            var values = Enum.GetValues(type);

            leaf = values.Length > 0 ? values.GetValue(0) : Enum.ToObject(type, 0);

            return true;
        }

        switch (Type.GetTypeCode(type))
        {
            case TypeCode.Boolean:
                leaf = true;

                return true;

            case TypeCode.Char:
                leaf = SampleCharacter;

                return true;

            case TypeCode.String:
                leaf = SampleString;

                return true;

            case TypeCode.DateTime:
                leaf = SampleInstant;

                return true;

            case TypeCode.SByte:
            case TypeCode.Byte:
            case TypeCode.Int16:
            case TypeCode.UInt16:
            case TypeCode.Int32:
            case TypeCode.UInt32:
            case TypeCode.Int64:
            case TypeCode.UInt64:
            case TypeCode.Single:
            case TypeCode.Double:
            case TypeCode.Decimal:
                try
                {
                    leaf = Convert.ChangeType(1, type, CultureInfo.InvariantCulture);

                    return true;
                }
                catch (Exception exception) when (exception is InvalidCastException or FormatException or OverflowException or ArgumentException)
                {
                    leaf = null;

                    return false;
                }

            default:
                break;
        }

        if (type == typeof(Guid))
        {
            leaf = SampleGuid;

            return true;
        }

        if (type == typeof(DateTimeOffset))
        {
            leaf = new DateTimeOffset(SampleInstant);

            return true;
        }

        if (type == typeof(TimeSpan))
        {
            leaf = TimeSpan.FromHours(1);

            return true;
        }

        if (type == typeof(DateOnly))
        {
            leaf = DateOnly.FromDateTime(SampleInstant);

            return true;
        }

        if (type == typeof(TimeOnly))
        {
            leaf = TimeOnly.FromDateTime(SampleInstant);

            return true;
        }

        if (type == typeof(Uri))
        {
            leaf = SampleUri;

            return true;
        }

        if (type == typeof(Version))
        {
            leaf = SampleVersion;

            return true;
        }

        leaf = null;

        return false;
    }

    /// <summary>
    ///     Determines whether a type samples as a leaf value, which a nested object fills even one level down.
    /// </summary>
    /// <param name="type">The type to test.</param>
    /// <returns><see langword="true" /> when the type samples without construction.</returns>
    private static bool IsLeaf(Type type)
    {
        if (Nullable.GetUnderlyingType(type) is { } underlying)
        {
            return IsLeaf(underlying);
        }

        return TryCreateLeaf(type, out _);
    }

    /// <summary>
    ///     Builds a single-element array sample.
    /// </summary>
    /// <param name="type">The array type to sample.</param>
    /// <param name="context">The sampling context carrying cycle protection and the failure diagnosis.</param>
    /// <returns>The sample array, or <see langword="null" /> when the element type cannot be sampled.</returns>
    private Array? TryCreateArray(Type type, SamplingContext context)
    {
        if (type.GetArrayRank() != 1 || type.GetElementType() is not { } elementType)
        {
            context.ReportFailure(type, "only single-dimensional arrays can be sampled");

            return null;
        }

        context.Path.Add("[0]");

        try
        {
            var element = TryCreateSample(elementType, false, context);

            if (element is null)
            {
                context.ReportFailure(elementType, "the array element type cannot be sampled");

                return null;
            }

            var array = Array.CreateInstance(elementType, 1);
            array.SetValue(element, 0);

            return array;
        }
        finally
        {
            context.Path.RemoveAt(context.Path.Count - 1);
        }
    }

    /// <summary>
    ///     Reads the key and value types of a dictionary shape.
    /// </summary>
    /// <param name="type">The type to inspect.</param>
    /// <param name="keyType">When this method returns <see langword="true" />, the dictionary key type.</param>
    /// <param name="valueType">When this method returns <see langword="true" />, the dictionary value type.</param>
    /// <returns><see langword="true" /> when the type is a closed dictionary shape.</returns>
    private static bool TryGetDictionaryArguments(Type type, out Type? keyType, out Type? valueType)
    {
        if (type.IsGenericType)
        {
            var definition = type.GetGenericTypeDefinition();

            if (definition == typeof(Dictionary<,>)
                || definition == typeof(IDictionary<,>)
                || definition == typeof(IReadOnlyDictionary<,>))
            {
                var arguments = type.GetGenericArguments();
                keyType = arguments[0];
                valueType = arguments[1];

                return true;
            }
        }

        foreach (var contract in type.GetInterfaces())
        {
            if (contract.IsGenericType && contract.GetGenericTypeDefinition() == typeof(IDictionary<,>))
            {
                var arguments = contract.GetGenericArguments();
                keyType = arguments[0];
                valueType = arguments[1];

                return true;
            }
        }

        keyType = null;
        valueType = null;

        return false;
    }

    /// <summary>
    ///     Builds a single-entry dictionary sample.
    /// </summary>
    /// <param name="keyType">The dictionary key type.</param>
    /// <param name="valueType">The dictionary value type.</param>
    /// <param name="context">The sampling context carrying cycle protection and the failure diagnosis.</param>
    /// <returns>The sample dictionary, or <see langword="null" /> when the key or value cannot be sampled.</returns>
    private object? TryCreateDictionary(Type keyType, Type valueType, SamplingContext context)
    {
        context.Path.Add("[key]");

        object? key;

        try
        {
            key = TryCreateSample(keyType, false, context);
        }
        finally
        {
            context.Path.RemoveAt(context.Path.Count - 1);
        }

        if (key is null)
        {
            context.ReportFailure(keyType, "the dictionary key type cannot be sampled");

            return null;
        }

        context.Path.Add("[value]");

        object? value;

        try
        {
            value = TryCreateSample(valueType, false, context);
        }
        finally
        {
            context.Path.RemoveAt(context.Path.Count - 1);
        }

        if (value is null)
        {
            context.ReportFailure(valueType, "the dictionary value type cannot be sampled");

            return null;
        }

        try
        {
            var dictionary = (IDictionary) Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(keyType, valueType))!;
            dictionary.Add(key, value);

            return dictionary;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or TargetInvocationException or MemberAccessException)
        {
            context.ReportFailure(typeof(Dictionary<,>).MakeGenericType(keyType, valueType), "the dictionary cannot be constructed");

            return null;
        }
    }

    /// <summary>
    ///     Reads the element type of a list shape. Dictionaries answer through
    ///     <see cref="TryGetDictionaryArguments" /> instead and never reach here.
    /// </summary>
    /// <param name="type">The type to inspect.</param>
    /// <param name="elementType">When this method returns <see langword="true" />, the element type.</param>
    /// <returns><see langword="true" /> when the type is a closed enumerable shape.</returns>
    private static bool TryGetEnumerableElementType(Type type, out Type? elementType)
    {
        if (type.IsGenericType)
        {
            var definition = type.GetGenericTypeDefinition();

            if (definition == typeof(List<>)
                || definition == typeof(IList<>)
                || definition == typeof(ICollection<>)
                || definition == typeof(IEnumerable<>)
                || definition == typeof(IReadOnlyList<>)
                || definition == typeof(IReadOnlyCollection<>))
            {
                elementType = type.GetGenericArguments()[0];

                return true;
            }
        }

        foreach (var contract in type.GetInterfaces())
        {
            if (contract.IsGenericType && contract.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            {
                elementType = contract.GetGenericArguments()[0];

                return true;
            }
        }

        elementType = null;

        return false;
    }

    /// <summary>
    ///     Builds a single-element list sample.
    /// </summary>
    /// <param name="type">The list shape to sample.</param>
    /// <param name="elementType">The element type.</param>
    /// <param name="context">The sampling context carrying cycle protection and the failure diagnosis.</param>
    /// <returns>The sample list, or <see langword="null" /> when the element type cannot be sampled.</returns>
    private object? TryCreateList(Type type, Type elementType, SamplingContext context)
    {
        context.Path.Add("[0]");

        object? element;

        try
        {
            element = TryCreateSample(elementType, false, context);
        }
        finally
        {
            context.Path.RemoveAt(context.Path.Count - 1);
        }

        if (element is null)
        {
            context.ReportFailure(elementType, "the list element type cannot be sampled");

            return null;
        }

        try
        {
            if (type.IsInterface || type.IsAbstract || type == typeof(List<>).MakeGenericType(elementType))
            {
                IList list = (IList) Activator.CreateInstance(typeof(List<>).MakeGenericType(elementType))!;
                list.Add(element);

                return list;
            }

            var instance = Activator.CreateInstance(type);

            if (instance is IList concrete)
            {
                concrete.Add(element);

                return concrete;
            }

            if (instance is not null && TryAddThroughAddMethod(instance, element))
            {
                return instance;
            }

            context.ReportFailure(type, "the collection cannot be constructed");

            return null;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or TargetInvocationException or MemberAccessException)
        {
            context.ReportFailure(type, "the collection cannot be constructed");

            return null;
        }
    }

    /// <summary>
    ///     Adds one element through a public <c>Add</c> method, for collections that are not lists.
    /// </summary>
    /// <param name="instance">The collection instance.</param>
    /// <param name="element">The element to add.</param>
    /// <returns><see langword="true" /> when an element was added; otherwise, <see langword="false" />.</returns>
    private static bool TryAddThroughAddMethod(object instance, object element)
    {
        foreach (var method in instance.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!string.Equals(method.Name, "Add", StringComparison.Ordinal))
            {
                continue;
            }

            var parameters = method.GetParameters();

            if (parameters.Length != 1 || !parameters[0].ParameterType.IsInstanceOfType(element))
            {
                continue;
            }

            try
            {
                method.Invoke(instance, [element]);

                return true;
            }
            catch (Exception exception) when (exception is ArgumentException or TargetInvocationException or MemberAccessException or TargetParameterCountException)
            {
                return false;
            }
        }

        return false;
    }

    /// <summary>
    ///     Builds a sample object through static factories and constructors, and for abstract classes through
    ///     instance members and derived types.
    /// </summary>
    /// <param name="type">The type to sample.</param>
    /// <param name="fillNested">
    ///     <see langword="true" /> to fill nested reference values; <see langword="false" /> to fill leaf values
    ///     only, which bounds sampling to one level of nested objects.
    /// </param>
    /// <param name="context">The sampling context carrying cycle protection and the failure diagnosis.</param>
    /// <returns>The sample instance, or <see langword="null" /> when the type cannot be constructed.</returns>
    private object? TryCreateObject(Type type, bool fillNested, SamplingContext context)
    {
        if (!context.Active.Add(type))
        {
            context.ReportFailure(type, "the type refers to itself while sampling");

            return null;
        }

        try
        {
            foreach (var factory in FindFactoryMethods(type))
            {
                if (TryInvokeFactory(factory, type, context, out var produced) && produced is not null)
                {
                    FillProperties(produced, produced.GetType(), fillNested, context);

                    return produced;
                }
            }

            var constructors = type.GetConstructors();
            OrderConstructors(type, constructors);

            foreach (var constructor in constructors)
            {
                if (TryInvokeConstructor(constructor, context, out var candidate) && candidate is not null)
                {
                    FillProperties(candidate, candidate.GetType(), fillNested, context);

                    return candidate;
                }
            }

            if (type.IsAbstract)
            {
                if (TryCreateFromInstanceMember(type, context, out var member) && member is not null)
                {
                    return member;
                }

                if (TryCreateFromDerivedType(type, fillNested, context, out var derived) && derived is not null)
                {
                    return derived;
                }

                context.ReportFailure(type, "no concrete derived type in the same assembly can be sampled");

                return null;
            }

            if (type.IsValueType && constructors.Length == 0)
            {
                var instance = Activator.CreateInstance(type)!;
                FillProperties(instance, instance.GetType(), fillNested, context);

                return instance;
            }

            context.ReportFailure(type, "no static factory or constructor accepts sample values");

            return null;
        }
        finally
        {
            context.Active.Remove(type);
        }
    }

    /// <summary>
    ///     Finds the static factory methods that can build one type, in trial order.
    /// </summary>
    /// <param name="type">The type to build.</param>
    /// <returns>The factory methods, ordered by name group, name, then parameter count.</returns>
    private static List<MethodInfo> FindFactoryMethods(Type type)
    {
        List<MethodInfo> matches = [];

        foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Static))
        {
            if (method.IsSpecialName || method.ContainsGenericParameters || method.DeclaringType == typeof(object))
            {
                continue;
            }

            if (FactoryGroup(method.Name) < 0)
            {
                continue;
            }

            if (!IsTryFactory(method, type, out _) && !IsInstanceFactory(method, type))
            {
                continue;
            }

            matches.Add(method);
        }

        matches.Sort(static (left, right) =>
        {
            var order = FactoryGroup(left.Name).CompareTo(FactoryGroup(right.Name));

            if (order != 0)
            {
                return order;
            }

            order = string.CompareOrdinal(left.Name, right.Name);

            return order != 0
                ? order
                : left.GetParameters().Length.CompareTo(right.GetParameters().Length);
        });

        return matches;
    }

    /// <summary>
    ///     Reads the factory group of a method name.
    /// </summary>
    /// <param name="name">The method name to classify.</param>
    /// <returns>The group index, or <c>-1</c> when the name matches no group.</returns>
    private static int FactoryGroup(string name)
    {
        for (var index = 0; index < FactoryMethodGroups.Length; index++)
        {
            if (name.StartsWith(FactoryMethodGroups[index], StringComparison.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }

    /// <summary>
    ///     Determines whether a method is a <c>Try</c> factory answering one type through an <c>out</c> value.
    /// </summary>
    /// <param name="factory">The method to test.</param>
    /// <param name="type">The type the factory must answer.</param>
    /// <param name="outIndex">When this method returns <see langword="true" />, the <c>out</c> parameter index.</param>
    /// <returns><see langword="true" /> when the method answers the type through an <c>out</c> value.</returns>
    private static bool IsTryFactory(MethodInfo factory, Type type, out int outIndex)
    {
        outIndex = -1;

        if (factory.ReturnType != typeof(bool))
        {
            return false;
        }

        var parameters = factory.GetParameters();

        if (parameters.Length == 0)
        {
            return false;
        }

        var candidate = parameters[^1];

        if (!candidate.IsOut || !candidate.ParameterType.IsByRef)
        {
            return false;
        }

        if (candidate.ParameterType.GetElementType() is not { } element || !type.IsAssignableFrom(element))
        {
            return false;
        }

        for (var index = 0; index < parameters.Length - 1; index++)
        {
            if (parameters[index].ParameterType.IsByRef)
            {
                return false;
            }
        }

        outIndex = parameters.Length - 1;

        return true;
    }

    /// <summary>
    ///     Determines whether a method is a factory answering one type through its return value.
    /// </summary>
    /// <param name="factory">The method to test.</param>
    /// <param name="type">The type the factory must answer.</param>
    /// <returns><see langword="true" /> when the method returns the type and takes values only.</returns>
    private static bool IsInstanceFactory(MethodInfo factory, Type type)
    {
        if (factory.ReturnType == typeof(void) || !type.IsAssignableFrom(factory.ReturnType))
        {
            return false;
        }

        foreach (var parameter in factory.GetParameters())
        {
            if (parameter.ParameterType.IsByRef)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    ///     Invokes one static factory with sampled arguments.
    /// </summary>
    /// <param name="factory">The factory to invoke.</param>
    /// <param name="type">The type the factory must answer.</param>
    /// <param name="context">The sampling context carrying cycle protection and the failure diagnosis.</param>
    /// <param name="instance">When this method returns <see langword="true" />, the produced instance.</param>
    /// <returns><see langword="true" /> when the factory answered; otherwise, <see langword="false" />.</returns>
    private bool TryInvokeFactory(MethodInfo factory, Type type, SamplingContext context, out object? instance)
    {
        var parameters = factory.GetParameters();

        if (IsTryFactory(factory, type, out var outIndex))
        {
            if (!TrySampleArguments(parameters, outIndex, context, out var arguments))
            {
                instance = null;

                return false;
            }

            return TryAttempt(
                arguments,
                args =>
                {
                    var accepted = (bool) factory.Invoke(null, args)!;

                    return accepted ? args[outIndex] : null;
                },
                static candidate => candidate is not null,
                out instance);
        }

        if (!TrySampleArguments(parameters, -1, context, out var plain))
        {
            instance = null;

            return false;
        }

        return TryAttempt(
            plain,
            args => factory.Invoke(null, args),
            result => type.IsInstanceOfType(result),
            out instance);
    }

    /// <summary>
    ///     Invokes one constructor with sampled arguments.
    /// </summary>
    /// <param name="constructor">The constructor to invoke.</param>
    /// <param name="context">The sampling context carrying cycle protection and the failure diagnosis.</param>
    /// <param name="instance">When this method returns <see langword="true" />, the constructed instance.</param>
    /// <returns><see langword="true" /> when the constructor ran; otherwise, <see langword="false" />.</returns>
    private bool TryInvokeConstructor(ConstructorInfo constructor, SamplingContext context, out object? instance)
    {
        var parameters = constructor.GetParameters();

        if (!TrySampleArguments(parameters, -1, context, out var arguments))
        {
            instance = null;

            return false;
        }

        return TryAttempt(
            arguments,
            argumentsToUse => constructor.Invoke(argumentsToUse),
            static result => result is not null,
            out instance);
    }

    /// <summary>
    ///     Samples the arguments for one factory or constructor invocation.
    /// </summary>
    /// <param name="parameters">The parameters to fill.</param>
    /// <param name="outIndex">The <c>out</c> parameter index, or <c>-1</c> when no parameter is filled by the callee.</param>
    /// <param name="context">The sampling context carrying cycle protection and the failure diagnosis.</param>
    /// <param name="arguments">When this method returns <see langword="true" />, the sampled arguments.</param>
    /// <returns><see langword="true" /> when every parameter sampled; otherwise, <see langword="false" />.</returns>
    private bool TrySampleArguments(ParameterInfo[] parameters, int outIndex, SamplingContext context, out object?[] arguments)
    {
        arguments = new object?[parameters.Length];

        for (var index = 0; index < parameters.Length; index++)
        {
            if (index == outIndex)
            {
                continue;
            }

            var parameter = parameters[index];
            context.Path.Add(parameter.Name ?? $"[{index}]");

            try
            {
                var argument = TryCreateSample(parameter.ParameterType, false, context);

                if (argument is null)
                {
                    context.ReportFailure(parameter.ParameterType, "no sample is available for this parameter");

                    return false;
                }

                arguments[index] = argument;
            }
            finally
            {
                context.Path.RemoveAt(context.Path.Count - 1);
            }
        }

        return true;
    }

    /// <summary>
    ///     Runs one factory or constructor attempt and validates its answer.
    /// </summary>
    /// <param name="arguments">The arguments to invoke with.</param>
    /// <param name="attempt">The invocation, answering the candidate or <see langword="null" /> when rejected.</param>
    /// <param name="accept">Whether an answered candidate is usable.</param>
    /// <param name="instance">When this method returns <see langword="true" />, the produced instance.</param>
    /// <returns><see langword="true" /> when the attempt answered a usable instance.</returns>
    private static bool TryAttempt(
        object?[] arguments,
        Func<object?[], object?> attempt,
        Func<object?, bool> accept,
        out object? instance)
    {
        instance = null;

        object? result;

        try
        {
            result = attempt(arguments);
        }
        catch (Exception exception) when (IsSamplingInvocationException(exception))
        {
            return false;
        }

        if (result is null || !accept(result))
        {
            return false;
        }

        instance = result;

        return true;
    }

    /// <summary>
    ///     Determines whether an invocation failure moves sampling to the next attempt.
    /// </summary>
    /// <param name="exception">The exception the invocation raised.</param>
    /// <returns><see langword="true" /> for validation and reflection failures; otherwise, <see langword="false" />.</returns>
    /// <remarks>
    ///     A factory or constructor that guards its arguments, and reflection invoking it, raise these types.
    ///     Anything else escapes, because an unexpected failure in a guard is a defect to surface rather than a
    ///     rejection to work around. Reflection wraps a guard failure in
    ///     <see cref="TargetInvocationException" />, so the wrapper always moves on.
    /// </remarks>
    private static bool IsSamplingInvocationException(Exception exception)
    {
        return exception is ArgumentException
            or FormatException
            or InvalidOperationException
            or OverflowException
            or NotSupportedException
            or MemberAccessException
            or TargetParameterCountException
            or TargetInvocationException;
    }

    /// <summary>
    ///     Samples an abstract class through a parameterless static member answering it.
    /// </summary>
    /// <param name="type">The abstract class to sample.</param>
    /// <param name="context">The sampling context carrying cycle protection and the failure diagnosis.</param>
    /// <param name="instance">When this method returns <see langword="true" />, the sampled instance.</param>
    /// <returns><see langword="true" /> when a member answered; otherwise, <see langword="false" />.</returns>
    /// <remarks>
    ///     Closed sets expose their cases through members such as an <c>All</c> list, which is the only
    ///     construction path when the cases hide their constructors. Members are read by name order, and an
    ///     enumerable member answers its first usable item. The instance is returned unfilled, because a
    ///     canonical member value is shared state that sampling must not mutate.
    /// </remarks>
    private static bool TryCreateFromInstanceMember(Type type, SamplingContext context, out object? instance)
    {
        List<(string Name, Func<object?> Read)> members = [];

        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Static))
        {
            if (property.CanRead && property.GetIndexParameters().Length == 0)
            {
                members.Add((property.Name, () => property.GetValue(null)));
            }
        }

        foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (!field.IsSpecialName)
            {
                members.Add((field.Name, () => field.GetValue(null)));
            }
        }

        foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Static))
        {
            if (!method.IsSpecialName
                && !method.ContainsGenericParameters
                && method.GetParameters().Length == 0
                && method.ReturnType != typeof(void))
            {
                members.Add((method.Name, () => method.Invoke(null, null)));
            }
        }

        members.Sort(static (left, right) => string.CompareOrdinal(left.Name, right.Name));

        foreach (var (_, read) in members)
        {
            object? value;

            try
            {
                value = read();
            }
            catch (Exception exception) when (IsSamplingInvocationException(exception))
            {
                continue;
            }

            if (value is null)
            {
                continue;
            }

            if (type.IsInstanceOfType(value))
            {
                instance = value;

                return true;
            }

            if (value is IEnumerable items and not string
                && TryReadFirst(items, type, out instance)
                && instance is not null)
            {
                return true;
            }
        }

        instance = null;

        return false;
    }

    /// <summary>
    ///     Reads the first usable item of an enumerable member value.
    /// </summary>
    /// <param name="items">The enumerable to read.</param>
    /// <param name="type">The type the item must answer.</param>
    /// <param name="first">When this method returns <see langword="true" />, the first usable item.</param>
    /// <returns><see langword="true" /> when the first item is usable; otherwise, <see langword="false" />.</returns>
    private static bool TryReadFirst(IEnumerable items, Type type, out object? first)
    {
        first = null;

        try
        {
            foreach (var item in items)
            {
                if (item is not null && type.IsInstanceOfType(item))
                {
                    first = item;

                    return true;
                }

                return false;
            }
        }
        catch (Exception exception) when (IsSamplingInvocationException(exception))
        {
            return false;
        }

        return false;
    }

    /// <summary>
    ///     Samples an abstract class through a concrete derived type from the same assembly.
    /// </summary>
    /// <param name="type">The abstract class to sample.</param>
    /// <param name="fillNested">
    ///     <see langword="true" /> to fill nested reference values; <see langword="false" /> to fill leaf values
    ///     only, which bounds sampling to one level of nested objects.
    /// </param>
    /// <param name="context">The sampling context carrying cycle protection and the failure diagnosis.</param>
    /// <param name="instance">When this method returns <see langword="true" />, the sampled instance.</param>
    /// <returns><see langword="true" /> when a derived type sampled; otherwise, <see langword="false" />.</returns>
    private bool TryCreateFromDerivedType(Type type, bool fillNested, SamplingContext context, out object? instance)
    {
        Type[] candidates;

        try
        {
            candidates = type.Assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            List<Type> loaded = [];

            foreach (var loadedType in exception.Types)
            {
                if (loadedType is not null)
                {
                    loaded.Add(loadedType);
                }
            }

            candidates = [.. loaded];
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            context.ReportFailure(type, "the assembly types cannot be enumerated");
            instance = null;

            return false;
        }

        List<Type> matches = [];

        foreach (var candidate in candidates)
        {
            if (!candidate.IsClass || candidate.IsAbstract || candidate.ContainsGenericParameters)
            {
                continue;
            }

            if (!type.IsAssignableFrom(candidate))
            {
                continue;
            }

            matches.Add(candidate);
        }

        matches.Sort(static (left, right) => string.CompareOrdinal(left.FullName ?? left.Name, right.FullName ?? right.Name));

        foreach (var match in matches)
        {
            var value = TryCreateSample(match, fillNested, context);

            if (value is not null)
            {
                instance = value;

                return true;
            }
        }

        instance = null;

        return false;
    }

    /// <summary>
    ///     Orders constructors for trial: reference types by ascending parameter count, value types with the
    ///     parameterless constructor last.
    /// </summary>
    /// <param name="type">The type the constructors build.</param>
    /// <param name="constructors">The constructors to order.</param>
    private static void OrderConstructors(Type type, ConstructorInfo[] constructors)
    {
        if (type.IsValueType)
        {
            Array.Sort(
                constructors,
                static (left, right) => ValueConstructorOrder(left).CompareTo(ValueConstructorOrder(right)));
        }
        else
        {
            Array.Sort(
                constructors,
                static (left, right) => left.GetParameters().Length.CompareTo(right.GetParameters().Length));
        }
    }

    /// <summary>
    ///     Reads the trial order of one value type constructor.
    /// </summary>
    /// <param name="constructor">The constructor to order.</param>
    /// <returns>The parameter count, or the largest value for the parameterless constructor.</returns>
    private static int ValueConstructorOrder(ConstructorInfo constructor)
    {
        var count = constructor.GetParameters().Length;

        return count == 0 ? int.MaxValue : count;
    }

    /// <summary>
    ///     Sets writable public properties from the sample recipe, skipping one whose setter throws.
    /// </summary>
    /// <param name="instance">The instance to fill.</param>
    /// <param name="type">The instance type.</param>
    /// <param name="fillNested">
    ///     <see langword="true" /> to fill nested reference values; <see langword="false" /> to fill leaf values
    ///     only, which bounds sampling to one level of nested objects.
    /// </param>
    /// <param name="context">The sampling context carrying cycle protection.</param>
    /// <remarks>
    ///     A property fill is optional: a skipped property leaves its default rather than failing the sample.
    ///     Fills run in a detached context that shares cycle protection but discards diagnoses, so a skipped
    ///     property never misreports as the cause when construction fails elsewhere.
    /// </remarks>
    private void FillProperties(object instance, Type type, bool fillNested, SamplingContext context)
    {
        var optional = context.ForProperty();

        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!property.CanWrite || property.GetIndexParameters().Length != 0)
            {
                continue;
            }

            if (!fillNested && !IsLeaf(property.PropertyType))
            {
                continue;
            }

            var value = TryCreateSample(property.PropertyType, false, optional);

            if (value is null)
            {
                continue;
            }

            try
            {
                property.SetValue(instance, value);
            }
            catch (Exception exception) when (exception is ArgumentException or TargetInvocationException or MemberAccessException or TargetParameterCountException)
            {
                continue;
            }
        }
    }

    /// <summary>
    ///     Names a type the way failure reasons identify it.
    /// </summary>
    /// <param name="type">The type to name.</param>
    /// <returns>The full type name, or the type name when no full name exists.</returns>
    private static string TypeName(Type type)
    {
        return type.FullName ?? type.Name;
    }

    /// <summary>
    ///     Truncates an exception message to the length a failure reason keeps.
    /// </summary>
    /// <param name="value">The message to truncate.</param>
    /// <returns>The message, truncated to a bounded length.</returns>
    private static string Truncate(string value)
    {
        return value.Length > MaxSampleExceptionLength ? value[..MaxSampleExceptionLength] + "..." : value;
    }

    /// <summary>
    ///     Carries cycle protection and the failure diagnosis across one sampling run.
    /// </summary>
    private sealed class SamplingContext
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="SamplingContext" /> class.
        /// </summary>
        public SamplingContext()
        {
            Active = [];
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="SamplingContext" /> class with shared cycle protection.
        /// </summary>
        /// <param name="active">The cycle guard shared with the parent run.</param>
        private SamplingContext(HashSet<Type> active)
        {
            Active = active;
        }

        /// <summary>
        ///     Gets the types already being constructed up the call stack, which guards cycles.
        /// </summary>
        public HashSet<Type> Active { get; }

        /// <summary>
        ///     Gets the member path segments from the sampled root to the value under construction.
        /// </summary>
        public List<string> Path { get; } = [];

        /// <summary>
        ///     Gets the first sampling failure recorded during the run.
        /// </summary>
        public MessageSampleFailure? Failure { get; private set; }

        /// <summary>
        ///     Creates a context for optional property fills.
        /// </summary>
        /// <returns>A context sharing cycle protection with a discarded diagnosis.</returns>
        public SamplingContext ForProperty()
        {
            return new SamplingContext(Active);
        }

        /// <summary>
        ///     Records a sampling failure when none is recorded yet.
        /// </summary>
        /// <param name="failedType">The type that could not be sampled.</param>
        /// <param name="reason">Why the type cannot be sampled.</param>
        /// <remarks>
        ///     The first failure wins, because sampling unwinds from the deepest unsampleable value and the
        ///     deepest failure names the member to fix.
        /// </remarks>
        public void ReportFailure(Type failedType, string reason)
        {
            Failure ??= new MessageSampleFailure(failedType, FormatPath(), reason);
        }

        /// <summary>
        ///     Formats the member path from the sampled root to the value under construction.
        /// </summary>
        /// <returns>The path, or the empty string at the root.</returns>
        private string FormatPath()
        {
            if (Path.Count == 0)
            {
                return string.Empty;
            }

            var path = new StringBuilder(Path[0]);

            for (var index = 1; index < Path.Count; index++)
            {
                if (Path[index].StartsWith('['))
                {
                    path.Append(Path[index]);
                }
                else
                {
                    path.Append('.');
                    path.Append(Path[index]);
                }
            }

            return path.ToString();
        }
    }
}
