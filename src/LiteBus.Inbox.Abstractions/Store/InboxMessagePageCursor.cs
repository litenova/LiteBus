using System;
using System.Text;
using System.Text.Json;

namespace LiteBus.Inbox.Abstractions;

/// <summary>
///     Encodes and decodes keyset pagination cursors for inbox message queries.
/// </summary>
public static class InboxMessagePageCursor
{
    /// <summary>
    ///     Encodes a keyset cursor from the last row of a page.
    /// </summary>
    /// <param name="createdAt">The created timestamp of the last row in the page.</param>
    /// <param name="messageId">The message identifier of the last row in the page.</param>
    /// <returns>A Base64-encoded JSON cursor.</returns>
    public static string Encode(DateTimeOffset createdAt, Guid messageId)
    {
        var payload = JsonSerializer.Serialize(new CursorPayload(createdAt, messageId));
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(payload));
    }

    /// <summary>
    ///     Decodes a keyset cursor supplied by a caller.
    /// </summary>
    /// <param name="cursor">The opaque cursor from a previous page.</param>
    /// <param name="createdAt">The decoded created timestamp when decoding succeeds.</param>
    /// <param name="messageId">The decoded message identifier when decoding succeeds.</param>
    /// <returns><see langword="true" /> when <paramref name="cursor" /> is a valid cursor; otherwise, <see langword="false" />.</returns>
    public static bool TryDecode(string? cursor, out DateTimeOffset createdAt, out Guid messageId)
    {
        createdAt = default;
        messageId = default;

        if (string.IsNullOrWhiteSpace(cursor))
        {
            return false;
        }

        try
        {
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
            var payload = JsonSerializer.Deserialize<CursorPayload>(json);

            if (payload is null)
            {
                return false;
            }

            createdAt = payload.CreatedAt;
            messageId = payload.MessageId;
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    ///     The JSON payload stored inside a Base64 cursor.
    /// </summary>
    /// <param name="CreatedAt">The created timestamp of the cursor row.</param>
    /// <param name="MessageId">The message identifier of the cursor row.</param>
    private sealed record CursorPayload(DateTimeOffset CreatedAt, Guid MessageId);
}
