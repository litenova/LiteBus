using System;
using System.Reflection;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Write-only surface for message contract registration.
///     Consumed by module builders at configuration time.
/// </summary>
public interface IContractWriter
{
    /// <summary>
    ///     Maps <typeparamref name="TMessage" /> to the given durable contract
    ///     name and version.
    /// </summary>
    /// <typeparam name="TMessage">The concrete message type to register.</typeparam>
    /// <param name="name">The stable contract name stored in inbox and outbox envelopes.</param>
    /// <param name="version">The positive contract version stored with the payload.</param>
    /// <returns>The writer so module builders can chain registrations.</returns>
    IContractWriter Register<TMessage>(string name, int version = 1)
        where TMessage : notnull;

    /// <summary>
    ///     Maps <paramref name="messageType" /> to the given durable contract
    ///     name and version. Use when the CLR type is not known at compile time.
    /// </summary>
    /// <param name="messageType">The concrete message type to register.</param>
    /// <param name="name">The stable contract name stored in inbox and outbox envelopes.</param>
    /// <param name="version">The positive contract version stored with the payload.</param>
    /// <returns>The writer so module builders can chain registrations.</returns>
    IContractWriter Register(Type messageType, string name, int version = 1);

    /// <summary>
    ///     Scans <paramref name="assembly" /> for types decorated with
    ///     <see cref="MessageContractAttribute" /> and registers each one.
    ///     Equivalent to calling <see cref="Register(Type,string,int)" /> for
    ///     every attributed type found.
    /// </summary>
    /// <param name="assembly">The assembly to scan for <see cref="MessageContractAttribute" />.</param>
    /// <returns>The writer so module builders can chain registrations.</returns>
    IContractWriter AddFromAssembly(Assembly assembly);
}
