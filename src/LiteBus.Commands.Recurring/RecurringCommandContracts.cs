using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Commands.Abstractions;
using LiteBus.Runtime.Abstractions.Exceptions;

namespace LiteBus.Commands.Recurring;

/// <summary>
///     Resolves the command contract a recurring command implements and closes the mediator overload over it.
/// </summary>
/// <remarks>
///     The result type is only known at runtime, because a recurring command is registered by its own type rather
///     than by the pair of type and result. The closed delegate is cached per command type and result type, so the
///     reflection cost is paid once per registration rather than once per pass.
/// </remarks>
internal static class RecurringCommandContracts
{
    /// <summary>
    ///     Caches closed send delegates keyed by the command's runtime type and the declared result type.
    /// </summary>
    private static readonly ConcurrentDictionary<(Type CommandType, Type ResultType), Func<ICommandMediator, object, CancellationToken, Task<object?>>> SendDelegates = new();

    /// <summary>
    ///     The open generic helper closed over each command's declared result type.
    /// </summary>
    private static readonly MethodInfo SendTypedAsyncMethod = typeof(RecurringCommandContracts)
                                                                  .GetMethod(nameof(SendTypedAsync), BindingFlags.NonPublic | BindingFlags.Static)
                                                              ?? throw new InvalidOperationException($"Could not resolve {nameof(SendTypedAsync)}.");

    /// <summary>
    ///     Finds the result type a command declares through <see cref="ICommand{TCommandResult}" />.
    /// </summary>
    /// <param name="commandType">The command's runtime type.</param>
    /// <returns>The single declared result type, or <see langword="null" /> when the command produces no result.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="commandType" /> is <see langword="null" />.</exception>
    /// <exception cref="LiteBusConfigurationException">
    ///     The command implements more than one <see cref="ICommand{TCommandResult}" /> contract.
    /// </exception>
    internal static Type? FindResultContract(Type commandType)
    {
        ArgumentNullException.ThrowIfNull(commandType);

        var resultTypes = commandType
            .GetInterfaces()
            .Where(static contract => contract.IsGenericType && contract.GetGenericTypeDefinition() == typeof(ICommand<>))
            .Select(static contract => contract.GetGenericArguments()[0])
            .ToArray();

        if (resultTypes.Length > 1)
        {
            throw new LiteBusConfigurationException(
                $"The command type '{commandType.FullName}' implements more than one ICommand<> contract. "
                + "A recurring command must declare at most one result type.");
        }

        return resultTypes.SingleOrDefault();
    }

    /// <summary>
    ///     Sends a command that produces a result through the mediator's generic overload.
    /// </summary>
    /// <param name="mediator">The command mediator resolved for this pass.</param>
    /// <param name="command">The command instance to send.</param>
    /// <param name="resultType">The result type the command declares.</param>
    /// <param name="cancellationToken">A token that stops the send when the host shuts down.</param>
    /// <returns>The command result boxed as an object.</returns>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="mediator" />, <paramref name="command" /> or <paramref name="resultType" /> is
    ///     <see langword="null" />.
    /// </exception>
    internal static Task<object?> SendAsync(
        ICommandMediator mediator,
        object command,
        Type resultType,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(mediator);
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(resultType);

        var send = SendDelegates.GetOrAdd(
            (command.GetType(), resultType),
            static key => CreateSendDelegate(key.ResultType));

        return send(mediator, command, cancellationToken);
    }

    /// <summary>
    ///     Builds the closed send delegate for one result type.
    /// </summary>
    /// <param name="resultType">The result type the command declares.</param>
    /// <returns>The delegate that sends the command and returns its boxed result.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="resultType" /> is <see langword="null" />.</exception>
    private static Func<ICommandMediator, object, CancellationToken, Task<object?>> CreateSendDelegate(Type resultType)
    {
        ArgumentNullException.ThrowIfNull(resultType);

        return SendTypedAsyncMethod
            .MakeGenericMethod(resultType)
            .CreateDelegate<Func<ICommandMediator, object, CancellationToken, Task<object?>>>();
    }

    /// <summary>
    ///     Sends a command through <see cref="ICommandMediator.SendAsync{TCommandResult}" /> and boxes the result.
    /// </summary>
    /// <typeparam name="TCommandResult">The result type the command declares.</typeparam>
    /// <param name="mediator">The command mediator resolved for this pass.</param>
    /// <param name="command">The command instance to send.</param>
    /// <param name="cancellationToken">A token that stops the send when the host shuts down.</param>
    /// <returns>The command result boxed as an object.</returns>
    private static async Task<object?> SendTypedAsync<TCommandResult>(
        ICommandMediator mediator,
        object command,
        CancellationToken cancellationToken)
    {
        var result = await mediator
            .SendAsync((ICommand<TCommandResult>) command, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return result;
    }
}
