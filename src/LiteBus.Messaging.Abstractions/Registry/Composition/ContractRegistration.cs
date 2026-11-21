using System;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     One registered message contract and the assembly that registered it.
/// </summary>
/// <param name="Name">The stable contract name written to stored envelopes.</param>
/// <param name="Version">The positive contract version written to stored envelopes.</param>
/// <param name="MessageType">The concrete CLR message type associated with the persisted contract.</param>
/// <param name="AssemblyName">The name of the assembly that registered the contract.</param>
/// <remarks>
///     <para>
///         Read through <see cref="ICompositionModel.Contracts" />. It carries the <see cref="MessageContract" /> shape
///         plus the registering assembly, so a composition check reports which assembly owns each contract rather than
///         only that it exists. The exported snapshot carries the same rows for CI diffs.
///     </para>
/// </remarks>
public sealed record ContractRegistration(
    string Name,
    int Version,
    Type MessageType,
    string AssemblyName);
