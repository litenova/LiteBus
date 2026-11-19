using System.Text.Json.Serialization;

namespace LiteBus.Extensions.AspNetCore;

/// <summary>
///     One validation failure in the RFC 9457 <c>errors</c> array shape.
/// </summary>
/// <param name="Detail">What is wrong, written for a person.</param>
/// <param name="JsonPointer">The JSON pointer (RFC 6901), written as the <c>pointer</c> member, of the invalid value, or <see langword="null" /> when the failure is about the message as a whole.</param>
/// <param name="Code">The machine-readable code for the failure, or <see langword="null" /> when the validator supplied none.</param>
public sealed record ValidationErrorEntry(string Detail, [property: JsonPropertyName("pointer")] string? JsonPointer, string? Code);
