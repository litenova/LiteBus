using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
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
///         Construction tries the parameterless constructor first, then the remaining public constructors in
///         ascending parameter count, filling each parameter from the same recipe. The first constructor that
///         succeeds wins; when every constructor throws, the factory answers <see langword="null" /> so the
///         check reports the missing sample. After construction it sets writable public properties, skipping one
///         whose setter throws so a guarded setter does not fail the whole sample. Nested reference values
///         recurse one level with cycle protection, and anything deeper stays at its default.
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

    /// <inheritdoc />
    public ValueTask<object?> CreateSampleAsync(Type messageType, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(messageType);

        return new ValueTask<object?>(TryCreateSample(messageType, true, new HashSet<Type>()));
    }

    /// <summary>
    ///     Builds a sample value for one type, or answers <see langword="null" /> when the type cannot be sampled.
    /// </summary>
    /// <param name="type">The type to sample.</param>
    /// <param name="fillNested">
    ///     <see langword="true" /> to fill nested reference values; <see langword="false" /> to fill leaf values
    ///     only, which bounds sampling to one level of nested objects.
    /// </param>
    /// <param name="active">The types already being constructed up the call stack, which guards cycles.</param>
    /// <returns>The sample value, or <see langword="null" /> when the type cannot be sampled.</returns>
    private static object? TryCreateSample(Type type, bool fillNested, HashSet<Type> active)
    {
        if (type.IsAbstract || type.IsInterface)
        {
            return null;
        }

        if (Nullable.GetUnderlyingType(type) is { } underlying)
        {
            var value = TryCreateSample(underlying, fillNested, active);

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
                return null;
            }
        }

        if (TryCreateLeaf(type, out var leaf))
        {
            return leaf;
        }

        if (type.IsArray)
        {
            return TryCreateArray(type, active);
        }

        if (TryGetDictionaryArguments(type, out var keyType, out var valueType))
        {
            return TryCreateDictionary(keyType!, valueType!, active);
        }

        if (TryGetEnumerableElementType(type, out var elementType))
        {
            return TryCreateList(type, elementType!, active);
        }

        return TryCreateObject(type, fillNested, active);
    }

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
    /// <param name="active">The types already being constructed up the call stack, which guards cycles.</param>
    /// <returns>The sample array, or <see langword="null" /> when the element type cannot be sampled.</returns>
    private static Array? TryCreateArray(Type type, HashSet<Type> active)
    {
        if (type.GetArrayRank() != 1 || type.GetElementType() is not { } elementType)
        {
            return null;
        }

        var element = TryCreateSample(elementType, false, active);

        if (element is null)
        {
            return null;
        }

        var array = Array.CreateInstance(elementType, 1);
        array.SetValue(element, 0);

        return array;
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
    /// <param name="active">The types already being constructed up the call stack, which guards cycles.</param>
    /// <returns>The sample dictionary, or <see langword="null" /> when the key or value cannot be sampled.</returns>
    private static object? TryCreateDictionary(Type keyType, Type valueType, HashSet<Type> active)
    {
        var key = TryCreateSample(keyType, false, active);
        var value = TryCreateSample(valueType, false, active);

        if (key is null || value is null)
        {
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
    /// <param name="active">The types already being constructed up the call stack, which guards cycles.</param>
    /// <returns>The sample list, or <see langword="null" /> when the element type cannot be sampled.</returns>
    private static object? TryCreateList(Type type, Type elementType, HashSet<Type> active)
    {
        var element = TryCreateSample(elementType, false, active);

        if (element is null)
        {
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

            return null;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or TargetInvocationException or MemberAccessException)
        {
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
    ///     Builds a sample object through its public constructors and fills its writable properties.
    /// </summary>
    /// <param name="type">The type to sample.</param>
    /// <param name="fillNested">
    ///     <see langword="true" /> to fill nested reference values; <see langword="false" /> to fill leaf values
    ///     only, which bounds sampling to one level of nested objects.
    /// </param>
    /// <param name="active">The types already being constructed up the call stack, which guards cycles.</param>
    /// <returns>The sample instance, or <see langword="null" /> when the type cannot be constructed.</returns>
    private static object? TryCreateObject(Type type, bool fillNested, HashSet<Type> active)
    {
        if (!active.Add(type))
        {
            return null;
        }

        try
        {
            object instance;

            if (type.IsValueType)
            {
                instance = Activator.CreateInstance(type)!;
            }
            else
            {
                var constructors = type.GetConstructors();
                Array.Sort(constructors, static (left, right) => left.GetParameters().Length.CompareTo(right.GetParameters().Length));

                object? constructed = null;

                foreach (var constructor in constructors)
                {
                    if (TryInvokeConstructor(constructor, active, out var candidate) && candidate is not null)
                    {
                        constructed = candidate;
                        break;
                    }
                }

                if (constructed is null)
                {
                    return null;
                }

                instance = constructed;
            }

            FillProperties(instance, type, fillNested, active);

            return instance;
        }
        finally
        {
            active.Remove(type);
        }
    }

    /// <summary>
    ///     Invokes one constructor with sampled arguments.
    /// </summary>
    /// <param name="constructor">The constructor to invoke.</param>
    /// <param name="active">The types already being constructed up the call stack, which guards cycles.</param>
    /// <param name="instance">When this method returns <see langword="true" />, the constructed instance.</param>
    /// <returns><see langword="true" /> when the constructor ran; otherwise, <see langword="false" />.</returns>
    private static bool TryInvokeConstructor(ConstructorInfo constructor, HashSet<Type> active, out object? instance)
    {
        var parameters = constructor.GetParameters();
        var arguments = new object?[parameters.Length];

        for (var index = 0; index < parameters.Length; index++)
        {
            var argument = TryCreateSample(parameters[index].ParameterType, false, active);

            if (argument is null)
            {
                instance = null;

                return false;
            }

            arguments[index] = argument;
        }

        try
        {
            instance = constructor.Invoke(arguments);

            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or TargetInvocationException or MemberAccessException or TargetParameterCountException)
        {
            instance = null;

            return false;
        }
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
    /// <param name="active">The types already being constructed up the call stack, which guards cycles.</param>
    private static void FillProperties(object instance, Type type, bool fillNested, HashSet<Type> active)
    {
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

            var value = TryCreateSample(property.PropertyType, false, active);

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
}
