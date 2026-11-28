using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Runtime.Extensions.Microsoft.Hosting;

/// <summary>
///     Round-trips every registered durable message contract through the configured serializer and compares values.
/// </summary>
/// <remarks>
///     <para>
///         For each contract in <see cref="ICompositionModel.Contracts" />, the check builds a sample through the
///         resolved <see cref="IMessageSampleFactory" />, serializes and deserializes it through the resolved
///         <see cref="IMessageSerializer" />, and compares every readable public instance property with structural
///         equality. Each failure names the message type, the contract name and version, and the property.
///     </para>
///     <para>
///         Blocking applies: the check runs synchronously inside host validation, so it waits for the sample factory
///         and the serializer to complete. Both complete synchronously in their default implementations; a custom
///         factory must not depend on the calling context.
///     </para>
/// </remarks>
internal static class MessageContractRoundTripCheck
{
    /// <summary>
    ///     The section of a formatted value kept in a failure message.
    /// </summary>
    private const int MaxValueLength = 200;

    /// <summary>
    ///     The comparison depth past which values fall back to <see cref="object.Equals(object?, object?)" />.
    /// </summary>
    private const int MaxComparisonDepth = 32;

    /// <summary>
    ///     The number of enumerable items compared before the value reports as too large to compare.
    /// </summary>
    private const int MaxEnumerableItems = 10000;

    /// <summary>
    ///     Round-trips every registered durable message contract and records one failure per problem found.
    /// </summary>
    /// <param name="services">The root service provider built by the host.</param>
    /// <param name="options">The validation options selecting strict or lenient missing-sample handling.</param>
    /// <param name="failures">The failure messages collected so far.</param>
    /// <param name="skippedRoundTrips">The not-run entries collected so far.</param>
    /// <remarks>
    ///     Skipped silently when messaging was not composed: with no composition model or no serializer there are no
    ///     contracts to check. A missing sample factory fails every contract instead, because messaging composed
    ///     without one is a broken composition rather than an absent feature.
    /// </remarks>
    [RequiresUnreferencedCode("Round-trip comparison inspects contract message types via reflection.")]
    internal static void Validate(
        IServiceProvider services,
        LiteBusHostValidationOptions options,
        List<string> failures,
        List<string> skippedRoundTrips)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(failures);
        ArgumentNullException.ThrowIfNull(skippedRoundTrips);

        if (services.GetService(typeof(ICompositionModel)) is not ICompositionModel model
            || services.GetService(typeof(IMessageSerializer)) is not IMessageSerializer serializer)
        {
            return;
        }

        if (services.GetService(typeof(IMessageSampleFactory)) is not IMessageSampleFactory factory)
        {
            failures.Add(
                "message contract round trip: no service of type "
                + $"'{typeof(IMessageSampleFactory).FullName}' is registered, so no contract sample can be built.");
            return;
        }

        foreach (var contract in model.Contracts)
        {
            ValidateContract(contract, options, serializer, factory, failures, skippedRoundTrips);
        }
    }

#pragma warning disable CA1031 // A validation helper records failures of every type so one call reports all problems.
    /// <summary>
    ///     Round-trips one registered contract and records one failure per problem found.
    /// </summary>
    /// <param name="contract">The contract registration to check.</param>
    /// <param name="options">The validation options selecting strict or lenient missing-sample handling.</param>
    /// <param name="serializer">The configured message serializer.</param>
    /// <param name="factory">The configured message sample factory.</param>
    /// <param name="failures">The failure messages collected so far.</param>
    /// <param name="skippedRoundTrips">The not-run entries collected so far.</param>
    /// <remarks>
    ///     The catches stay broad because the sample factory and the serializer are application-replaceable ports:
    ///     a custom implementation throws exception types the check cannot enumerate, and a check that crashed on
    ///     the first custom failure would hide every failure after it.
    /// </remarks>
    [RequiresUnreferencedCode("Round-trip comparison inspects contract message types via reflection.")]
    private static void ValidateContract(
        ContractRegistration contract,
        LiteBusHostValidationOptions options,
        IMessageSerializer serializer,
        IMessageSampleFactory factory,
        List<string> failures,
        List<string> skippedRoundTrips)
    {
        var messageType = contract.MessageType;
        var typeName = messageType.FullName ?? messageType.Name;
        object? sample;

        try
        {
            sample = factory.CreateSampleAsync(messageType).AsTask().GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            failures.Add(
                $"message contract round trip for '{typeName}' (contract '{contract.Name}' version "
                + $"{contract.Version}): the sample factory threw {exception.GetType().FullName}: {exception.Message}");
            return;
        }

        if (sample is null)
        {
            var diagnosis = DescribeFailure(factory, messageType);
            var entry = FormatMissingSample(contract, typeName, diagnosis, options.FailOnMissingSample);

            if (options.FailOnMissingSample)
            {
                failures.Add(entry);
            }
            else
            {
                skippedRoundTrips.Add(entry);
            }

            return;
        }

        string payload;

        try
        {
            payload = SerializeWithMessageType(serializer, messageType, sample);
        }
        catch (Exception exception)
        {
            var reported = exception is TargetInvocationException { InnerException: { } inner }
                ? inner
                : exception;
            failures.Add(
                $"message contract round trip for '{typeName}' (contract '{contract.Name}' version "
                + $"{contract.Version}): serialization threw {reported.GetType().FullName}: {reported.Message}");
            return;
        }

        object roundTripped;

        try
        {
            roundTripped = serializer.DeserializeAsync(messageType, payload).GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            failures.Add(
                $"message contract round trip for '{typeName}' (contract '{contract.Name}' version "
                + $"{contract.Version}): deserialization threw {exception.GetType().FullName}: {exception.Message}");
            return;
        }

        CompareProperties(contract, typeName, sample, roundTripped, failures);
    }

    /// <summary>
    ///     Asks the sample factory why a message type cannot be sampled.
    /// </summary>
    /// <param name="factory">The configured message sample factory.</param>
    /// <param name="messageType">The message type that cannot be sampled.</param>
    /// <returns>The sampling failure, or <see langword="null" /> when the factory carries no detail.</returns>
    /// <remarks>
    ///     The catch stays broad because the factory is an application-replaceable port: a custom diagnosis
    ///     throws exception types the check cannot enumerate, and a throwing diagnosis must fall back to the
    ///     generic message rather than crash validation.
    /// </remarks>
    private static MessageSampleFailure? DescribeFailure(IMessageSampleFactory factory, Type messageType)
    {
        try
        {
            return factory.DescribeFailureAsync(messageType).AsTask().GetAwaiter().GetResult();
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    ///     Formats the failure or not-run entry for a contract no sample factory can sample.
    /// </summary>
    /// <param name="contract">The contract registration under check.</param>
    /// <param name="typeName">The display name of the message type.</param>
    /// <param name="diagnosis">The sampling failure, or <see langword="null" /> when the factory carries no detail.</param>
    /// <param name="failOnMissingSample"><see langword="true" /> for a failure entry; otherwise, a not-run entry.</param>
    /// <returns>The formatted entry.</returns>
    private static string FormatMissingSample(
        ContractRegistration contract,
        string typeName,
        MessageSampleFailure? diagnosis,
        bool failOnMissingSample)
    {
        var outcome = failOnMissingSample ? "no sample is available" : "not run: no sample is available";
        var header = $"message contract round trip for '{typeName}' (contract '{contract.Name}' version "
            + $"{contract.Version}): {outcome}";

        if (diagnosis is null)
        {
            return header
                + $". Register a sample with 'MessageModuleBuilder.RegisterSample<{contract.MessageType.Name}>' or an "
                + $"'{nameof(IMessageSampleFactory)}' that samples this type, or switch the round-trip check off."
                + LenientNote(failOnMissingSample);
        }

        var failedName = diagnosis.FailedType.FullName ?? diagnosis.FailedType.Name;
        var location = diagnosis.MemberPath.Length == 0
            ? $"type '{failedName}'"
            : $"member '{diagnosis.MemberPath}' (type '{failedName}')";

        return header
            + $": {location} cannot be sampled: {diagnosis.Reason}. Register a sample with "
            + $"'MessageModuleBuilder.RegisterSample<{diagnosis.FailedType.Name}>' or an "
            + $"'{nameof(IMessageSampleFactory)}' that samples this type, or switch the round-trip check off."
            + LenientNote(failOnMissingSample);
    }

    /// <summary>
    ///     Formats the trailing note of a not-run entry.
    /// </summary>
    /// <param name="failOnMissingSample"><see langword="true" /> for a failure entry; otherwise, a not-run entry.</param>
    /// <returns>The note, or the empty string for a failure entry.</returns>
    private static string LenientNote(bool failOnMissingSample)
    {
        return failOnMissingSample
            ? string.Empty
            : " The round trip did not run because 'FailOnMissingSample' is 'false'.";
    }
#pragma warning restore CA1031

    /// <summary>
    ///     Serializes a sample through the serializer closed over the contract message type.
    /// </summary>
    /// <param name="serializer">The configured message serializer.</param>
    /// <param name="messageType">The contract message type closing the generic call.</param>
    /// <param name="sample">The sample to serialize.</param>
    /// <returns>The serialized payload.</returns>
    /// <remarks>
    ///     The sample is held as <see cref="object" />, so an inferred call would close the serializer over
    ///     <see cref="object" />. The default serializer reads the runtime type, but a custom serializer may honor
    ///     the compile-time type, so the check closes the call over the contract message type.
    /// </remarks>
    [RequiresUnreferencedCode("Serialization closes the generic serializer call over the contract message type.")]
    private static string SerializeWithMessageType(IMessageSerializer serializer, Type messageType, object sample)
    {
        var serialize = typeof(IMessageSerializer)
            .GetMethod(nameof(IMessageSerializer.SerializeAsync))!
            .MakeGenericMethod(messageType);
        var task = (Task<string>) serialize.Invoke(serializer, [sample, CancellationToken.None])!;

        return task.GetAwaiter().GetResult();
    }

    /// <summary>
    ///     Compares every readable public instance property of a sample with its round-tripped copy.
    /// </summary>
    /// <param name="contract">The contract registration under check.</param>
    /// <param name="typeName">The display name of the message type.</param>
    /// <param name="sample">The sample before serialization.</param>
    /// <param name="roundTripped">The sample after serialization and deserialization.</param>
    /// <param name="failures">The failure messages collected so far.</param>
    private static void CompareProperties(
        ContractRegistration contract,
        string typeName,
        object sample,
        object roundTripped,
        List<string> failures)
    {
        PropertyInfo[] properties;

        try
        {
            properties = contract.MessageType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        }
        catch (Exception exception) when (exception is ArgumentException || exception is NotSupportedException)
        {
            failures.Add(
                $"message contract round trip for '{typeName}' (contract '{contract.Name}' version "
                + $"{contract.Version}): properties could not be read: {exception.Message}");
            return;
        }

        foreach (var property in properties)
        {
            if (!property.CanRead || property.GetIndexParameters().Length > 0)
            {
                continue;
            }

            object? expected;
            object? actual;

            try
            {
                expected = property.GetValue(sample);
            }
            catch (Exception exception) when (exception is TargetInvocationException
                || exception is ArgumentException
                || exception is NotSupportedException)
            {
                failures.Add(
                    $"message contract round trip for '{typeName}' (contract '{contract.Name}' version "
                    + $"{contract.Version}): property '{property.Name}' could not be read from the sample: "
                    + $"{exception.GetType().Name}: {ReadInnerMessage(exception)}");
                continue;
            }

            try
            {
                actual = property.GetValue(roundTripped);
            }
            catch (Exception exception) when (exception is TargetInvocationException
                || exception is ArgumentException
                || exception is NotSupportedException)
            {
                failures.Add(
                    $"message contract round trip for '{typeName}' (contract '{contract.Name}' version "
                    + $"{contract.Version}): property '{property.Name}' could not be read after deserialization: "
                    + $"{exception.GetType().Name}: {ReadInnerMessage(exception)}");
                continue;
            }

            CompareValues(
                contract,
                typeName,
                property.Name,
                expected,
                actual,
                failures,
                new HashSet<object>(ReferenceEqualityComparer.Instance),
                0);
        }
    }

    /// <summary>
    ///     Compares two values with structural equality, recording one failure per difference found.
    /// </summary>
    /// <param name="contract">The contract registration under check.</param>
    /// <param name="typeName">The display name of the message type.</param>
    /// <param name="path">The property path of the values, such as <c>Lines[2].Sku</c>.</param>
    /// <param name="expected">The value before serialization.</param>
    /// <param name="actual">The value after serialization and deserialization.</param>
    /// <param name="failures">The failure messages collected so far.</param>
    /// <param name="visited">The expected-side values already compared up the call stack, which guards cycles.</param>
    /// <param name="depth">The current comparison depth.</param>
    /// <remarks>
    ///     Leaf values compare with <see cref="object.Equals(object?, object?)" />, which is structural for records
    ///     and value objects. Dictionaries compare key by key, enumerables element by element, and objects property
    ///     by property, because a round trip always allocates new collection and object instances: reference
    ///     equality would fault every message with a populated collection or nested object.
    /// </remarks>
    private static void CompareValues(
        ContractRegistration contract,
        string typeName,
        string path,
        object? expected,
        object? actual,
        List<string> failures,
        HashSet<object> visited,
        int depth)
    {
        if (ReferenceEquals(expected, actual))
        {
            return;
        }

        if (expected is null || actual is null)
        {
            failures.Add(
                $"message contract round trip for '{typeName}' (contract '{contract.Name}' version "
                + $"{contract.Version}): property '{path}' changed from {FormatValue(expected)} to "
                + $"{FormatValue(actual)}; the configured serializer does not preserve it.");
            return;
        }

        var expectedType = expected.GetType();
        var actualType = actual.GetType();

        if (expectedType != actualType)
        {
            failures.Add(
                $"message contract round trip for '{typeName}' (contract '{contract.Name}' version "
                + $"{contract.Version}): property '{path}' changed type from "
                + $"'{expectedType.FullName ?? expectedType.Name}' to '{actualType.FullName ?? actualType.Name}' "
                + "after the round trip.");
            return;
        }

        if (IsLeafValue(expectedType) || depth >= MaxComparisonDepth)
        {
            if (!Equals(expected, actual))
            {
                failures.Add(
                    $"message contract round trip for '{typeName}' (contract '{contract.Name}' version "
                    + $"{contract.Version}): property '{path}' changed from {FormatValue(expected)} to "
                    + $"{FormatValue(actual)}; the configured serializer does not preserve it.");
            }

            return;
        }

        if (!visited.Add(expected))
        {
            return;
        }

        try
        {
            if (expected is IDictionary expectedDictionary && actual is IDictionary actualDictionary)
            {
                CompareDictionaries(
                    contract,
                    typeName,
                    path,
                    expectedDictionary,
                    actualDictionary,
                    failures,
                    visited,
                    depth);
                return;
            }

            if (expected is IEnumerable expectedItems && actual is IEnumerable actualItems)
            {
                CompareEnumerables(
                    contract,
                    typeName,
                    path,
                    expectedItems,
                    actualItems,
                    failures,
                    visited,
                    depth);
                return;
            }

            CompareObjects(
                contract,
                typeName,
                path,
                expectedType,
                expected,
                actual,
                failures,
                visited,
                depth);
        }
        finally
        {
            visited.Remove(expected);
        }
    }

    /// <summary>
    ///     Determines whether a type compares as one leaf value rather than by structure.
    /// </summary>
    /// <param name="type">The value type to test.</param>
    /// <returns><see langword="true" /> when the type compares with <see cref="object.Equals(object?, object?)" />.</returns>
    private static bool IsLeafValue(Type type)
    {
        if (Nullable.GetUnderlyingType(type) is { } underlying)
        {
            return IsLeafValue(underlying);
        }

        if (type.IsEnum || type.IsPrimitive)
        {
            return true;
        }

        return type == typeof(string)
            || type == typeof(decimal)
            || type == typeof(Guid)
            || type == typeof(DateTime)
            || type == typeof(DateTimeOffset)
            || type == typeof(TimeSpan)
            || type == typeof(DateOnly)
            || type == typeof(TimeOnly)
            || type == typeof(Uri)
            || type == typeof(Version);
    }

    /// <summary>
    ///     Compares two dictionaries key by key, recording one failure per difference found.
    /// </summary>
    /// <param name="contract">The contract registration under check.</param>
    /// <param name="typeName">The display name of the message type.</param>
    /// <param name="path">The property path of the dictionaries.</param>
    /// <param name="expected">The dictionary before serialization.</param>
    /// <param name="actual">The dictionary after serialization and deserialization.</param>
    /// <param name="failures">The failure messages collected so far.</param>
    /// <param name="visited">The expected-side values already compared up the call stack, which guards cycles.</param>
    /// <param name="depth">The current comparison depth.</param>
    private static void CompareDictionaries(
        ContractRegistration contract,
        string typeName,
        string path,
        IDictionary expected,
        IDictionary actual,
        List<string> failures,
        HashSet<object> visited,
        int depth)
    {
        List<object> keys = [];

        try
        {
            foreach (var key in expected.Keys)
            {
                keys.Add(key);
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException
            || exception is ArgumentException
            || exception is NotSupportedException)
        {
            failures.Add(
                $"message contract round trip for '{typeName}' (contract '{contract.Name}' version "
                + $"{contract.Version}): property '{path}' could not be enumerated: {exception.Message}");
            return;
        }

        if (expected.Count != actual.Count)
        {
            failures.Add(
                $"message contract round trip for '{typeName}' (contract '{contract.Name}' version "
                + $"{contract.Version}): property '{path}' changed from {expected.Count} entries to "
                + $"{actual.Count} entries; the configured serializer does not preserve it.");
        }

        foreach (var key in keys)
        {
            if (key is null)
            {
                continue;
            }

            var entryPath = $"{path}[{FormatValue(key)}]";

            if (!actual.Contains(key))
            {
                failures.Add(
                    $"message contract round trip for '{typeName}' (contract '{contract.Name}' version "
                    + $"{contract.Version}): property '{entryPath}' is missing after the round trip; the configured "
                    + "serializer does not preserve it.");
                continue;
            }

            CompareValues(
                contract,
                typeName,
                entryPath,
                expected[key],
                actual[key],
                failures,
                visited,
                depth + 1);
        }
    }

    /// <summary>
    ///     Compares two enumerables element by element, recording one failure per difference found.
    /// </summary>
    /// <param name="contract">The contract registration under check.</param>
    /// <param name="typeName">The display name of the message type.</param>
    /// <param name="path">The property path of the enumerables.</param>
    /// <param name="expected">The enumerable before serialization.</param>
    /// <param name="actual">The enumerable after serialization and deserialization.</param>
    /// <param name="failures">The failure messages collected so far.</param>
    /// <param name="visited">The expected-side values already compared up the call stack, which guards cycles.</param>
    /// <param name="depth">The current comparison depth.</param>
    private static void CompareEnumerables(
        ContractRegistration contract,
        string typeName,
        string path,
        IEnumerable expected,
        IEnumerable actual,
        List<string> failures,
        HashSet<object> visited,
        int depth)
    {
        if (!TryReadItems(expected, out var expectedItems))
        {
            failures.Add(
                $"message contract round trip for '{typeName}' (contract '{contract.Name}' version "
                + $"{contract.Version}): property '{path}' could not be enumerated from the sample.");
            return;
        }

        if (!TryReadItems(actual, out var actualItems))
        {
            failures.Add(
                $"message contract round trip for '{typeName}' (contract '{contract.Name}' version "
                + $"{contract.Version}): property '{path}' could not be enumerated after deserialization.");
            return;
        }

        if (expectedItems.Count != actualItems.Count)
        {
            failures.Add(
                $"message contract round trip for '{typeName}' (contract '{contract.Name}' version "
                + $"{contract.Version}): property '{path}' changed from {expectedItems.Count} items to "
                + $"{actualItems.Count} items; the configured serializer does not preserve it.");
        }

        var shared = Math.Min(expectedItems.Count, actualItems.Count);

        for (var index = 0; index < shared; index++)
        {
            CompareValues(
                contract,
                typeName,
                $"{path}[{index}]",
                expectedItems[index],
                actualItems[index],
                failures,
                visited,
                depth + 1);
        }
    }

    /// <summary>
    ///     Reads enumerable items up to a bound, so an unbounded sequence cannot hang validation.
    /// </summary>
    /// <param name="items">The enumerable to read.</param>
    /// <param name="read">When this method returns <see langword="true" />, the items read.</param>
    /// <returns>
    ///     <see langword="true" /> when the enumerable was read within the bound; otherwise,
    ///     <see langword="false" />.
    /// </returns>
    private static bool TryReadItems(IEnumerable items, out List<object?> read)
    {
        read = [];

        try
        {
            foreach (var item in items)
            {
                if (read.Count >= MaxEnumerableItems)
                {
                    return false;
                }

                read.Add(item);
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException
            || exception is ArgumentException
            || exception is NotSupportedException)
        {
            return false;
        }

        return true;
    }

    /// <summary>
    ///     Compares two objects property by property, recording one failure per difference found.
    /// </summary>
    /// <param name="contract">The contract registration under check.</param>
    /// <param name="typeName">The display name of the message type.</param>
    /// <param name="path">The property path of the objects.</param>
    /// <param name="objectType">The shared runtime type of both values.</param>
    /// <param name="expected">The object before serialization.</param>
    /// <param name="actual">The object after serialization and deserialization.</param>
    /// <param name="failures">The failure messages collected so far.</param>
    /// <param name="visited">The expected-side values already compared up the call stack, which guards cycles.</param>
    /// <param name="depth">The current comparison depth.</param>
    private static void CompareObjects(
        ContractRegistration contract,
        string typeName,
        string path,
        Type objectType,
        object expected,
        object actual,
        List<string> failures,
        HashSet<object> visited,
        int depth)
    {
        PropertyInfo[] properties;

        try
        {
            properties = objectType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        }
        catch (Exception exception) when (exception is ArgumentException || exception is NotSupportedException)
        {
            failures.Add(
                $"message contract round trip for '{typeName}' (contract '{contract.Name}' version "
                + $"{contract.Version}): property '{path}' could not be enumerated: {exception.Message}");
            return;
        }

        foreach (var property in properties)
        {
            if (!property.CanRead || property.GetIndexParameters().Length > 0)
            {
                continue;
            }

            var propertyPath = $"{path}.{property.Name}";
            object? expectedValue;
            object? actualValue;

            try
            {
                expectedValue = property.GetValue(expected);
            }
            catch (Exception exception) when (exception is TargetInvocationException
                || exception is ArgumentException
                || exception is NotSupportedException)
            {
                failures.Add(
                    $"message contract round trip for '{typeName}' (contract '{contract.Name}' version "
                    + $"{contract.Version}): property '{propertyPath}' could not be read: "
                    + $"{exception.GetType().Name}: {ReadInnerMessage(exception)}");
                continue;
            }

            try
            {
                actualValue = property.GetValue(actual);
            }
            catch (Exception exception) when (exception is TargetInvocationException
                || exception is ArgumentException
                || exception is NotSupportedException)
            {
                failures.Add(
                    $"message contract round trip for '{typeName}' (contract '{contract.Name}' version "
                    + $"{contract.Version}): property '{propertyPath}' could not be read: "
                    + $"{exception.GetType().Name}: {ReadInnerMessage(exception)}");
                continue;
            }

            CompareValues(
                contract,
                typeName,
                propertyPath,
                expectedValue,
                actualValue,
                failures,
                visited,
                depth + 1);
        }
    }

    /// <summary>
    ///     Reads the message of the exception a throwing property getter raised.
    /// </summary>
    /// <param name="exception">The exception the getter raised.</param>
    /// <returns>The inner message when the getter threw through reflection, else the exception message.</returns>
    private static string ReadInnerMessage(Exception exception)
    {
        return exception is TargetInvocationException { InnerException: { } inner }
            ? inner.Message
            : exception.Message;
    }

    /// <summary>
    ///     Formats one property value for a failure message.
    /// </summary>
    /// <param name="value">The value to format.</param>
    /// <returns>The formatted value, truncated to a bounded length.</returns>
    private static string FormatValue(object? value)
    {
        if (value is null)
        {
            return "null";
        }

        string text;

        if (value is string textValue)
        {
            text = $"'{textValue}'";
        }
        else
        {
            try
            {
                text = value.ToString() ?? value.GetType().Name;
            }
            catch (Exception exception) when (exception is ArgumentException
                || exception is InvalidOperationException
                || exception is NotSupportedException)
            {
                text = value.GetType().Name;
            }
        }

        return text.Length > MaxValueLength ? text[..MaxValueLength] + "..." : text;
    }
}
