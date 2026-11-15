using System;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Commands.Abstractions;

/// <summary>
///     Reports how one command of a command batch ended.
/// </summary>
/// <param name="Index">The command's position in the list the batch was sent with.</param>
/// <param name="CommandType">The runtime type of the command.</param>
/// <param name="Executed"><see langword="true" /> when the command's pipeline ran; <see langword="false" /> when the batch stopped first.</param>
/// <param name="Result">The command's mediation result, or <see langword="null" /> when it did not run.</param>
public sealed record CommandBatchEntryResult(int Index, Type CommandType, bool Executed, MediationResult? Result);
